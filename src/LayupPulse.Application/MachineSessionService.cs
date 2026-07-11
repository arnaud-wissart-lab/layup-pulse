using System.Collections.Immutable;
using LayupPulse.Domain;

namespace LayupPulse.Application;

/// <summary>
/// Possède la session, le pipeline télémétrique et l’unique boucle de reconnexion de l’application.
/// </summary>
public sealed class MachineSessionService : IMachineSessionService
{
    private readonly IMachineGateway _gateway;
    private readonly IDemoFaultGateway? _demoFaultGateway;
    private readonly TimeProvider _timeProvider;
    private readonly MachineSessionOptions _options;
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly Queue<MachineDiagnosticMessage> _diagnostics = new();
    private MachineSessionState _state;
    private IMachineSession? _transportSession;
    private CancellationTokenSource? _connectionCancellation;
    private CancellationTokenSource? _streamAttemptCancellation;
    private Task? _supervisorTask;
    private Task? _freshnessMonitorTask;
    private TelemetryPipeline? _pipeline;
    private int _isDisposed;

    public MachineSessionService(
        IMachineGateway gateway,
        TimeProvider timeProvider,
        MachineSessionOptions options,
        IDemoFaultGateway? demoFaultGateway = null)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _gateway = gateway;
        _demoFaultGateway = demoFaultGateway;
        _timeProvider = timeProvider;
        _options = options;
        _state = CreateInitialState();
    }

    public event EventHandler<MachineSessionStateChangedEventArgs>? StateChanged;

    public MachineSessionState State
    {
        get
        {
            lock (_stateLock)
            {
                return _state;
            }
        }
    }

    public IReadOnlyList<TelemetrySample> GetTelemetryHistorySnapshot()
    {
        lock (_stateLock)
        {
            return _pipeline?.GetHistorySnapshot() ?? Array.Empty<TelemetrySample>();
        }
    }

    public async Task<MachineSessionOperationResult> ConnectAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        IMachineSession? connectedSession = null;
        CancellationTokenSource? connectionCancellation = null;
        TelemetryPipeline? pipeline = null;
        try
        {
            if (State.ConnectionStatus != MachineConnectionStatus.Disconnected)
            {
                return MachineSessionOperationResult.Failed(
                    "La session est déjà active ou en cours de modification.",
                    MachineGatewayFailureKind.CommandRejected);
            }

            UpdateState(
                state => state with
                {
                    ConnectionStatus = MachineConnectionStatus.Connecting,
                    LastCommunicationError = null,
                    LastFailureKind = null,
                },
                MachineDiagnosticLevel.Information,
                "Connexion au simulateur en cours…");

            connectionCancellation = new CancellationTokenSource();
            using CancellationTokenSource connectAttempt = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                connectionCancellation.Token);
            connectedSession = await _gateway.ConnectAsync(connectAttempt.Token).ConfigureAwait(false);
            MachineSnapshot snapshot = await _gateway
                .GetSnapshotAsync(connectedSession, connectAttempt.Token)
                .ConfigureAwait(false);

            pipeline = new TelemetryPipeline(
                _timeProvider,
                _options.Telemetry,
                new AlarmEngine(_timeProvider, _options.Alarms));
            pipeline.PublicationReady += OnPipelinePublicationReady;
            TelemetryPipelinePublicationEventArgs initialPublication = pipeline.GetCurrentPublication();
            DateTimeOffset connectedAt = _timeProvider.GetUtcNow();

            lock (_stateLock)
            {
                _transportSession = connectedSession;
                _connectionCancellation = connectionCancellation;
                _pipeline = pipeline;
            }

            UpdateState(
                state => state with
                {
                    ConnectionStatus = MachineConnectionStatus.Connected,
                    LatestSnapshot = snapshot,
                    LatestTelemetry = null,
                    SessionId = connectedSession.SessionId,
                    ConnectedAt = connectedSession.ConnectedAt,
                    LastSuccessfulCommunication = connectedAt,
                    ReceivedSampleCount = 0,
                    LastCommunicationError = null,
                    LastFailureKind = null,
                    TelemetryMetrics = initialPublication.Metrics,
                    LatestAggregate = null,
                    ActiveAlarms = initialPublication.ActiveAlarms,
                    AlarmHistory = initialPublication.AlarmHistory,
                },
                MachineDiagnosticLevel.Information,
                "Connexion au simulateur établie.");

            Task supervisorTask = SuperviseTelemetryAsync(connectedSession, connectionCancellation.Token);
            Task freshnessMonitorTask = MonitorFreshnessAsync(connectionCancellation.Token);
            lock (_stateLock)
            {
                _supervisorTask = supervisorTask;
                _freshnessMonitorTask = freshnessMonitorTask;
            }

            connectedSession = null;
            connectionCancellation = null;
            pipeline = null;
            return MachineSessionOperationResult.Successful("Connexion établie.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (connectedSession is not null)
            {
                await TryAbandonSessionAsync(connectedSession).ConfigureAwait(false);
            }

            pipeline?.Complete();
            SetDisconnected("Connexion annulée.", MachineDiagnosticLevel.Warning);
            throw;
        }
        catch (MachineGatewayException exception)
        {
            if (connectedSession is not null)
            {
                await TryAbandonSessionAsync(connectedSession).ConfigureAwait(false);
            }

            UpdateState(
                state => CreateDisconnectedState(state) with
                {
                    LastCommunicationError = exception.Message,
                    LastFailureKind = exception.FailureKind,
                },
                MachineDiagnosticLevel.Error,
                exception.Message);
            return MachineSessionOperationResult.Failed(exception.Message, exception.FailureKind);
        }
        finally
        {
            if (pipeline is not null)
            {
                pipeline.PublicationReady -= OnPipelinePublicationReady;
            }

            connectionCancellation?.Dispose();
            _operationLock.Release();
        }
    }

    public async Task<MachineSessionOperationResult> DisconnectAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed(allowDisposing: true);
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            IMachineSession? session;
            CancellationTokenSource? connectionCancellation;
            CancellationTokenSource? streamAttemptCancellation;
            Task? supervisorTask;
            Task? freshnessMonitorTask;
            TelemetryPipeline? pipeline;

            lock (_stateLock)
            {
                session = _transportSession;
                connectionCancellation = _connectionCancellation;
                streamAttemptCancellation = _streamAttemptCancellation;
                supervisorTask = _supervisorTask;
                freshnessMonitorTask = _freshnessMonitorTask;
                pipeline = _pipeline;
            }

            if (connectionCancellation is null)
            {
                SetDisconnected("Session déjà déconnectée.", MachineDiagnosticLevel.Information);
                return MachineSessionOperationResult.Successful("Session déjà déconnectée.");
            }

            UpdateState(
                state => state with { ConnectionStatus = MachineConnectionStatus.Disconnecting },
                MachineDiagnosticLevel.Information,
                "Déconnexion du simulateur en cours…");

            connectionCancellation.Cancel();
            streamAttemptCancellation?.Cancel();
            await AwaitBackgroundTasksAsync(supervisorTask, freshnessMonitorTask).ConfigureAwait(false);
            pipeline?.EvaluateCommunication(communicationExpected: false, communicationStartedAt: null);
            pipeline?.Complete();

            MachineGatewayException? failure = null;
            session = GetTransportSession();
            if (session is not null)
            {
                try
                {
                    await _gateway.DisconnectAsync(session, cancellationToken).ConfigureAwait(false);
                }
                catch (MachineGatewayException exception)
                {
                    failure = exception;
                }
            }

            CleanupConnection(connectionCancellation, pipeline);

            if (failure is not null)
            {
                UpdateState(
                    state => CreateDisconnectedState(state) with
                    {
                        LastCommunicationError = failure.Message,
                        LastFailureKind = failure.FailureKind,
                    },
                    MachineDiagnosticLevel.Warning,
                    $"Session fermée localement après une erreur : {failure.Message}");
                return MachineSessionOperationResult.Failed(failure.Message, failure.FailureKind);
            }

            SetDisconnected("Session déconnectée proprement.", MachineDiagnosticLevel.Information);
            return MachineSessionOperationResult.Successful("Déconnexion terminée.");
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<MachineCommandExecutionResult> ExecuteCommandAsync(
        MachineCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ThrowIfDisposed();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            IMachineSession? session = GetTransportSession();
            if (session is null || State.ConnectionStatus != MachineConnectionStatus.Connected)
            {
                return MachineCommandExecutionResult.NotConnected();
            }

            try
            {
                CommandResult result = await _gateway
                    .ExecuteCommandAsync(session, command, cancellationToken)
                    .ConfigureAwait(false);
                DateTimeOffset receivedAt = _timeProvider.GetUtcNow();
                string message = result.IsAccepted
                    ? $"Commande {command.Type} acceptée."
                    : result.Transition.Rejection?.Message ?? $"Commande {command.Type} rejetée.";

                UpdateState(
                    state => state with
                    {
                        LatestSnapshot = result.Transition.Snapshot,
                        LastSuccessfulCommunication = receivedAt,
                        LastCommunicationError = null,
                        LastFailureKind = null,
                    },
                    result.IsAccepted ? MachineDiagnosticLevel.Information : MachineDiagnosticLevel.Warning,
                    message);
                return MachineCommandExecutionResult.FromCommandResult(result) with { Message = message };
            }
            catch (MachineGatewayException exception)
            {
                SetRecoverableCommunicationFailure(exception);
                return MachineCommandExecutionResult.Failed(exception.Message, exception.FailureKind);
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<MachineSessionOperationResult> SetDemoFaultAsync(
        FaultType fault,
        bool active,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            IMachineSession? session = GetTransportSession();
            MachineConnectionStatus status = State.ConnectionStatus;
            if (_demoFaultGateway is null)
            {
                return MachineSessionOperationResult.Failed(
                    "Les contrôles de simulation ne sont pas disponibles.",
                    MachineGatewayFailureKind.CommandRejected);
            }

            if (session is null || status is not (
                MachineConnectionStatus.Connected
                or MachineConnectionStatus.Stale
                or MachineConnectionStatus.Reconnecting))
            {
                return MachineSessionOperationResult.Failed(
                    "Aucune session simulée n’est disponible pour modifier ce défaut.",
                    MachineGatewayFailureKind.Interrupted);
            }

            try
            {
                Guid correlationId = Guid.NewGuid();
                CommandResult result = active
                    ? await _demoFaultGateway
                        .InjectFaultAsync(session, correlationId, fault, cancellationToken)
                        .ConfigureAwait(false)
                    : await _demoFaultGateway
                        .ClearFaultAsync(session, correlationId, fault, cancellationToken)
                        .ConfigureAwait(false);
                string action = active ? "injecté" : "levé";
                string message = result.IsAccepted
                    ? $"Défaut simulé {fault} {action}."
                    : result.Transition.Rejection?.Message ?? $"Modification du défaut simulé {fault} rejetée.";

                UpdateState(
                    state => state with
                    {
                        LatestSnapshot = result.Transition.Snapshot,
                        LastSuccessfulCommunication = _timeProvider.GetUtcNow(),
                    },
                    result.IsAccepted ? MachineDiagnosticLevel.Warning : MachineDiagnosticLevel.Error,
                    message);
                return result.IsAccepted
                    ? MachineSessionOperationResult.Successful(message)
                    : MachineSessionOperationResult.Failed(message, MachineGatewayFailureKind.CommandRejected);
            }
            catch (MachineGatewayException exception)
            {
                SetRecoverableCommunicationFailure(exception);
                return MachineSessionOperationResult.Failed(exception.Message, exception.FailureKind);
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public bool AcknowledgeAlarm(Guid alarmId)
    {
        ThrowIfDisposed();
        TelemetryPipeline? pipeline;
        lock (_stateLock)
        {
            pipeline = _pipeline;
        }

        return pipeline?.AcknowledgeAlarm(alarmId) == true;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        try
        {
            await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _operationLock.Dispose();
        }
    }

    private async Task SuperviseTelemetryAsync(
        IMachineSession initialSession,
        CancellationToken cancellationToken)
    {
        IMachineSession? session = initialSession;
        int consecutiveFailures = 0;
        bool reconnecting = false;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (reconnecting)
                {
                    TelemetryPipeline? reconnectPipeline = GetPipeline();
                    reconnectPipeline?.RegisterReconnectAttempt();
                    ApplyPipelinePublication(reconnectPipeline?.GetCurrentPublication());
                    TimeSpan delay = CalculateReconnectDelay(consecutiveFailures);

                    try
                    {
                        await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    try
                    {
                        session = await PrepareReconnectSessionAsync(session, cancellationToken)
                            .ConfigureAwait(false);
                        reconnecting = false;
                    }
                    catch (MachineGatewayException exception)
                    {
                        consecutiveFailures++;
                        SetReconnecting(exception.FailureKind, exception.Message);
                        if (session is not null)
                        {
                            await TryAbandonSessionAsync(session).ConfigureAwait(false);
                            session = null;
                            SetTransportSession(null);
                        }

                        continue;
                    }
                }

                if (session is null)
                {
                    reconnecting = true;
                    continue;
                }

                bool receivedAnySample = false;
                string failureMessage = "Le flux de télémétrie s’est terminé sans demande de déconnexion.";
                MachineGatewayFailureKind failureKind = MachineGatewayFailureKind.Interrupted;
                using CancellationTokenSource streamAttempt = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                SetStreamAttemptCancellation(streamAttempt);

                try
                {
                    await foreach (TelemetrySample sample in _gateway
                        .StreamTelemetryAsync(session, streamAttempt.Token)
                        .WithCancellation(streamAttempt.Token)
                        .ConfigureAwait(false))
                    {
                        receivedAnySample = true;
                        GetPipeline()?.Accept(sample);
                        MachineConnectionStatus previousStatus = State.ConnectionStatus;
                        if (previousStatus is MachineConnectionStatus.Stale or MachineConnectionStatus.Reconnecting)
                        {
                            UpdateState(
                                state => state with
                                {
                                    ConnectionStatus = MachineConnectionStatus.Connected,
                                    LastCommunicationError = null,
                                    LastFailureKind = null,
                                },
                                MachineDiagnosticLevel.Information,
                                "Réception de la télémétrie rétablie.");
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException) when (streamAttempt.IsCancellationRequested)
                {
                    failureKind = MachineGatewayFailureKind.Timeout;
                    failureMessage = "Le flux télémétrique a été annulé après le délai de communication.";
                }
                catch (MachineGatewayException exception)
                {
                    failureKind = exception.FailureKind;
                    failureMessage = exception.Message;
                }
                finally
                {
                    ClearStreamAttemptCancellation(streamAttempt);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                consecutiveFailures = receivedAnySample ? 0 : consecutiveFailures + 1;
                reconnecting = true;
                SetReconnecting(failureKind, failureMessage);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            UpdateState(
                state => state with
                {
                    ConnectionStatus = MachineConnectionStatus.Disconnected,
                    LastCommunicationError = exception.Message,
                    LastFailureKind = MachineGatewayFailureKind.Unexpected,
                },
                MachineDiagnosticLevel.Error,
                $"La supervision de communication s’est arrêtée : {exception.Message}");
        }
    }

    private async Task<IMachineSession> PrepareReconnectSessionAsync(
        IMachineSession? session,
        CancellationToken cancellationToken)
    {
        if (session is not null)
        {
            try
            {
                MachineSnapshot snapshot = await _gateway
                    .GetSnapshotAsync(session, cancellationToken)
                    .ConfigureAwait(false);
                if (snapshot.State != MachineState.Disconnected)
                {
                    UpdateState(state => state with { LatestSnapshot = snapshot });
                    return session;
                }
            }
            catch (MachineGatewayException)
            {
                // La session locale est abandonnée ci-dessous avant une nouvelle connexion.
            }

            await TryAbandonSessionAsync(session).ConfigureAwait(false);
            SetTransportSession(null);
        }

        IMachineSession connectedSession = await _gateway.ConnectAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            MachineSnapshot snapshot = await _gateway
                .GetSnapshotAsync(connectedSession, cancellationToken)
                .ConfigureAwait(false);
            GetPipeline()?.BeginSequenceScope();
            SetTransportSession(connectedSession);
            UpdateState(
                state => state with
                {
                    LatestSnapshot = snapshot,
                    SessionId = connectedSession.SessionId,
                    ConnectedAt = connectedSession.ConnectedAt,
                });
            return connectedSession;
        }
        catch
        {
            await TryAbandonSessionAsync(connectedSession).ConfigureAwait(false);
            throw;
        }
    }

    private async Task MonitorFreshnessAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(_options.StaleCheckInterval, _timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                TelemetryPipeline? pipeline = GetPipeline();
                if (pipeline is null)
                {
                    continue;
                }

                MachineSessionState state = State;
                DateTimeOffset now = _timeProvider.GetUtcNow();
                TelemetryPipelinePublicationEventArgs publication = pipeline.GetCurrentPublication();
                DateTimeOffset? freshnessReference = publication.LastTelemetryReceivedAt
                    ?? state.LastSuccessfulCommunication;
                pipeline.EvaluateCommunication(
                    communicationExpected: true,
                    state.LastSuccessfulCommunication);

                if (freshnessReference is null)
                {
                    continue;
                }

                TimeSpan age = now - freshnessReference.Value;
                if (age >= _options.Alarms.CommunicationTimeout)
                {
                    if (state.ConnectionStatus != MachineConnectionStatus.Reconnecting)
                    {
                        SetReconnecting(
                            MachineGatewayFailureKind.Timeout,
                            $"Aucune télémétrie fraîche depuis {age.TotalSeconds:F1} s.");
                    }

                    CancelCurrentStreamAttempt();
                }
                else if (age >= _options.StaleAfter
                    && state.ConnectionStatus == MachineConnectionStatus.Connected)
                {
                    UpdateState(
                        current => current with { ConnectionStatus = MachineConnectionStatus.Stale },
                        MachineDiagnosticLevel.Warning,
                        $"Télémétrie périmée depuis {age.TotalSeconds:F1} s.");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // L’annulation appartient à la déconnexion explicite ou à l’arrêt de l’application.
        }
    }

    private void OnPipelinePublicationReady(object? sender, TelemetryPipelinePublicationEventArgs publication)
    {
        lock (_stateLock)
        {
            if (!ReferenceEquals(sender, _pipeline))
            {
                return;
            }
        }

        ApplyPipelinePublication(publication);
    }

    private void ApplyPipelinePublication(TelemetryPipelinePublicationEventArgs? publication)
    {
        if (publication is null)
        {
            return;
        }

        UpdateState(state =>
        {
            TelemetrySample? telemetry = publication.LatestTelemetry;
            bool telemetryIsCurrent = telemetry is not null
                && telemetry.Timestamp >= state.LatestSnapshot.Timestamp;
            MachineSnapshot snapshot = !telemetryIsCurrent
                ? state.LatestSnapshot
                : state.LatestSnapshot with
                {
                    State = telemetry!.MachineState,
                    Timestamp = telemetry.Timestamp,
                    ActiveFaults = telemetry.GetActiveFaults().ToImmutableHashSet(),
                };
            return state with
            {
                LatestTelemetry = telemetry,
                LatestSnapshot = snapshot,
                LastSuccessfulCommunication = publication.LastTelemetryReceivedAt
                    ?? state.LastSuccessfulCommunication,
                ReceivedSampleCount = publication.Metrics.ReceivedSamples,
                TelemetryMetrics = publication.Metrics,
                LatestAggregate = publication.LatestAggregate,
                ActiveAlarms = publication.ActiveAlarms,
                AlarmHistory = publication.AlarmHistory,
            };
        });
    }

    private void SetRecoverableCommunicationFailure(MachineGatewayException exception)
    {
        UpdateState(
            state => state with
            {
                ConnectionStatus = MachineConnectionStatus.Reconnecting,
                LastCommunicationError = exception.Message,
                LastFailureKind = exception.FailureKind,
            },
            MachineDiagnosticLevel.Error,
            exception.Message);
        CancelCurrentStreamAttempt();
    }

    private void SetReconnecting(MachineGatewayFailureKind failureKind, string message)
    {
        UpdateState(
            state => state with
            {
                ConnectionStatus = MachineConnectionStatus.Reconnecting,
                LastCommunicationError = message,
                LastFailureKind = failureKind,
            },
            MachineDiagnosticLevel.Warning,
            $"Reconnexion automatique en cours : {message}");
    }

    private TimeSpan CalculateReconnectDelay(int consecutiveFailures)
    {
        double multiplier = Math.Pow(
            _options.ReconnectBackoffMultiplier,
            Math.Max(0, consecutiveFailures - 1));
        double ticks = Math.Min(
            _options.ReconnectMaximumDelay.Ticks,
            _options.ReconnectInitialDelay.Ticks * multiplier);
        return TimeSpan.FromTicks((long)ticks);
    }

    private void CleanupConnection(
        CancellationTokenSource connectionCancellation,
        TelemetryPipeline? pipeline)
    {
        lock (_stateLock)
        {
            _transportSession = null;
            _connectionCancellation = null;
            _streamAttemptCancellation = null;
            _supervisorTask = null;
            _freshnessMonitorTask = null;
            _pipeline = null;
        }

        if (pipeline is not null)
        {
            pipeline.PublicationReady -= OnPipelinePublicationReady;
        }

        connectionCancellation.Dispose();
    }

    private async ValueTask TryAbandonSessionAsync(IMachineSession session)
    {
        try
        {
            await _gateway.AbandonAsync(session).ConfigureAwait(false);
        }
        catch (MachineGatewayException)
        {
            // La session était déjà inutilisable ou libérée par une tentative concurrente annulée.
        }
        catch (ObjectDisposedException)
        {
            // La racine de composition a déjà libéré la passerelle.
        }
    }

    private static async Task AwaitBackgroundTasksAsync(params Task?[] tasks)
    {
        Task[] runningTasks = tasks.Where(static task => task is not null).Cast<Task>().ToArray();
        if (runningTasks.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(runningTasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Les tâches possédées sont annulées avant la fermeture du transport.
        }
    }

    private void SetDisconnected(string diagnosticMessage, MachineDiagnosticLevel diagnosticLevel) =>
        UpdateState(
            CreateDisconnectedState,
            diagnosticLevel,
            diagnosticMessage);

    private MachineSessionState CreateInitialState() => new(
        MachineConnectionStatus.Disconnected,
        MachineSnapshot.Disconnected(_timeProvider.GetUtcNow()),
        null,
        null,
        null,
        null,
        0,
        null,
        null,
        Array.Empty<MachineDiagnosticMessage>(),
        TelemetryPipelineMetrics.Empty(_options.Telemetry.HistoryCapacity),
        null,
        Array.Empty<AlarmEvent>(),
        Array.Empty<AlarmEvent>());

    private MachineSessionState CreateDisconnectedState(MachineSessionState state) => state with
    {
        ConnectionStatus = MachineConnectionStatus.Disconnected,
        LatestSnapshot = MachineSnapshot.Disconnected(_timeProvider.GetUtcNow()),
        LatestTelemetry = null,
        SessionId = null,
        ConnectedAt = null,
        LastSuccessfulCommunication = null,
    };

    private void UpdateState(
        Func<MachineSessionState, MachineSessionState> update,
        MachineDiagnosticLevel? diagnosticLevel = null,
        string? diagnosticMessage = null)
    {
        MachineSessionState publishedState;
        DateTimeOffset now = _timeProvider.GetUtcNow();

        lock (_stateLock)
        {
            if (diagnosticLevel is not null && !string.IsNullOrWhiteSpace(diagnosticMessage))
            {
                _diagnostics.Enqueue(new MachineDiagnosticMessage(now, diagnosticLevel.Value, diagnosticMessage));
                while (_diagnostics.Count > _options.DiagnosticCapacity)
                {
                    _diagnostics.Dequeue();
                }
            }

            _state = update(_state) with
            {
                RecentDiagnostics = Array.AsReadOnly(_diagnostics.Reverse().ToArray()),
            };
            publishedState = _state;
        }

        StateChanged?.Invoke(this, new MachineSessionStateChangedEventArgs(publishedState));
    }

    private IMachineSession? GetTransportSession()
    {
        lock (_stateLock)
        {
            return _transportSession;
        }
    }

    private void SetTransportSession(IMachineSession? session)
    {
        lock (_stateLock)
        {
            _transportSession = session;
        }
    }

    private TelemetryPipeline? GetPipeline()
    {
        lock (_stateLock)
        {
            return _pipeline;
        }
    }

    private void SetStreamAttemptCancellation(CancellationTokenSource cancellation)
    {
        lock (_stateLock)
        {
            _streamAttemptCancellation = cancellation;
        }
    }

    private void ClearStreamAttemptCancellation(CancellationTokenSource cancellation)
    {
        lock (_stateLock)
        {
            if (ReferenceEquals(_streamAttemptCancellation, cancellation))
            {
                _streamAttemptCancellation = null;
            }
        }
    }

    private void CancelCurrentStreamAttempt()
    {
        CancellationTokenSource? cancellation;
        lock (_stateLock)
        {
            cancellation = _streamAttemptCancellation;
        }

        cancellation?.Cancel();
    }

    private void ThrowIfDisposed(bool allowDisposing = false)
    {
        int disposed = Volatile.Read(ref _isDisposed);
        ObjectDisposedException.ThrowIf(disposed != 0 && !allowDisposing, this);
    }
}

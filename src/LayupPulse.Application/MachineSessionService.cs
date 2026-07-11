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
    private readonly IHistoryWriter? _historyWriter;
    private readonly object _stateLock = new();
    private readonly object _historyLock = new();
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly Queue<MachineDiagnosticMessage> _diagnostics = new();
    private MachineSessionState _state;
    private IMachineSession? _transportSession;
    private CancellationTokenSource? _connectionCancellation;
    private CancellationTokenSource? _streamAttemptCancellation;
    private Task? _supervisorTask;
    private Task? _freshnessMonitorTask;
    private TelemetryPipeline? _pipeline;
    private ActiveProductionRunAccumulator? _activeProductionRun;
    private FinalizedProductionRun? _recentFinalizedProductionRun;
    private int _isDisposed;

    public MachineSessionService(
        IMachineGateway gateway,
        TimeProvider timeProvider,
        MachineSessionOptions options,
        IDemoFaultGateway? demoFaultGateway = null,
        IHistoryWriter? historyWriter = null)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _gateway = gateway;
        _demoFaultGateway = demoFaultGateway;
        _timeProvider = timeProvider;
        _options = options;
        _historyWriter = historyWriter;
        _state = CreateInitialState();
        if (_historyWriter is not null)
        {
            _historyWriter.DiagnosticOccurred += OnHistoryDiagnosticOccurred;
            if (_historyWriter.LastDiagnosticMessage is { } diagnostic)
            {
                UpdateState(
                    static state => state,
                    MachineDiagnosticLevel.Error,
                    $"Historique local indisponible : {diagnostic}");
            }
        }
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
            MachineTransportAttachment attachment = await _gateway
                .AttachAsync(connectAttempt.Token)
                .ConfigureAwait(false);
            connectedSession = attachment.Session;
            MachineSnapshot snapshot = await EnsureMachineLifecycleConnectedAsync(
                connectedSession,
                attachment.Snapshot,
                connectAttempt.Token).ConfigureAwait(false);

            pipeline = new TelemetryPipeline(
                _timeProvider,
                _options.Telemetry,
                new AlarmEngine(_timeProvider, _options.Alarms));
            pipeline.PublicationReady += OnPipelinePublicationReady;
            pipeline.AggregateCompleted += OnAggregateCompleted;
            pipeline.AlarmStateChanged += OnAlarmStateChanged;
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

            if (snapshot.State is MachineState.Running or MachineState.Paused
                && snapshot.LoadedRecipe is not null)
            {
                BeginProductionRun(
                    Guid.NewGuid(),
                    snapshot.LoadedRecipe,
                    connectedAt,
                    pipeline,
                    inferredAfterReconnect: true);
            }

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
                pipeline.AggregateCompleted -= OnAggregateCompleted;
                pipeline.AlarmStateChanged -= OnAlarmStateChanged;
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
            Task? supervisorTask;
            Task? freshnessMonitorTask;
            TelemetryPipeline? pipeline;

            lock (_stateLock)
            {
                session = _transportSession;
                connectionCancellation = _connectionCancellation;
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

            FinalizeProductionRun(
                ProductionRunStatus.Aborted,
                _timeProvider.GetUtcNow(),
                MachineState.Disconnected,
                "Déconnexion de la session machine.",
                pipeline);
            ClearRecentProductionRunAssociation(pipeline);
            connectionCancellation.Cancel();
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

                if (result.IsAccepted)
                {
                    ProcessAcceptedCommand(command, result.Transition.Snapshot);
                }

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

                if (result.IsAccepted
                    && active
                    && fault == FaultType.CommunicationTimeout)
                {
                    FinalizeProductionRun(
                        ProductionRunStatus.Faulted,
                        _timeProvider.GetUtcNow(),
                        MachineState.Faulted,
                        "Coupure de communication simulée.",
                        GetPipeline());
                }

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
            if (_historyWriter is not null)
            {
                _historyWriter.DiagnosticOccurred -= OnHistoryDiagnosticOccurred;
            }

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
                        TelemetryPipeline? pipeline = GetPipeline();
                        EnsureProductionRunForTelemetry(sample, pipeline);
                        AddProductionRunSample(sample);
                        pipeline?.Accept(sample);
                        ProcessTerminalTelemetry(sample, pipeline);
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

        MachineTransportAttachment attachment = await _gateway
            .AttachAsync(cancellationToken)
            .ConfigureAwait(false);
        IMachineSession connectedSession = attachment.Session;
        try
        {
            ReconcileProductionRunAfterNewAttachment(attachment.Snapshot);
            MachineSnapshot snapshot = await EnsureMachineLifecycleConnectedAsync(
                connectedSession,
                attachment.Snapshot,
                cancellationToken).ConfigureAwait(false);
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

    private void ReconcileProductionRunAfterNewAttachment(MachineSnapshot attachmentSnapshot)
    {
        if (attachmentSnapshot.State is not (MachineState.Ready or MachineState.Disconnected))
        {
            return;
        }

        TelemetryPipeline? pipeline = GetPipeline();
        FinalizeProductionRun(
            ProductionRunStatus.Aborted,
            _timeProvider.GetUtcNow(),
            attachmentSnapshot.State,
            "Contexte du simulateur remplacé pendant le cycle.",
            pipeline);
        ClearRecentProductionRunAssociation(pipeline);
    }

    private async Task<MachineSnapshot> EnsureMachineLifecycleConnectedAsync(
        IMachineSession session,
        MachineSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (snapshot.State != MachineState.Disconnected)
        {
            return snapshot;
        }

        MachineCommand command = new(
            Guid.NewGuid(),
            MachineCommandType.ConnectRequested,
            _timeProvider.GetUtcNow());
        CommandResult result = await _gateway
            .ExecuteCommandAsync(session, command, cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsAccepted)
        {
            throw new MachineGatewayException(
                MachineGatewayFailureKind.CommandRejected,
                result.Transition.Rejection?.Message
                    ?? "Le simulateur a rejeté l’activation du cycle de vie machine.");
        }

        return result.Transition.Snapshot;
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

    private void OnAggregateCompleted(object? sender, TelemetryAggregateCompletedEventArgs eventArgs)
    {
        lock (_stateLock)
        {
            if (!ReferenceEquals(sender, _pipeline))
            {
                return;
            }
        }

        _historyWriter?.TryRecordTelemetryAggregate(eventArgs.Aggregate);
    }

    private void OnAlarmStateChanged(object? sender, TelemetryPipelinePublicationEventArgs publication)
    {
        lock (_stateLock)
        {
            if (!ReferenceEquals(sender, _pipeline))
            {
                return;
            }
        }

        foreach (AlarmEvent alarm in publication.ActiveAlarms.Concat(publication.AlarmHistory))
        {
            _historyWriter?.TryRecordAlarm(alarm);
            ProductionRun? updatedRun = null;
            lock (_historyLock)
            {
                _activeProductionRun?.RegisterAlarm(alarm);
                if (_recentFinalizedProductionRun?.Accumulator.RegisterAlarm(alarm) == true)
                {
                    updatedRun = _recentFinalizedProductionRun.CreateSnapshot();
                }
            }

            if (updatedRun is not null)
            {
                _historyWriter?.TryRecordProductionRun(updatedRun);
            }
        }
    }

    private void OnHistoryDiagnosticOccurred(
        object? sender,
        HistoryPersistenceDiagnosticEventArgs eventArgs) =>
        UpdateState(
            static state => state,
            MachineDiagnosticLevel.Error,
            $"Historique local : {eventArgs.Message}");

    private void ProcessAcceptedCommand(MachineCommand command, MachineSnapshot snapshot)
    {
        if (command.Type == MachineCommandType.Start && snapshot.LoadedRecipe is not null)
        {
            ClearRecentProductionRunAssociation(GetPipeline());
            BeginProductionRun(
                command.ProductionRunId ?? command.CorrelationId,
                snapshot.LoadedRecipe,
                command.Timestamp,
                GetPipeline(),
                inferredAfterReconnect: false);
            return;
        }

        if (command.Type == MachineCommandType.Stop)
        {
            FinalizeProductionRun(
                ProductionRunStatus.Aborted,
                command.Timestamp,
                MachineState.Ready,
                "Arrêt demandé par l’opérateur.",
                GetPipeline());
            return;
        }

        if (command.Type == MachineCommandType.Reset)
        {
            ClearRecentProductionRunAssociation(GetPipeline());
        }
    }

    private void EnsureProductionRunForTelemetry(
        TelemetrySample sample,
        TelemetryPipeline? pipeline)
    {
        if (sample.MachineState is not (MachineState.Running or MachineState.Paused))
        {
            return;
        }

        lock (_historyLock)
        {
            if (_activeProductionRun is not null)
            {
                return;
            }
        }

        ProductionRecipe? recipe = State.LatestSnapshot.LoadedRecipe;
        if (recipe is not null)
        {
            BeginProductionRun(
                Guid.NewGuid(),
                recipe,
                sample.Timestamp,
                pipeline,
                inferredAfterReconnect: true);
        }
    }

    private void BeginProductionRun(
        Guid productionRunId,
        ProductionRecipe recipe,
        DateTimeOffset startedAt,
        TelemetryPipeline? pipeline,
        bool inferredAfterReconnect)
    {
        ProductionRun runningRun = new(
            productionRunId,
            recipe,
            ProductionRunStatus.Running,
            startedAt);
        lock (_historyLock)
        {
            if (_activeProductionRun is not null)
            {
                return;
            }

            _activeProductionRun = new ActiveProductionRunAccumulator(runningRun);
            _recentFinalizedProductionRun = null;
        }

        _historyWriter?.TryRecordProductionRun(runningRun);
        pipeline?.BeginProductionRun(productionRunId);
        if (inferredAfterReconnect)
        {
            UpdateState(
                static state => state,
                MachineDiagnosticLevel.Warning,
                "Un cycle déjà actif a été rattaché à un nouvel identifiant d’historique local.");
        }
    }

    private void AddProductionRunSample(TelemetrySample sample)
    {
        lock (_historyLock)
        {
            _activeProductionRun?.Add(sample);
        }
    }

    private void ProcessTerminalTelemetry(TelemetrySample sample, TelemetryPipeline? pipeline)
    {
        switch (sample.MachineState)
        {
            case MachineState.Completed:
                FinalizeProductionRun(
                    ProductionRunStatus.Completed,
                    sample.Timestamp,
                    MachineState.Completed,
                    null,
                    pipeline);
                break;
            case MachineState.Faulted:
                FinalizeProductionRun(
                    ProductionRunStatus.Faulted,
                    sample.Timestamp,
                    MachineState.Faulted,
                    "Défaut critique simulé.",
                    pipeline);
                break;
            case MachineState.Disconnected:
                FinalizeProductionRun(
                    ProductionRunStatus.Aborted,
                    sample.Timestamp,
                    MachineState.Disconnected,
                    "Déconnexion de la session machine.",
                    pipeline);
                break;
        }
    }

    private void FinalizeProductionRun(
        ProductionRunStatus status,
        DateTimeOffset endedAt,
        MachineState terminalState,
        string? reason,
        TelemetryPipeline? pipeline)
    {
        ActiveProductionRunAccumulator? accumulator;
        lock (_historyLock)
        {
            accumulator = _activeProductionRun;
            _activeProductionRun = null;
        }

        if (accumulator is null)
        {
            return;
        }

        bool retainAlarmAssociation = status == ProductionRunStatus.Faulted;
        pipeline?.EndProductionRun(retainAlarmAssociation);
        FinalizedProductionRun finalized = new(
            accumulator,
            status,
            endedAt,
            terminalState,
            reason);
        if (retainAlarmAssociation)
        {
            lock (_historyLock)
            {
                _recentFinalizedProductionRun = finalized;
            }
        }

        _historyWriter?.TryRecordProductionRun(finalized.CreateSnapshot());
    }

    private void ClearRecentProductionRunAssociation(TelemetryPipeline? pipeline)
    {
        lock (_historyLock)
        {
            _recentFinalizedProductionRun = null;
        }

        pipeline?.ClearProductionRunAssociation();
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
            pipeline.AggregateCompleted -= OnAggregateCompleted;
            pipeline.AlarmStateChanged -= OnAlarmStateChanged;
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

        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // La tentative s’est terminée et a libéré son jeton entre la lecture et l’annulation.
        }
    }

    private void ThrowIfDisposed(bool allowDisposing = false)
    {
        int disposed = Volatile.Read(ref _isDisposed);
        ObjectDisposedException.ThrowIf(disposed != 0 && !allowDisposing, this);
    }

    private sealed class ActiveProductionRunAccumulator(ProductionRun runningRun)
    {
        private readonly HashSet<Guid> _alarmIds = [];
        private double _temperatureSum;
        private double _pressureSum;
        private double _forceSum;
        private double _feedRateSum;
        private double _minimumProcessHealth = 100;
        private double _completionPercentage;
        private int _sampleCount;

        public void Add(TelemetrySample sample)
        {
            _sampleCount++;
            _temperatureSum += sample.HeaterTemperatureCelsius;
            _pressureSum += sample.MaterialPressureBar;
            _forceSum += sample.CompactionForceNewtons;
            _feedRateSum += sample.ActualFeedRateMillimetersPerSecond;
            _minimumProcessHealth = Math.Min(
                _minimumProcessHealth,
                sample.ProcessHealthPercentage);
            _completionPercentage = sample.CycleProgressPercentage;
        }

        public bool RegisterAlarm(AlarmEvent alarm)
        {
            if (alarm.ProductionRunId != runningRun.Id)
            {
                return false;
            }

            return _alarmIds.Add(alarm.Id);
        }

        public ProductionRun Complete(
            ProductionRunStatus status,
            DateTimeOffset endedAt,
            MachineState terminalState,
            string? reason) => new(
                runningRun.Id,
                runningRun.Recipe,
                status,
                runningRun.StartedAt,
                endedAt,
                terminalState,
                status == ProductionRunStatus.Completed ? 100 : _completionPercentage,
                reason,
                Average(_temperatureSum),
                Average(_pressureSum),
                Average(_forceSum),
                Average(_feedRateSum),
                _minimumProcessHealth,
                _alarmIds.Count);

        private double Average(double sum) => _sampleCount == 0 ? 0 : sum / _sampleCount;
    }

    private sealed record FinalizedProductionRun(
        ActiveProductionRunAccumulator Accumulator,
        ProductionRunStatus Status,
        DateTimeOffset EndedAt,
        MachineState TerminalState,
        string? Reason)
    {
        public ProductionRun CreateSnapshot() =>
            Accumulator.Complete(Status, EndedAt, TerminalState, Reason);
    }
}

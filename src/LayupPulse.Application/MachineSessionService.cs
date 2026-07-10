using LayupPulse.Domain;

namespace LayupPulse.Application;

/// <summary>
/// Maintient une session machine unique, sa télémétrie la plus récente et son état de fraîcheur.
/// </summary>
public sealed class MachineSessionService : IMachineSessionService
{
    private readonly IMachineGateway _gateway;
    private readonly TimeProvider _timeProvider;
    private readonly MachineSessionOptions _options;
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly Queue<MachineDiagnosticMessage> _diagnostics = new();
    private MachineSessionState _state;
    private IMachineSession? _transportSession;
    private CancellationTokenSource? _sessionCancellation;
    private Task? _telemetryTask;
    private Task? _staleMonitorTask;
    private DateTimeOffset _lastNotificationAt = DateTimeOffset.MinValue;
    private int _isDisposed;

    public MachineSessionService(
        IMachineGateway gateway,
        TimeProvider timeProvider,
        MachineSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _gateway = gateway;
        _timeProvider = timeProvider;
        _options = options;
        _state = new MachineSessionState(
            MachineConnectionStatus.Disconnected,
            MachineSnapshot.Disconnected(_timeProvider.GetUtcNow()),
            null,
            null,
            null,
            null,
            0,
            null,
            null,
            Array.Empty<MachineDiagnosticMessage>());
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

    public async Task<MachineSessionOperationResult> ConnectAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        IMachineSession? connectedSession = null;
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
                "Connexion au simulateur en cours…",
                forceNotification: true);

            connectedSession = await _gateway.ConnectAsync(cancellationToken).ConfigureAwait(false);
            MachineSnapshot snapshot = await _gateway
                .GetSnapshotAsync(connectedSession, cancellationToken)
                .ConfigureAwait(false);

            CancellationTokenSource sessionCancellation = new();
            DateTimeOffset receivedAt = _timeProvider.GetUtcNow();

            lock (_stateLock)
            {
                _transportSession = connectedSession;
                _sessionCancellation = sessionCancellation;
            }

            UpdateState(
                state => state with
                {
                    ConnectionStatus = MachineConnectionStatus.Connected,
                    LatestSnapshot = snapshot,
                    LatestTelemetry = null,
                    SessionId = connectedSession.SessionId,
                    ConnectedAt = connectedSession.ConnectedAt,
                    LastSuccessfulCommunication = receivedAt,
                    ReceivedSampleCount = 0,
                    LastCommunicationError = null,
                    LastFailureKind = null,
                },
                MachineDiagnosticLevel.Information,
                "Connexion au simulateur établie.",
                forceNotification: true);

            Task telemetryTask = ReadTelemetryAsync(connectedSession, sessionCancellation.Token);
            Task staleMonitorTask = MonitorStalenessAsync(sessionCancellation.Token);
            lock (_stateLock)
            {
                _telemetryTask = telemetryTask;
                _staleMonitorTask = staleMonitorTask;
            }

            connectedSession = null;
            return MachineSessionOperationResult.Successful("Connexion établie.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (connectedSession is not null)
            {
                await TryAbandonSessionAsync(connectedSession).ConfigureAwait(false);
            }

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
                state => state with
                {
                    ConnectionStatus = MachineConnectionStatus.Disconnected,
                    LatestSnapshot = MachineSnapshot.Disconnected(_timeProvider.GetUtcNow()),
                    LastCommunicationError = exception.Message,
                    LastFailureKind = exception.FailureKind,
                },
                MachineDiagnosticLevel.Error,
                exception.Message,
                forceNotification: true);
            return MachineSessionOperationResult.Failed(exception.Message, exception.FailureKind);
        }
        finally
        {
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
            CancellationTokenSource? sessionCancellation;
            Task? telemetryTask;
            Task? staleMonitorTask;

            lock (_stateLock)
            {
                session = _transportSession;
                sessionCancellation = _sessionCancellation;
                telemetryTask = _telemetryTask;
                staleMonitorTask = _staleMonitorTask;
            }

            if (session is null)
            {
                SetDisconnected("Session déjà déconnectée.", MachineDiagnosticLevel.Information);
                return MachineSessionOperationResult.Successful("Session déjà déconnectée.");
            }

            UpdateState(
                state => state with { ConnectionStatus = MachineConnectionStatus.Disconnecting },
                MachineDiagnosticLevel.Information,
                "Déconnexion du simulateur en cours…",
                forceNotification: true);

            sessionCancellation?.Cancel();
            await AwaitBackgroundTasksAsync(telemetryTask, staleMonitorTask).ConfigureAwait(false);

            MachineGatewayException? failure = null;
            try
            {
                await _gateway.DisconnectAsync(session, cancellationToken).ConfigureAwait(false);
            }
            catch (MachineGatewayException exception)
            {
                failure = exception;
            }
            finally
            {
                lock (_stateLock)
                {
                    if (ReferenceEquals(_transportSession, session))
                    {
                        _transportSession = null;
                        _sessionCancellation = null;
                        _telemetryTask = null;
                        _staleMonitorTask = null;
                    }
                }

                sessionCancellation?.Dispose();
            }

            if (failure is not null)
            {
                UpdateState(
                    state => CreateDisconnectedState(state) with
                    {
                        LastCommunicationError = failure.Message,
                        LastFailureKind = failure.FailureKind,
                    },
                    MachineDiagnosticLevel.Warning,
                    $"Session fermée localement après une erreur : {failure.Message}",
                    forceNotification: true);
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
            IMachineSession? session;
            MachineConnectionStatus connectionStatus;
            lock (_stateLock)
            {
                session = _transportSession;
                connectionStatus = _state.ConnectionStatus;
            }

            if (session is null || connectionStatus != MachineConnectionStatus.Connected)
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
                    message,
                    forceNotification: true);

                return MachineCommandExecutionResult.FromCommandResult(result) with { Message = message };
            }
            catch (MachineGatewayException exception)
            {
                UpdateState(
                    state => state with
                    {
                        ConnectionStatus = MachineConnectionStatus.Stale,
                        LastCommunicationError = exception.Message,
                        LastFailureKind = exception.FailureKind,
                    },
                    MachineDiagnosticLevel.Error,
                    exception.Message,
                    forceNotification: true);
                return MachineCommandExecutionResult.Failed(exception.Message, exception.FailureKind);
            }
        }
        finally
        {
            _operationLock.Release();
        }
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

    private async Task ReadTelemetryAsync(IMachineSession session, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (TelemetrySample sample in _gateway
                .StreamTelemetryAsync(session, cancellationToken)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                DateTimeOffset receivedAt = _timeProvider.GetUtcNow();
                bool recovered = State.ConnectionStatus == MachineConnectionStatus.Stale;
                MachineState previousMachineState = State.LatestSnapshot.State;

                UpdateState(
                    state => state with
                    {
                        ConnectionStatus = MachineConnectionStatus.Connected,
                        LatestTelemetry = sample,
                        LatestSnapshot = state.LatestSnapshot with
                        {
                            State = sample.MachineState,
                            Timestamp = sample.Timestamp,
                        },
                        LastSuccessfulCommunication = receivedAt,
                        ReceivedSampleCount = state.ReceivedSampleCount + 1,
                        LastCommunicationError = null,
                        LastFailureKind = null,
                    },
                    recovered || previousMachineState != sample.MachineState
                        ? MachineDiagnosticLevel.Information
                        : null,
                    recovered
                        ? "Réception de la télémétrie rétablie."
                        : previousMachineState != sample.MachineState
                            ? $"État machine reçu : {sample.MachineState}."
                            : null,
                    forceNotification: recovered || previousMachineState != sample.MachineState);
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                await HandleUnexpectedStreamEndAsync(
                    session,
                    MachineGatewayFailureKind.Interrupted,
                    "Le flux de télémétrie s’est terminé sans demande de déconnexion.")
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // L’annulation appartient au cycle de vie normal de la session.
        }
        catch (MachineGatewayException exception)
        {
            await HandleUnexpectedStreamEndAsync(session, exception.FailureKind, exception.Message)
                .ConfigureAwait(false);
        }
    }

    private async Task MonitorStalenessAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(_options.StaleCheckInterval, _timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                MachineSessionState state = State;
                if (state.ConnectionStatus != MachineConnectionStatus.Connected
                    || state.LastSuccessfulCommunication is null)
                {
                    continue;
                }

                TimeSpan age = _timeProvider.GetUtcNow() - state.LastSuccessfulCommunication.Value;
                if (age < _options.StaleAfter)
                {
                    continue;
                }

                UpdateState(
                    current => current with { ConnectionStatus = MachineConnectionStatus.Stale },
                    MachineDiagnosticLevel.Warning,
                    $"Télémétrie périmée depuis {age.TotalSeconds:F1} s.",
                    forceNotification: true);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // L’annulation appartient au cycle de vie normal de la session.
        }
    }

    private async Task HandleUnexpectedStreamEndAsync(
        IMachineSession session,
        MachineGatewayFailureKind failureKind,
        string message)
    {
        CancellationTokenSource? sessionCancellation;
        lock (_stateLock)
        {
            if (!ReferenceEquals(_transportSession, session))
            {
                return;
            }

            sessionCancellation = _sessionCancellation;
        }

        sessionCancellation?.Cancel();
        UpdateState(
            state => state with
            {
                ConnectionStatus = MachineConnectionStatus.Disconnected,
                LastCommunicationError = message,
                LastFailureKind = failureKind,
            },
            MachineDiagnosticLevel.Error,
            message,
            forceNotification: true);

        await TryAbandonSessionAsync(session).ConfigureAwait(false);

        lock (_stateLock)
        {
            if (ReferenceEquals(_transportSession, session))
            {
                _transportSession = null;
                _sessionCancellation = null;
                _telemetryTask = null;
                _staleMonitorTask = null;
            }
        }

        sessionCancellation?.Dispose();
    }

    private async Task TryAbandonSessionAsync(IMachineSession session)
    {
        try
        {
            await _gateway.DisconnectAsync(session, CancellationToken.None).ConfigureAwait(false);
        }
        catch (MachineGatewayException)
        {
            // La passerelle ferme le canal local dans tous les cas.
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
            // Les tâches de session sont annulées avant la déconnexion du transport.
        }
    }

    private void SetDisconnected(string diagnosticMessage, MachineDiagnosticLevel diagnosticLevel) =>
        UpdateState(
            CreateDisconnectedState,
            diagnosticLevel,
            diagnosticMessage,
            forceNotification: true);

    private MachineSessionState CreateDisconnectedState(MachineSessionState state) => state with
    {
        ConnectionStatus = MachineConnectionStatus.Disconnected,
        LatestSnapshot = MachineSnapshot.Disconnected(_timeProvider.GetUtcNow()),
        LatestTelemetry = null,
        SessionId = null,
        ConnectedAt = null,
        LastSuccessfulCommunication = null,
        ReceivedSampleCount = 0,
    };

    private void UpdateState(
        Func<MachineSessionState, MachineSessionState> update,
        MachineDiagnosticLevel? diagnosticLevel = null,
        string? diagnosticMessage = null,
        bool forceNotification = false)
    {
        MachineSessionState? publishedState = null;
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

            if (forceNotification || now - _lastNotificationAt >= _options.NotificationInterval)
            {
                _lastNotificationAt = now;
                publishedState = _state;
            }
        }

        if (publishedState is not null)
        {
            StateChanged?.Invoke(this, new MachineSessionStateChangedEventArgs(publishedState));
        }
    }

    private void ThrowIfDisposed(bool allowDisposing = false)
    {
        int disposed = Volatile.Read(ref _isDisposed);
        ObjectDisposedException.ThrowIf(disposed != 0 && !allowDisposing, this);
    }
}

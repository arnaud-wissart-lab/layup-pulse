using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LayupPulse.Application;
using LayupPulse.Domain;

namespace LayupPulse.Desktop;

public sealed class OverviewViewModel : ObservableObject
{
    private readonly IMachineSessionService _sessionService;
    private readonly TimeProvider _timeProvider;
    private MachineSessionState _sessionState;
    private bool _isOperationInProgress;
    private string _connectionStatus = string.Empty;
    private string _connectionTone = "Neutral";
    private string _machineState = string.Empty;
    private string _machineTone = "Neutral";
    private string _loadedRecipe = string.Empty;
    private string _cycleProgress = "0,0 %";
    private double _cycleProgressValue;
    private string _headX = "—";
    private string _headY = "—";
    private string _headZ = "—";
    private string _targetFeedRate = "—";
    private string _actualFeedRate = "—";
    private string _compactionForce = "—";
    private string _heaterTemperature = "—";
    private string _materialPressure = "—";
    private string _processHealth = "—";
    private string _lastTelemetryTimestamp = "Aucun échantillon reçu";
    private string _sequenceNumber = "—";
    private string _telemetryQuality = "Valeurs indisponibles tant que la session n’est pas connectée.";
    private string _availabilityMessage = string.Empty;
    private string _lastCommandFeedback = "Prêt. Démarrez le simulateur, puis connectez la session.";
    private string _feedbackTone = "Neutral";
    private string _feedbackGlyph = "i";

    public OverviewViewModel(IMachineSessionService sessionService, TimeProvider timeProvider)
    {
        _sessionService = sessionService;
        _timeProvider = timeProvider;
        _sessionState = sessionService.State;

        ConnectCommand = new AsyncRelayCommand(ConnectAsync, CanConnect);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, CanDisconnect);
        LoadDemoRecipeCommand = new AsyncRelayCommand(
            cancellationToken => ExecuteMachineCommandAsync(MachineCommandType.LoadRecipe, cancellationToken),
            () => CanExecuteMachineCommand(MachineCommandType.LoadRecipe));
        StartCommand = new AsyncRelayCommand(
            cancellationToken => ExecuteMachineCommandAsync(MachineCommandType.Start, cancellationToken),
            () => CanExecuteMachineCommand(MachineCommandType.Start));
        PauseCommand = new AsyncRelayCommand(
            cancellationToken => ExecuteMachineCommandAsync(MachineCommandType.Pause, cancellationToken),
            () => CanExecuteMachineCommand(MachineCommandType.Pause));
        ResumeCommand = new AsyncRelayCommand(
            cancellationToken => ExecuteMachineCommandAsync(MachineCommandType.Resume, cancellationToken),
            () => CanExecuteMachineCommand(MachineCommandType.Resume));
        StopCommand = new AsyncRelayCommand(
            cancellationToken => ExecuteMachineCommandAsync(MachineCommandType.Stop, cancellationToken),
            () => CanExecuteMachineCommand(MachineCommandType.Stop));
        ResetCommand = new AsyncRelayCommand(
            cancellationToken => ExecuteMachineCommandAsync(MachineCommandType.Reset, cancellationToken),
            () => CanExecuteMachineCommand(MachineCommandType.Reset));

        ApplyState(_sessionState);
    }

    public IAsyncRelayCommand ConnectCommand { get; }

    public IAsyncRelayCommand DisconnectCommand { get; }

    public IAsyncRelayCommand LoadDemoRecipeCommand { get; }

    public IAsyncRelayCommand StartCommand { get; }

    public IAsyncRelayCommand PauseCommand { get; }

    public IAsyncRelayCommand ResumeCommand { get; }

    public IAsyncRelayCommand StopCommand { get; }

    public IAsyncRelayCommand ResetCommand { get; }

    public bool IsOperationInProgress
    {
        get => _isOperationInProgress;
        private set => SetProperty(ref _isOperationInProgress, value);
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        private set => SetProperty(ref _connectionStatus, value);
    }

    public string ConnectionTone
    {
        get => _connectionTone;
        private set => SetProperty(ref _connectionTone, value);
    }

    public string MachineState
    {
        get => _machineState;
        private set => SetProperty(ref _machineState, value);
    }

    public string MachineTone
    {
        get => _machineTone;
        private set => SetProperty(ref _machineTone, value);
    }

    public string LoadedRecipe
    {
        get => _loadedRecipe;
        private set => SetProperty(ref _loadedRecipe, value);
    }

    public string CycleProgress
    {
        get => _cycleProgress;
        private set => SetProperty(ref _cycleProgress, value);
    }

    public double CycleProgressValue
    {
        get => _cycleProgressValue;
        private set => SetProperty(ref _cycleProgressValue, value);
    }

    public string HeadX
    {
        get => _headX;
        private set => SetProperty(ref _headX, value);
    }

    public string HeadY
    {
        get => _headY;
        private set => SetProperty(ref _headY, value);
    }

    public string HeadZ
    {
        get => _headZ;
        private set => SetProperty(ref _headZ, value);
    }

    public string TargetFeedRate
    {
        get => _targetFeedRate;
        private set => SetProperty(ref _targetFeedRate, value);
    }

    public string ActualFeedRate
    {
        get => _actualFeedRate;
        private set => SetProperty(ref _actualFeedRate, value);
    }

    public string CompactionForce
    {
        get => _compactionForce;
        private set => SetProperty(ref _compactionForce, value);
    }

    public string HeaterTemperature
    {
        get => _heaterTemperature;
        private set => SetProperty(ref _heaterTemperature, value);
    }

    public string MaterialPressure
    {
        get => _materialPressure;
        private set => SetProperty(ref _materialPressure, value);
    }

    public string ProcessHealth
    {
        get => _processHealth;
        private set => SetProperty(ref _processHealth, value);
    }

    public string LastTelemetryTimestamp
    {
        get => _lastTelemetryTimestamp;
        private set => SetProperty(ref _lastTelemetryTimestamp, value);
    }

    public string SequenceNumber
    {
        get => _sequenceNumber;
        private set => SetProperty(ref _sequenceNumber, value);
    }

    public string TelemetryQuality
    {
        get => _telemetryQuality;
        private set => SetProperty(ref _telemetryQuality, value);
    }

    public string AvailabilityMessage
    {
        get => _availabilityMessage;
        private set => SetProperty(ref _availabilityMessage, value);
    }

    public string LastCommandFeedback
    {
        get => _lastCommandFeedback;
        private set => SetProperty(ref _lastCommandFeedback, value);
    }

    public string FeedbackTone
    {
        get => _feedbackTone;
        private set => SetProperty(ref _feedbackTone, value);
    }

    public string FeedbackGlyph
    {
        get => _feedbackGlyph;
        private set => SetProperty(ref _feedbackGlyph, value);
    }

    public void ApplyState(MachineSessionState state)
    {
        _sessionState = state;
        ConnectionStatus = MachineDisplayText.ConnectionStatus(state.ConnectionStatus);
        ConnectionTone = MachineDisplayText.ConnectionTone(state.ConnectionStatus);
        MachineState = MachineDisplayText.MachineState(state.LatestSnapshot.State);
        MachineTone = MachineDisplayText.MachineTone(state.LatestSnapshot.State);
        LoadedRecipe = state.LatestSnapshot.LoadedRecipe?.Name ?? "Aucune recette chargée";

        TelemetrySample? telemetry = state.LatestTelemetry;
        CycleProgressValue = telemetry?.CycleProgressPercentage ?? 0;
        CycleProgress = Format(telemetry?.CycleProgressPercentage, "F1", "%");
        HeadX = Format(telemetry?.HeadXMillimeters, "F1", "mm");
        HeadY = Format(telemetry?.HeadYMillimeters, "F1", "mm");
        HeadZ = Format(telemetry?.HeadZMillimeters, "F1", "mm");
        TargetFeedRate = Format(telemetry?.TargetFeedRateMillimetersPerSecond, "F1", "mm/s");
        ActualFeedRate = Format(telemetry?.ActualFeedRateMillimetersPerSecond, "F1", "mm/s");
        CompactionForce = Format(telemetry?.CompactionForceNewtons, "F0", "N");
        HeaterTemperature = Format(telemetry?.HeaterTemperatureCelsius, "F1", "°C");
        MaterialPressure = Format(telemetry?.MaterialPressureBar, "F2", "bar");
        ProcessHealth = Format(telemetry?.ProcessHealthPercentage, "F1", "%");
        LastTelemetryTimestamp = telemetry is null
            ? "Aucun échantillon reçu"
            : telemetry.Timestamp.ToLocalTime().ToString(
                "dd/MM/yyyy HH:mm:ss.fff",
                CultureInfo.CurrentCulture);
        SequenceNumber = telemetry?.SequenceNumber.ToString(CultureInfo.CurrentCulture) ?? "—";
        TelemetryQuality = state.ConnectionStatus switch
        {
            MachineConnectionStatus.Connected when telemetry is not null => "Télémétrie fraîche",
            MachineConnectionStatus.Stale => "Attention : les dernières valeurs sont conservées mais périmées.",
            MachineConnectionStatus.Connecting => "En attente du premier état machine…",
            _ => "Valeurs indisponibles tant que la session n’est pas connectée.",
        };

        AvailabilityMessage = BuildAvailabilityMessage();
        NotifyCommandsCanExecuteChanged();
    }

    private async Task ConnectAsync(CancellationToken cancellationToken) =>
        await RunOperationAsync(
            async token =>
            {
                MachineSessionOperationResult result = await _sessionService.ConnectAsync(token);
                SetFeedback(result.Message, !result.IsSuccessful);
            },
            cancellationToken);

    private async Task DisconnectAsync(CancellationToken cancellationToken) =>
        await RunOperationAsync(
            async token =>
            {
                MachineSessionOperationResult result = await _sessionService.DisconnectAsync(token);
                SetFeedback(result.Message, !result.IsSuccessful);
            },
            cancellationToken);

    private async Task ExecuteMachineCommandAsync(
        MachineCommandType commandType,
        CancellationToken cancellationToken) =>
        await RunOperationAsync(
            async token =>
            {
                ProductionRecipe? recipe = commandType == MachineCommandType.LoadRecipe
                    ? BuiltInRecipes.WingPanelDemo
                    : null;
                MachineCommand command = new(
                    Guid.NewGuid(),
                    commandType,
                    _timeProvider.GetUtcNow(),
                    recipe);
                MachineCommandExecutionResult result = await _sessionService
                    .ExecuteCommandAsync(command, token);
                SetFeedback(result.Message, !result.IsAccepted);
            },
            cancellationToken);

    private async Task RunOperationAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        IsOperationInProgress = true;
        AvailabilityMessage = "Une action est en cours ; les autres commandes sont temporairement suspendues.";
        NotifyCommandsCanExecuteChanged();

        try
        {
            await operation(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetFeedback("Action annulée.", isError: false);
        }
        catch (Exception exception)
        {
            SetFeedback($"Erreur applicative inattendue : {exception.Message}", isError: true);
        }
        finally
        {
            IsOperationInProgress = false;
            AvailabilityMessage = BuildAvailabilityMessage();
            NotifyCommandsCanExecuteChanged();
        }
    }

    private bool CanConnect() =>
        !IsOperationInProgress
        && _sessionState.ConnectionStatus == MachineConnectionStatus.Disconnected;

    private bool CanDisconnect() =>
        !IsOperationInProgress
        && _sessionState.ConnectionStatus is MachineConnectionStatus.Connected or MachineConnectionStatus.Stale;

    private bool CanExecuteMachineCommand(MachineCommandType commandType) =>
        !IsOperationInProgress
        && _sessionState.ConnectionStatus == MachineConnectionStatus.Connected
        && MachineCommandAvailability.CanExecute(_sessionState.LatestSnapshot, commandType);

    private string BuildAvailabilityMessage()
    {
        if (IsOperationInProgress)
        {
            return "Une action est en cours ; les autres commandes sont temporairement suspendues.";
        }

        if (_sessionState.ConnectionStatus == MachineConnectionStatus.Disconnected)
        {
            return "Action disponible : connecter le simulateur.";
        }

        if (_sessionState.ConnectionStatus == MachineConnectionStatus.Stale)
        {
            return "La télémétrie est périmée : seule la déconnexion est proposée.";
        }

        List<string> commands = new();
        if (CanExecuteMachineCommand(MachineCommandType.LoadRecipe))
        {
            commands.Add("charger la recette de démonstration");
        }

        if (CanExecuteMachineCommand(MachineCommandType.Start))
        {
            commands.Add("démarrer");
        }

        if (CanExecuteMachineCommand(MachineCommandType.Pause))
        {
            commands.Add("mettre en pause");
        }

        if (CanExecuteMachineCommand(MachineCommandType.Resume))
        {
            commands.Add("reprendre");
        }

        if (CanExecuteMachineCommand(MachineCommandType.Stop))
        {
            commands.Add("arrêter");
        }

        if (CanExecuteMachineCommand(MachineCommandType.Reset))
        {
            commands.Add("réinitialiser");
        }

        return commands.Count == 0
            ? "Aucune commande de cycle n’est valide dans l’état courant. La déconnexion reste disponible."
            : $"Actions valides : {string.Join(", ", commands)}.";
    }

    private void SetFeedback(string message, bool isError)
    {
        LastCommandFeedback = message;
        FeedbackTone = isError ? "Danger" : "Healthy";
        FeedbackGlyph = isError ? "!" : "✓";
    }

    private void NotifyCommandsCanExecuteChanged()
    {
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
        LoadDemoRecipeCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
    }

    private static string Format(double? value, string format, string unit) =>
        value is null
            ? "—"
            : $"{value.Value.ToString(format, CultureInfo.CurrentCulture)} {unit}";
}

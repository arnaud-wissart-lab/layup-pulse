using LayupPulse.Domain;

namespace LayupPulse.Simulator;

/// <summary>
/// Produit les signaux synthétiques d’une cellule fictive à partir de ticks discrets.
/// </summary>
public sealed class DeterministicMachineSimulator
{
    private const double AmbientTemperatureCelsius = 22;
    private const double NominalCompactionForceNewtons = 450;
    private const double FeedAccelerationMillimetersPerSecondSquared = 80;
    private static readonly Guid CycleCompletedCorrelationId =
        new("2f966722-64a8-4e04-9d58-aa65a313ed87");

    private readonly object _gate = new();
    private readonly Random _random;
    private readonly TimeSpan _tickDuration;
    private MachineSnapshot _machine;
    private DateTimeOffset _lastTimestamp;
    private long _sequenceNumber;
    private double _headXMillimeters = 100;
    private double _headYMillimeters = 75;
    private double _headZMillimeters = 25;
    private double _actualFeedRateMillimetersPerSecond;
    private double _compactionForceNewtons;
    private double _heaterTemperatureCelsius = AmbientTemperatureCelsius;
    private double _materialPressureBar;
    private double _cycleProgressPercentage;
    private double _processHealthPercentage = 100;

    public DeterministicMachineSimulator(
        int randomSeed,
        int telemetryRateHz,
        DateTimeOffset initialTimestamp)
    {
        if (telemetryRateHz is < SimulatorOptions.MinimumTelemetryRateHz or > SimulatorOptions.MaximumTelemetryRateHz)
        {
            throw new ArgumentOutOfRangeException(
                nameof(telemetryRateHz),
                $"La fréquence doit être comprise entre {SimulatorOptions.MinimumTelemetryRateHz} et " +
                $"{SimulatorOptions.MaximumTelemetryRateHz} Hz.");
        }

        _random = new Random(randomSeed);
        _tickDuration = TimeSpan.FromSeconds(1d / telemetryRateHz);
        _lastTimestamp = initialTimestamp.ToUniversalTime();
        _machine = MachineSnapshot.Disconnected(_lastTimestamp);
    }

    public MachineState State
    {
        get
        {
            lock (_gate)
            {
                return _machine.State;
            }
        }
    }

    public bool IsCommunicationDropped
    {
        get
        {
            lock (_gate)
            {
                return _machine.ActiveFaults.Contains(FaultType.CommunicationTimeout);
            }
        }
    }

    public CommandResult ExecuteCommand(MachineCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        lock (_gate)
        {
            StateTransitionResult transition = MachineStateMachine.Transition(_machine, command);
            if (!transition.IsAccepted)
            {
                return new CommandResult(command.CorrelationId, transition);
            }

            _machine = transition.Snapshot;
            _lastTimestamp = command.Timestamp;

            if (command.Type == MachineCommandType.ConnectRequested)
            {
                MachineCommand established = command with
                {
                    Type = MachineCommandType.ConnectionEstablished,
                };
                transition = MachineStateMachine.Transition(_machine, established);
                _machine = transition.Snapshot;
            }

            ApplyAcceptedCommandEffects(command.Type);
            return new CommandResult(command.CorrelationId, transition);
        }
    }

    public SimulationSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return BuildSnapshot();
        }
    }

    public SimulationSnapshot Advance(DateTimeOffset timestamp)
    {
        lock (_gate)
        {
            if (!CanProduceTelemetry())
            {
                throw new InvalidOperationException(
                    "La télémétrie est indisponible pendant une déconnexion simulée.");
            }

            return AdvanceCore(timestamp.ToUniversalTime());
        }
    }

    public bool TryAdvance(DateTimeOffset timestamp, out SimulationSnapshot snapshot)
    {
        lock (_gate)
        {
            if (!CanProduceTelemetry())
            {
                snapshot = null!;
                return false;
            }

            snapshot = AdvanceCore(timestamp.ToUniversalTime());
            return true;
        }
    }

    private SimulationSnapshot AdvanceCore(DateTimeOffset timestamp)
    {
        _lastTimestamp = timestamp;
        _sequenceNumber++;

        if (_machine.State == MachineState.Running)
        {
            AdvanceCycleProgress();
        }

        UpdateFeedRate();
        UpdatePosition();
        UpdateProcessSignals();
        return BuildSnapshot();
    }

    private void AdvanceCycleProgress()
    {
        ProductionRecipe? recipe = _machine.LoadedRecipe;
        if (recipe is null)
        {
            return;
        }

        double progressIncrement = 100 * _tickDuration.TotalSeconds / recipe.EstimatedDuration.TotalSeconds;
        _cycleProgressPercentage = Math.Min(100, _cycleProgressPercentage + progressIncrement);

        if (_cycleProgressPercentage < 100 - 1e-9)
        {
            return;
        }

        _cycleProgressPercentage = 100;
        MachineCommand completed = new(
            CycleCompletedCorrelationId,
            MachineCommandType.CycleCompleted,
            _lastTimestamp);
        StateTransitionResult transition = MachineStateMachine.Transition(_machine, completed);
        if (transition.IsAccepted)
        {
            _machine = transition.Snapshot;
        }
    }

    private void UpdateFeedRate()
    {
        double target = GetTargetFeedRate();
        double maximumDelta = FeedAccelerationMillimetersPerSecondSquared * _tickDuration.TotalSeconds;
        _actualFeedRateMillimetersPerSecond = MoveTowards(
            _actualFeedRateMillimetersPerSecond,
            target,
            maximumDelta);
    }

    private void UpdatePosition()
    {
        ProductionRecipe? recipe = _machine.LoadedRecipe;
        if (recipe is null)
        {
            return;
        }

        double pathProgress = _cycleProgressPercentage / 100 * recipe.PassCount;
        int passIndex = Math.Min((int)Math.Floor(pathProgress), recipe.PassCount - 1);
        double progressWithinPass = Math.Min(1, pathProgress - passIndex);
        double directionalProgress = passIndex % 2 == 0
            ? progressWithinPass
            : 1 - progressWithinPass;

        _headXMillimeters = 100 + (800 * directionalProgress);
        _headYMillimeters = 75 + (45 * passIndex);
        _headZMillimeters = 25 + (1.5 * Math.Sin(Math.PI * progressWithinPass));

        if (GetActiveFault() == FaultType.HeadPositionError)
        {
            _headXMillimeters += 60;
            _headZMillimeters += 15;
        }
    }

    private void UpdateProcessSignals()
    {
        FaultType? activeFault = GetActiveFault();
        ProductionRecipe? recipe = _machine.LoadedRecipe;
        bool cycleContext = _machine.State is MachineState.Running or MachineState.Paused;
        double temperatureTarget = cycleContext && recipe is not null
            ? recipe.TargetTemperatureCelsius
            : AmbientTemperatureCelsius;
        double temperatureStep = 12 * _tickDuration.TotalSeconds;

        _heaterTemperatureCelsius = MoveTowards(
            _heaterTemperatureCelsius,
            temperatureTarget,
            temperatureStep);
        _heaterTemperatureCelsius += NextCenteredNoise(0.03);

        if (activeFault == FaultType.HighTemperature)
        {
            _heaterTemperatureCelsius = (recipe?.TargetTemperatureCelsius ?? 145) + 35;
        }

        double pressureTarget = recipe?.TargetPressureBar ?? 6;
        _materialPressureBar = activeFault == FaultType.LowMaterialPressure
            ? 2.2
            : pressureTarget + NextCenteredNoise(0.04);

        if (activeFault == FaultType.UnstableCompactionForce)
        {
            _compactionForceNewtons = NominalCompactionForceNewtons +
                (140 * Math.Sin(_sequenceNumber * 0.9));
        }
        else if (cycleContext)
        {
            _compactionForceNewtons = NominalCompactionForceNewtons + NextCenteredNoise(5);
        }
        else
        {
            _compactionForceNewtons = 0;
        }

        _processHealthPercentage = activeFault switch
        {
            FaultType.HighTemperature => 20,
            FaultType.LowMaterialPressure => 30,
            FaultType.UnstableCompactionForce => 45,
            FaultType.HeadPositionError => 15,
            FaultType.CommunicationTimeout => 0,
            null => 100,
            _ => 100,
        };
    }

    private SimulationSnapshot BuildSnapshot()
    {
        FaultType? activeFault = GetActiveFault();
        TelemetrySample telemetry = new(
            _lastTimestamp,
            _sequenceNumber,
            _machine.State,
            _headXMillimeters,
            _headYMillimeters,
            _headZMillimeters,
            GetTargetFeedRate(),
            _actualFeedRateMillimetersPerSecond,
            _compactionForceNewtons,
            _heaterTemperatureCelsius,
            _materialPressureBar,
            _cycleProgressPercentage,
            _processHealthPercentage);

        return new SimulationSnapshot(_machine, telemetry, activeFault);
    }

    private void ApplyAcceptedCommandEffects(MachineCommandType commandType)
    {
        if (commandType is MachineCommandType.Start or MachineCommandType.Stop or
            MachineCommandType.Reset or MachineCommandType.Disconnected)
        {
            _cycleProgressPercentage = 0;
        }

        if (commandType == MachineCommandType.Disconnected)
        {
            _actualFeedRateMillimetersPerSecond = 0;
            _compactionForceNewtons = 0;
            _materialPressureBar = 0;
            _processHealthPercentage = 0;
        }
    }

    private bool CanProduceTelemetry() =>
        _machine.State != MachineState.Disconnected &&
        !_machine.ActiveFaults.Contains(FaultType.CommunicationTimeout);

    private double GetTargetFeedRate() => _machine.State == MachineState.Running
        ? _machine.LoadedRecipe?.FeedRateMillimetersPerSecond ?? 0
        : 0;

    private FaultType? GetActiveFault() => _machine.ActiveFaults.Count == 0
        ? null
        : _machine.ActiveFaults.Order().First();

    private double NextCenteredNoise(double amplitude) => (_random.NextDouble() - 0.5) * 2 * amplitude;

    private static double MoveTowards(double current, double target, double maximumDelta)
    {
        double difference = target - current;
        return Math.Abs(difference) <= maximumDelta
            ? target
            : current + (Math.Sign(difference) * maximumDelta);
    }
}

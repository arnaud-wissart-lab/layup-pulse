namespace LayupPulse.Domain;

/// <summary>
/// Regroupe un échantillon séquencé des signaux synthétiques de la cellule.
/// </summary>
public sealed record TelemetrySample
{
    public TelemetrySample(
        DateTimeOffset timestamp,
        long sequenceNumber,
        MachineState machineState,
        double headXMillimeters,
        double headYMillimeters,
        double headZMillimeters,
        double targetFeedRateMillimetersPerSecond,
        double actualFeedRateMillimetersPerSecond,
        double compactionForceNewtons,
        double heaterTemperatureCelsius,
        double materialPressureBar,
        double cycleProgressPercentage,
        double processHealthPercentage,
        IEnumerable<FaultType>? activeFaults = null)
    {
        if (sequenceNumber < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequenceNumber),
                "Le numéro de séquence ne peut pas être négatif.");
        }

        EnsurePercentage(cycleProgressPercentage, nameof(cycleProgressPercentage));
        EnsurePercentage(processHealthPercentage, nameof(processHealthPercentage));

        Timestamp = timestamp.ToUniversalTime();
        SequenceNumber = sequenceNumber;
        MachineState = machineState;
        HeadXMillimeters = headXMillimeters;
        HeadYMillimeters = headYMillimeters;
        HeadZMillimeters = headZMillimeters;
        TargetFeedRateMillimetersPerSecond = targetFeedRateMillimetersPerSecond;
        ActualFeedRateMillimetersPerSecond = actualFeedRateMillimetersPerSecond;
        CompactionForceNewtons = compactionForceNewtons;
        HeaterTemperatureCelsius = heaterTemperatureCelsius;
        MaterialPressureBar = materialPressureBar;
        CycleProgressPercentage = cycleProgressPercentage;
        ProcessHealthPercentage = processHealthPercentage;
        ActiveFaultSignals = activeFaults?.Aggregate(
            0,
            static (signals, fault) => signals | (1 << (int)fault)) ?? 0;
    }

    public DateTimeOffset Timestamp { get; init; }

    public long SequenceNumber { get; init; }

    public MachineState MachineState { get; init; }

    public double HeadXMillimeters { get; init; }

    public double HeadYMillimeters { get; init; }

    public double HeadZMillimeters { get; init; }

    public double TargetFeedRateMillimetersPerSecond { get; init; }

    public double ActualFeedRateMillimetersPerSecond { get; init; }

    public double CompactionForceNewtons { get; init; }

    public double HeaterTemperatureCelsius { get; init; }

    public double MaterialPressureBar { get; init; }

    public double CycleProgressPercentage { get; init; }

    public double ProcessHealthPercentage { get; init; }

    /// <summary>
    /// Signaux de défaut explicitement publiés par le simulateur fictif.
    /// </summary>
    public int ActiveFaultSignals { get; init; }

    public bool HasActiveFault(FaultType fault) =>
        (ActiveFaultSignals & (1 << (int)fault)) != 0;

    public IReadOnlyList<FaultType> GetActiveFaults() => Enum
        .GetValues<FaultType>()
        .Where(HasActiveFault)
        .ToArray();

    private static void EnsurePercentage(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Le pourcentage doit être compris entre 0 et 100.");
        }
    }
}

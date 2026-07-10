namespace LayupPulse.Domain;

/// <summary>
/// Représente le cycle courant ou le dernier cycle terminal de la machine.
/// </summary>
public sealed record ProductionRun
{
    public ProductionRun(
        Guid id,
        ProductionRecipe recipe,
        ProductionRunStatus status,
        DateTimeOffset startedAt,
        DateTimeOffset? endedAt = null,
        MachineState? terminalMachineState = null,
        double completionPercentage = 0,
        string? endReason = null,
        double averageTemperatureCelsius = 0,
        double averagePressureBar = 0,
        double averageCompactionForceNewtons = 0,
        double averageFeedRateMillimetersPerSecond = 0,
        double minimumProcessHealthPercentage = 100,
        int alarmCount = 0)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("L’identifiant d’exécution ne peut pas être vide.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(recipe);

        if (!double.IsFinite(completionPercentage) || completionPercentage is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completionPercentage),
                "La progression doit être comprise entre 0 et 100.");
        }

        if (endedAt is not null && endedAt.Value.ToUniversalTime() < startedAt.ToUniversalTime())
        {
            throw new ArgumentException(
                "L’horodatage de fin ne peut pas précéder le démarrage.",
                nameof(endedAt));
        }

        EnsureFinite(averageTemperatureCelsius, nameof(averageTemperatureCelsius));
        EnsureFinite(averagePressureBar, nameof(averagePressureBar));
        EnsureFinite(averageCompactionForceNewtons, nameof(averageCompactionForceNewtons));
        EnsureFinite(averageFeedRateMillimetersPerSecond, nameof(averageFeedRateMillimetersPerSecond));
        if (!double.IsFinite(minimumProcessHealthPercentage)
            || minimumProcessHealthPercentage is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumProcessHealthPercentage),
                "La santé minimale du procédé doit être comprise entre 0 et 100.");
        }

        if (alarmCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(alarmCount));
        }

        Id = id;
        Recipe = recipe;
        Status = status;
        StartedAt = startedAt.ToUniversalTime();
        EndedAt = endedAt?.ToUniversalTime();
        TerminalMachineState = terminalMachineState;
        CompletionPercentage = completionPercentage;
        EndReason = endReason;
        AverageTemperatureCelsius = averageTemperatureCelsius;
        AveragePressureBar = averagePressureBar;
        AverageCompactionForceNewtons = averageCompactionForceNewtons;
        AverageFeedRateMillimetersPerSecond = averageFeedRateMillimetersPerSecond;
        MinimumProcessHealthPercentage = minimumProcessHealthPercentage;
        AlarmCount = alarmCount;
    }

    public Guid Id { get; init; }

    public ProductionRecipe Recipe { get; init; }

    public ProductionRunStatus Status { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? EndedAt { get; init; }

    public MachineState? TerminalMachineState { get; init; }

    public double CompletionPercentage { get; init; }

    public string? EndReason { get; init; }

    public double AverageTemperatureCelsius { get; init; }

    public double AveragePressureBar { get; init; }

    public double AverageCompactionForceNewtons { get; init; }

    public double AverageFeedRateMillimetersPerSecond { get; init; }

    public double MinimumProcessHealthPercentage { get; init; }

    public int AlarmCount { get; init; }

    internal static ProductionRun Start(Guid id, ProductionRecipe recipe, DateTimeOffset timestamp) =>
        new(id, recipe, ProductionRunStatus.Running, timestamp);

    internal ProductionRun Pause() => this;

    internal ProductionRun Resume() => this;

    internal ProductionRun Complete(DateTimeOffset timestamp) => this with
    {
        Status = ProductionRunStatus.Completed,
        EndedAt = timestamp.ToUniversalTime(),
        TerminalMachineState = MachineState.Completed,
        CompletionPercentage = 100,
        EndReason = null,
    };

    internal ProductionRun Abort(DateTimeOffset timestamp, MachineState terminalState, string reason) => this with
    {
        Status = ProductionRunStatus.Aborted,
        EndedAt = timestamp.ToUniversalTime(),
        TerminalMachineState = terminalState,
        EndReason = reason,
    };

    internal ProductionRun Fault(DateTimeOffset timestamp, string reason) => this with
    {
        Status = ProductionRunStatus.Faulted,
        EndedAt = timestamp.ToUniversalTime(),
        TerminalMachineState = MachineState.Faulted,
        EndReason = reason,
    };

    private static void EnsureFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, "La valeur doit être finie.");
        }
    }
}

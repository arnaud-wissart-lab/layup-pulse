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
        string? endReason = null)
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

        Id = id;
        Recipe = recipe;
        Status = status;
        StartedAt = startedAt.ToUniversalTime();
        EndedAt = endedAt?.ToUniversalTime();
        TerminalMachineState = terminalMachineState;
        CompletionPercentage = completionPercentage;
        EndReason = endReason;
    }

    public Guid Id { get; init; }

    public ProductionRecipe Recipe { get; init; }

    public ProductionRunStatus Status { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? EndedAt { get; init; }

    public MachineState? TerminalMachineState { get; init; }

    public double CompletionPercentage { get; init; }

    public string? EndReason { get; init; }

    internal static ProductionRun Start(Guid id, ProductionRecipe recipe, DateTimeOffset timestamp) =>
        new(id, recipe, ProductionRunStatus.Running, timestamp);

    internal ProductionRun Pause() => this with { Status = ProductionRunStatus.Paused };

    internal ProductionRun Resume() => this with { Status = ProductionRunStatus.Running };

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

    internal ProductionRun Fail(DateTimeOffset timestamp, string reason) => this with
    {
        Status = ProductionRunStatus.Failed,
        EndedAt = timestamp.ToUniversalTime(),
        TerminalMachineState = MachineState.Faulted,
        EndReason = reason,
    };
}

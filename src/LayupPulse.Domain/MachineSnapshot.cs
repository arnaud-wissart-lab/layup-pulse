using System.Collections.Immutable;

namespace LayupPulse.Domain;

/// <summary>
/// Capture immuable du contexte nécessaire aux décisions de la machine d’états.
/// </summary>
public sealed record MachineSnapshot
{
    public MachineSnapshot(
        MachineState state,
        DateTimeOffset timestamp,
        ProductionRecipe? loadedRecipe = null,
        ProductionRun? currentRun = null,
        IEnumerable<FaultType>? activeFaults = null)
    {
        State = state;
        Timestamp = timestamp.ToUniversalTime();
        LoadedRecipe = loadedRecipe;
        CurrentRun = currentRun;
        ActiveFaults = activeFaults?.ToImmutableHashSet() ?? [];
    }

    public MachineState State { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    public ProductionRecipe? LoadedRecipe { get; init; }

    public ProductionRun? CurrentRun { get; init; }

    public ImmutableHashSet<FaultType> ActiveFaults { get; init; }

    /// <summary>
    /// Crée le contexte initial sans dépendre d’une horloge globale.
    /// </summary>
    public static MachineSnapshot Disconnected(DateTimeOffset timestamp) =>
        new(MachineState.Disconnected, timestamp);
}

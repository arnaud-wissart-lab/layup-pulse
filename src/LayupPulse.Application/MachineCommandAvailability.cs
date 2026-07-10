using LayupPulse.Domain;

namespace LayupPulse.Application;

/// <summary>
/// Projette les préconditions métier observables en disponibilité de commandes opérateur.
/// </summary>
public static class MachineCommandAvailability
{
    public static bool CanExecute(MachineSnapshot snapshot, MachineCommandType commandType)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return commandType switch
        {
            MachineCommandType.LoadRecipe => snapshot.State == MachineState.Ready,
            MachineCommandType.Start => snapshot.State == MachineState.Ready
                && snapshot.LoadedRecipe?.Validate().IsValid == true,
            MachineCommandType.Pause => snapshot.State == MachineState.Running,
            MachineCommandType.Resume => snapshot.State == MachineState.Paused,
            MachineCommandType.Stop => snapshot.State is MachineState.Running or MachineState.Paused,
            MachineCommandType.Reset => snapshot.State == MachineState.Completed
                || snapshot.State == MachineState.Faulted && snapshot.ActiveFaults.Count == 0,
            _ => false,
        };
    }
}

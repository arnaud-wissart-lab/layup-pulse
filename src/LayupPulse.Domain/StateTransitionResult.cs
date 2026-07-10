namespace LayupPulse.Domain;

/// <summary>
/// Décrit le résultat accepté ou rejeté d’une décision de la machine d’états.
/// </summary>
public sealed class StateTransitionResult
{
    private StateTransitionResult(
        bool isAccepted,
        MachineState previousState,
        MachineSnapshot snapshot,
        StateTransitionRejection? rejection)
    {
        IsAccepted = isAccepted;
        PreviousState = previousState;
        Snapshot = snapshot;
        Rejection = rejection;
    }

    public bool IsAccepted { get; }

    public MachineState PreviousState { get; }

    public MachineSnapshot Snapshot { get; }

    public StateTransitionRejection? Rejection { get; }

    internal static StateTransitionResult Accepted(MachineState previousState, MachineSnapshot snapshot) =>
        new(true, previousState, snapshot, null);

    internal static StateTransitionResult Rejected(
        MachineSnapshot snapshot,
        StateTransitionRejection rejection) =>
        new(false, snapshot.State, snapshot, rejection);
}

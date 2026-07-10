namespace LayupPulse.Domain;

/// <summary>
/// Identifie une commande opérateur ou un événement déterministe traité par la machine d’états.
/// </summary>
public enum MachineCommandType
{
    ConnectRequested,
    ConnectionEstablished,
    ConnectionFailed,
    LoadRecipe,
    Start,
    Pause,
    Resume,
    Stop,
    CycleCompleted,
    Reset,
    CriticalFaultRaised,
    FaultCleared,
    Disconnected,
}

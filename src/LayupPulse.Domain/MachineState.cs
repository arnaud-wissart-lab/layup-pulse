namespace LayupPulse.Domain;

/// <summary>
/// Décrit l’état de premier niveau de la cellule simulée.
/// </summary>
public enum MachineState
{
    Disconnected,
    Connecting,
    Ready,
    Running,
    Paused,
    Faulted,
    Completed,
}

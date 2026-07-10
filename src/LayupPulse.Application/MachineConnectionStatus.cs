namespace LayupPulse.Application;

/// <summary>
/// Décrit l’état de la session du point de vue de l’application cliente.
/// </summary>
public enum MachineConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    Stale,
    Reconnecting,
    Disconnecting,
}

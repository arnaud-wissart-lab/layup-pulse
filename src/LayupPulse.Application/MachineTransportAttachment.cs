using LayupPulse.Domain;

namespace LayupPulse.Application;

/// <summary>
/// Regroupe une session de transport locale et l’instantané observé lors de son attachement.
/// </summary>
public sealed record MachineTransportAttachment(
    IMachineSession Session,
    MachineSnapshot Snapshot);

namespace LayupPulse.Application;

/// <summary>
/// Classe les défaillances de communication sans exposer le transport concret.
/// </summary>
public enum MachineGatewayFailureKind
{
    Unavailable,
    Timeout,
    Interrupted,
    InvalidResponse,
    CommandRejected,
    Unexpected,
}

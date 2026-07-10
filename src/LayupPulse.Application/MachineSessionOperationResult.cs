namespace LayupPulse.Application;

/// <summary>
/// Rend une opération de cycle de vie exploitable par l’interface sans exception de transport.
/// </summary>
public sealed record MachineSessionOperationResult(
    bool IsSuccessful,
    string Message,
    MachineGatewayFailureKind? FailureKind = null)
{
    public static MachineSessionOperationResult Successful(string message) => new(true, message);

    public static MachineSessionOperationResult Failed(
        string message,
        MachineGatewayFailureKind failureKind) => new(false, message, failureKind);
}

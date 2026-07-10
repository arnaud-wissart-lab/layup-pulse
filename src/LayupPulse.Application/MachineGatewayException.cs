namespace LayupPulse.Application;

/// <summary>
/// Signale une défaillance traduite par un adaptateur de communication machine.
/// </summary>
public sealed class MachineGatewayException : Exception
{
    public MachineGatewayException(
        MachineGatewayFailureKind failureKind,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        FailureKind = failureKind;
    }

    public MachineGatewayFailureKind FailureKind { get; }
}

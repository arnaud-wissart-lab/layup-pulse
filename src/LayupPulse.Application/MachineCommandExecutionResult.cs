using LayupPulse.Domain;

namespace LayupPulse.Application;

/// <summary>
/// Distingue une acceptation, un rejet métier et une défaillance de communication.
/// </summary>
public sealed record MachineCommandExecutionResult(
    MachineCommandExecutionStatus Status,
    string Message,
    CommandResult? CommandResult = null,
    MachineGatewayFailureKind? FailureKind = null)
{
    public bool IsAccepted => Status == MachineCommandExecutionStatus.Accepted;

    public static MachineCommandExecutionResult FromCommandResult(CommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsAccepted
            ? new(MachineCommandExecutionStatus.Accepted, "Commande acceptée.", result)
            : new(
                MachineCommandExecutionStatus.Rejected,
                result.Transition.Rejection?.Message ?? "Commande rejetée.",
                result);
    }

    public static MachineCommandExecutionResult Failed(
        string message,
        MachineGatewayFailureKind failureKind) =>
        new(MachineCommandExecutionStatus.Failed, message, FailureKind: failureKind);

    public static MachineCommandExecutionResult NotConnected() =>
        new(MachineCommandExecutionStatus.NotConnected, "Aucune session machine active.");
}

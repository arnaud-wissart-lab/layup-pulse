using LayupPulse.Domain;

namespace LayupPulse.Application;

/// <summary>
/// Expose exclusivement les défauts du démonstrateur simulé ; ce port ne représente aucune fonction de sécurité.
/// </summary>
public interface IDemoFaultGateway
{
    public Task<CommandResult> InjectFaultAsync(
        IMachineSession session,
        Guid correlationId,
        FaultType fault,
        CancellationToken cancellationToken);

    public Task<CommandResult> ClearFaultAsync(
        IMachineSession session,
        Guid correlationId,
        FaultType fault,
        CancellationToken cancellationToken);
}

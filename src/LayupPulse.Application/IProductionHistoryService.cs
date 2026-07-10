using LayupPulse.Domain;

namespace LayupPulse.Application;

/// <summary>
/// Expose les cas d’usage de lecture et d’effacement de l’historique local.
/// </summary>
public interface IProductionHistoryService
{
    public Task<IReadOnlyList<ProductionRun>> GetRunsAsync(
        ProductionHistoryFilter filter,
        CancellationToken cancellationToken);

    public Task<ProductionRunDetails?> GetDetailsAsync(
        Guid productionRunId,
        CancellationToken cancellationToken);

    public Task ClearLocalDataAsync(CancellationToken cancellationToken);
}

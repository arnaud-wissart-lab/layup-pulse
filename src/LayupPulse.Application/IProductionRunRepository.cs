using LayupPulse.Domain;

namespace LayupPulse.Application;

/// <summary>
/// Persiste les frontières et l’issue des exécutions de production simulées.
/// </summary>
public interface IProductionRunRepository
{
    public Task SaveAsync(ProductionRun productionRun, CancellationToken cancellationToken);

    public Task<ProductionRun?> GetByIdAsync(Guid productionRunId, CancellationToken cancellationToken);

    public IAsyncEnumerable<ProductionRun> GetRecentAsync(
        int maximumCount,
        CancellationToken cancellationToken);
}

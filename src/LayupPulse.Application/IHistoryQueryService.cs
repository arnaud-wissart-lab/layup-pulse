using LayupPulse.Domain;

namespace LayupPulse.Application;

/// <summary>
/// Expose des lectures d’historique asynchrones, annulables et explicitement bornées.
/// </summary>
public interface IHistoryQueryService
{
    public Task<IReadOnlyList<ProductionRunHistoryItem>> GetRecentRunsAsync(
        ProductionRunStatus? status,
        int maximumCount,
        CancellationToken cancellationToken);

    public Task<ProductionRunHistoryDetails?> GetRunDetailsAsync(
        Guid productionRunId,
        int maximumAlarmCount,
        int maximumAggregateCount,
        CancellationToken cancellationToken);
}

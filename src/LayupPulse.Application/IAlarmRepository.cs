using LayupPulse.Domain;

namespace LayupPulse.Application;

/// <summary>
/// Persiste les occurrences d’alarme et permet une lecture bornée de leur historique.
/// </summary>
public interface IAlarmRepository
{
    public Task SaveAsync(AlarmEvent alarmEvent, CancellationToken cancellationToken);

    public IAsyncEnumerable<AlarmEvent> GetActiveAsync(CancellationToken cancellationToken);

    public IAsyncEnumerable<AlarmEvent> GetRecentAsync(
        int maximumCount,
        CancellationToken cancellationToken);

    public IAsyncEnumerable<AlarmEvent> GetByProductionRunIdAsync(
        Guid productionRunId,
        CancellationToken cancellationToken);
}

using LayupPulse.Domain;

namespace LayupPulse.Application;

/// <summary>
/// Décrit une lecture bornée de l’historique de production.
/// </summary>
public sealed record ProductionRunQuery
{
    public ProductionRunQuery(
        int maximumCount,
        ProductionRunStatus? status = null,
        DateTimeOffset? startedAfter = null)
    {
        if (maximumCount is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        MaximumCount = maximumCount;
        Status = status;
        StartedAfter = startedAfter?.ToUniversalTime();
    }

    public int MaximumCount { get; }

    public ProductionRunStatus? Status { get; }

    public DateTimeOffset? StartedAfter { get; }
}

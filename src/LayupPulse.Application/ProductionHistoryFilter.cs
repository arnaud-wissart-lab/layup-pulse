using LayupPulse.Domain;

namespace LayupPulse.Application;

/// <summary>
/// Porte les filtres pratiques exposés par la page d’historique.
/// </summary>
public sealed record ProductionHistoryFilter(
    ProductionRunStatus? Status,
    DateTimeOffset? StartedAfter,
    int MaximumCount = 200);

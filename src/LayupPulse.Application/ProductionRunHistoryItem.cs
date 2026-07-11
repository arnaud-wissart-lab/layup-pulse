using LayupPulse.Domain;

namespace LayupPulse.Application;

/// <summary>
/// Modèle de lecture synthétique d’une exécution persistée.
/// </summary>
public sealed record ProductionRunHistoryItem(
    Guid Id,
    string RecipeName,
    string PartReference,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    ProductionRunStatus Status,
    double CompletionPercentage,
    int AlarmCount,
    double AverageTemperatureCelsius,
    double AveragePressureBar,
    double AverageCompactionForceNewtons,
    double AverageFeedRateMillimetersPerSecond,
    double MinimumProcessHealthPercentage);

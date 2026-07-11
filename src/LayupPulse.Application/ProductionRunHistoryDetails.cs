namespace LayupPulse.Application;

/// <summary>
/// Regroupe les détails bornés associés à une exécution persistée.
/// </summary>
public sealed record ProductionRunHistoryDetails(
    ProductionRunHistoryItem Run,
    IReadOnlyList<AlarmHistoryItem> Alarms,
    IReadOnlyList<TelemetryAggregateHistoryItem> TelemetryAggregates);

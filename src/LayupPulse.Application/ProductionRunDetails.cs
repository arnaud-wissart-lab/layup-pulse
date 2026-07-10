using LayupPulse.Domain;

namespace LayupPulse.Application;

/// <summary>
/// Regroupe le résumé d’un run et ses données de détail bornées par ce run.
/// </summary>
public sealed record ProductionRunDetails(
    ProductionRun ProductionRun,
    IReadOnlyList<AlarmEvent> Alarms,
    IReadOnlyList<TelemetryAggregate> TelemetryAggregates);

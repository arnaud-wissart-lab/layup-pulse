using LayupPulse.Domain;

namespace LayupPulse.Application;

/// <summary>
/// Reçoit les événements durables sans exposer la technologie de stockage au pipeline.
/// </summary>
public interface IHistoryWriter
{
    public event EventHandler<HistoryPersistenceDiagnosticEventArgs>? DiagnosticOccurred;

    public string? LastDiagnosticMessage { get; }

    public bool TryRecordProductionRun(ProductionRun run);

    public bool TryRecordTelemetryAggregate(TelemetryAggregate aggregate);

    public bool TryRecordAlarm(AlarmEvent alarm);
}

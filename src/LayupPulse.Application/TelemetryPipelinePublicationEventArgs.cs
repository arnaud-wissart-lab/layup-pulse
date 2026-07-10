using LayupPulse.Domain;

namespace LayupPulse.Application;

/// <summary>
/// Capture immuable publiée à la cadence de présentation, sans dépendance à WPF.
/// </summary>
public sealed class TelemetryPipelinePublicationEventArgs : EventArgs
{
    public TelemetryPipelinePublicationEventArgs(
        TelemetrySample? latestTelemetry,
        DateTimeOffset? lastTelemetryReceivedAt,
        TelemetryAggregate? latestAggregate,
        TelemetryPipelineMetrics metrics,
        IReadOnlyList<AlarmEvent> activeAlarms,
        IReadOnlyList<AlarmEvent> alarmHistory)
    {
        LatestTelemetry = latestTelemetry;
        LastTelemetryReceivedAt = lastTelemetryReceivedAt;
        LatestAggregate = latestAggregate;
        Metrics = metrics;
        ActiveAlarms = activeAlarms;
        AlarmHistory = alarmHistory;
    }

    public TelemetrySample? LatestTelemetry { get; }

    public DateTimeOffset? LastTelemetryReceivedAt { get; }

    public TelemetryAggregate? LatestAggregate { get; }

    public TelemetryPipelineMetrics Metrics { get; }

    public IReadOnlyList<AlarmEvent> ActiveAlarms { get; }

    public IReadOnlyList<AlarmEvent> AlarmHistory { get; }
}

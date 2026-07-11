namespace LayupPulse.Application;

/// <summary>
/// Transporte un agrégat d’une seconde terminé vers un consommateur hors pipeline.
/// </summary>
public sealed class TelemetryAggregateCompletedEventArgs(TelemetryAggregate aggregate) : EventArgs
{
    public TelemetryAggregate Aggregate { get; } = aggregate;
}

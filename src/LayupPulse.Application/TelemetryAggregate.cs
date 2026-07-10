namespace LayupPulse.Application;

/// <summary>
/// Résume une fenêtre d’une seconde destinée à une future persistance, sans l’effectuer.
/// </summary>
public sealed record TelemetryAggregate(
    DateTimeOffset WindowStartedAt,
    DateTimeOffset WindowEndedAt,
    int SampleCount,
    long FirstSequenceNumber,
    long LastSequenceNumber,
    double AverageFeedRateMillimetersPerSecond,
    double AverageCompactionForceNewtons,
    double AverageHeaterTemperatureCelsius,
    double AverageMaterialPressureBar,
    double MinimumCompactionForceNewtons,
    double MaximumCompactionForceNewtons);

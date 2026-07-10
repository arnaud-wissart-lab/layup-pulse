namespace LayupPulse.Application;

/// <summary>
/// Résume une fenêtre télémétrique d’une seconde associée à une exécution de production.
/// </summary>
public sealed record TelemetryAggregate(
    Guid Id,
    Guid ProductionRunId,
    DateTimeOffset WindowStartedAt,
    DateTimeOffset WindowEndedAt,
    int SampleCount,
    long FirstSequenceNumber,
    long LastSequenceNumber,
    double AverageFeedRateMillimetersPerSecond,
    double AverageCompactionForceNewtons,
    double AverageHeaterTemperatureCelsius,
    double MinimumHeaterTemperatureCelsius,
    double MaximumHeaterTemperatureCelsius,
    double AverageMaterialPressureBar,
    double MinimumMaterialPressureBar,
    double MaximumMaterialPressureBar,
    double MinimumCompactionForceNewtons,
    double MaximumCompactionForceNewtons,
    double AverageProcessHealthPercentage,
    double MinimumProcessHealthPercentage,
    double EndOfBucketCycleProgressPercentage);

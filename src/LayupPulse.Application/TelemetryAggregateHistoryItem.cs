namespace LayupPulse.Application;

/// <summary>
/// Modèle de lecture d’un bucket télémétrique UTC d’une seconde.
/// </summary>
public sealed record TelemetryAggregateHistoryItem(
    DateTimeOffset BucketStartedAt,
    int SampleCount,
    double AverageHeaterTemperatureCelsius,
    double MinimumHeaterTemperatureCelsius,
    double MaximumHeaterTemperatureCelsius,
    double AverageMaterialPressureBar,
    double MinimumMaterialPressureBar,
    double MaximumMaterialPressureBar,
    double AverageCompactionForceNewtons,
    double MinimumCompactionForceNewtons,
    double MaximumCompactionForceNewtons,
    double AverageFeedRateMillimetersPerSecond,
    double AverageProcessHealthPercentage,
    double MinimumProcessHealthPercentage,
    double EndOfBucketCycleProgressPercentage);

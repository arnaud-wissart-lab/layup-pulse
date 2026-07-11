namespace LayupPulse.Infrastructure.Persistence;

/// <summary>
/// Représentation EF Core d’un bucket télémétrique UTC d’une seconde.
/// </summary>
public sealed class TelemetryAggregateRecord
{
    public Guid ProductionRunId { get; set; }

    public DateTime BucketStartedAtUtc { get; set; }

    public int SampleCount { get; set; }

    public double AverageFeedRateMillimetersPerSecond { get; set; }

    public double AverageCompactionForceNewtons { get; set; }

    public double AverageHeaterTemperatureCelsius { get; set; }

    public double MinimumHeaterTemperatureCelsius { get; set; }

    public double MaximumHeaterTemperatureCelsius { get; set; }

    public double AverageMaterialPressureBar { get; set; }

    public double MinimumMaterialPressureBar { get; set; }

    public double MaximumMaterialPressureBar { get; set; }

    public double MinimumCompactionForceNewtons { get; set; }

    public double MaximumCompactionForceNewtons { get; set; }

    public double AverageProcessHealthPercentage { get; set; }

    public double MinimumProcessHealthPercentage { get; set; }

    public double EndOfBucketCycleProgressPercentage { get; set; }

    public ProductionRunRecord ProductionRun { get; set; } = null!;
}

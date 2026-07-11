using System.Globalization;
using LayupPulse.Application;

namespace LayupPulse.Desktop;

public sealed class HistoryTelemetryRowViewModel(TelemetryAggregateHistoryItem aggregate)
{
    public string Bucket => aggregate.BucketStartedAt
        .ToLocalTime()
        .ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.CurrentCulture);

    public int SampleCount => aggregate.SampleCount;

    public string Temperature =>
        $"{aggregate.AverageHeaterTemperatureCelsius:F1} " +
        $"[{aggregate.MinimumHeaterTemperatureCelsius:F1}–{aggregate.MaximumHeaterTemperatureCelsius:F1}] °C";

    public string Pressure =>
        $"{aggregate.AverageMaterialPressureBar:F2} " +
        $"[{aggregate.MinimumMaterialPressureBar:F2}–{aggregate.MaximumMaterialPressureBar:F2}] bar";

    public string Force =>
        $"{aggregate.AverageCompactionForceNewtons:F0} " +
        $"[{aggregate.MinimumCompactionForceNewtons:F0}–{aggregate.MaximumCompactionForceNewtons:F0}] N";

    public string FeedRate => $"{aggregate.AverageFeedRateMillimetersPerSecond:F1} mm/s";

    public string ProcessHealth =>
        $"{aggregate.AverageProcessHealthPercentage:F1} / min. " +
        $"{aggregate.MinimumProcessHealthPercentage:F1} %";

    public string Progress => $"{aggregate.EndOfBucketCycleProgressPercentage:F1} %";
}

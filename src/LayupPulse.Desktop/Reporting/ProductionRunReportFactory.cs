using System.Collections.Immutable;
using LayupPulse.Application;

namespace LayupPulse.Desktop.Reporting;

/// <summary>
/// Projette les détails d’historique vers un modèle de rapport borné.
/// </summary>
public static class ProductionRunReportFactory
{
    public const int MaximumDetailedAlarmCount = 100;
    public const string ReportTitle = "Rapport de cycle LayupPulse";
    public const string DemonstratorWarning =
        "Démonstrateur logiciel — données simulées — non validé pour la production";

    public static ProductionRunReport Create(
        ProductionRunHistoryDetails details,
        DateTimeOffset generatedAt,
        string applicationVersion)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationVersion);

        ImmutableArray<AlarmHistoryItem> detailedAlarms = details.Alarms
            .Take(MaximumDetailedAlarmCount)
            .ToImmutableArray();
        int alarmCount = Math.Max(details.Run.AlarmCount, details.Alarms.Count);

        return new ProductionRunReport(
            ReportTitle,
            DemonstratorWarning,
            details.Run.Id,
            details.Run.RecipeName,
            details.Run.PartReference,
            details.Run.StartedAt,
            details.Run.EndedAt,
            details.Run.EndedAt - details.Run.StartedAt,
            details.Run.Status,
            details.Run.CompletionPercentage,
            alarmCount,
            details.Run.AverageTemperatureCelsius,
            details.Run.AveragePressureBar,
            details.Run.AverageCompactionForceNewtons,
            details.Run.AverageFeedRateMillimetersPerSecond,
            details.Run.MinimumProcessHealthPercentage,
            CreateTelemetrySummary(details.TelemetryAggregates),
            detailedAlarms,
            alarmCount - detailedAlarms.Length,
            generatedAt,
            applicationVersion);
    }

    private static ProductionRunReportTelemetrySummary? CreateTelemetrySummary(
        IReadOnlyList<TelemetryAggregateHistoryItem> aggregates)
    {
        if (aggregates.Count == 0)
        {
            return null;
        }

        TelemetryAggregateHistoryItem first = aggregates[0];
        DateTimeOffset periodStartedAt = first.BucketStartedAt;
        DateTimeOffset periodEndedAt = first.BucketStartedAt.AddSeconds(1);
        long totalSampleCount = first.SampleCount;
        double minimumTemperature = first.MinimumHeaterTemperatureCelsius;
        double maximumTemperature = first.MaximumHeaterTemperatureCelsius;
        double minimumPressure = first.MinimumMaterialPressureBar;
        double maximumPressure = first.MaximumMaterialPressureBar;
        double minimumForce = first.MinimumCompactionForceNewtons;
        double maximumForce = first.MaximumCompactionForceNewtons;
        double minimumAverageFeedRate = first.AverageFeedRateMillimetersPerSecond;
        double maximumAverageFeedRate = first.AverageFeedRateMillimetersPerSecond;

        for (int index = 1; index < aggregates.Count; index++)
        {
            TelemetryAggregateHistoryItem aggregate = aggregates[index];
            periodStartedAt = DateTimeOffset.Compare(
                aggregate.BucketStartedAt,
                periodStartedAt) < 0
                ? aggregate.BucketStartedAt
                : periodStartedAt;
            DateTimeOffset bucketEndedAt = aggregate.BucketStartedAt.AddSeconds(1);
            periodEndedAt = DateTimeOffset.Compare(bucketEndedAt, periodEndedAt) > 0
                ? bucketEndedAt
                : periodEndedAt;
            totalSampleCount += aggregate.SampleCount;
            minimumTemperature = Math.Min(
                minimumTemperature,
                aggregate.MinimumHeaterTemperatureCelsius);
            maximumTemperature = Math.Max(
                maximumTemperature,
                aggregate.MaximumHeaterTemperatureCelsius);
            minimumPressure = Math.Min(minimumPressure, aggregate.MinimumMaterialPressureBar);
            maximumPressure = Math.Max(maximumPressure, aggregate.MaximumMaterialPressureBar);
            minimumForce = Math.Min(minimumForce, aggregate.MinimumCompactionForceNewtons);
            maximumForce = Math.Max(maximumForce, aggregate.MaximumCompactionForceNewtons);
            minimumAverageFeedRate = Math.Min(
                minimumAverageFeedRate,
                aggregate.AverageFeedRateMillimetersPerSecond);
            maximumAverageFeedRate = Math.Max(
                maximumAverageFeedRate,
                aggregate.AverageFeedRateMillimetersPerSecond);
        }

        return new ProductionRunReportTelemetrySummary(
            periodStartedAt,
            periodEndedAt,
            aggregates.Count,
            totalSampleCount,
            new ProductionRunReportRange(minimumTemperature, maximumTemperature),
            new ProductionRunReportRange(minimumPressure, maximumPressure),
            new ProductionRunReportRange(minimumForce, maximumForce),
            new ProductionRunReportRange(minimumAverageFeedRate, maximumAverageFeedRate));
    }
}

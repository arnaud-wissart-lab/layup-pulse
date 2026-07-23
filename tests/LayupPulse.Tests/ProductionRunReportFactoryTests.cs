using LayupPulse.Application;
using LayupPulse.Desktop.Reporting;
using LayupPulse.Domain;
using Xunit;

namespace LayupPulse.Tests;

public sealed class ProductionRunReportFactoryTests
{
    private static readonly DateTimeOffset StartedAt =
        new(2026, 7, 23, 8, 15, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EndedAt = StartedAt.AddMinutes(12).AddSeconds(34);
    private static readonly DateTimeOffset GeneratedAt = EndedAt.AddMinutes(5);

    [Fact]
    public void ProjectsEveryRunAlarmAndMetadataField()
    {
        Guid runId = Guid.Parse("12345678-1234-1234-1234-1234567890ab");
        Guid alarmId = Guid.Parse("abcdefab-cdef-cdef-cdef-abcdefabcdef");
        AlarmHistoryItem alarm = new(
            alarmId,
            AlarmCode.HeadPositionError,
            AlarmSeverity.Critical,
            "head-position",
            "Écart de position simulé.",
            StartedAt.AddMinutes(2),
            StartedAt.AddMinutes(3),
            StartedAt.AddMinutes(4));
        ProductionRunHistoryDetails details = CreateDetails(
            runId,
            EndedAt,
            ProductionRunStatus.Faulted,
            alarmCount: 1,
            alarms: [alarm]);

        ProductionRunReport report = ProductionRunReportFactory.Create(
            details,
            GeneratedAt,
            "0.3.0");

        Assert.Equal(ProductionRunReportFactory.ReportTitle, report.Title);
        Assert.Equal(
            "Démonstrateur logiciel — données simulées — non validé pour la production",
            report.Warning);
        Assert.Equal(runId, report.ProductionRunId);
        Assert.Equal("Wing Panel Demo", report.RecipeName);
        Assert.Equal("LP-WING-DEMO-001", report.PartReference);
        Assert.Equal(StartedAt, report.StartedAt);
        Assert.Equal(EndedAt, report.EndedAt);
        Assert.Equal(EndedAt - StartedAt, report.Duration);
        Assert.Equal(ProductionRunStatus.Faulted, report.FinalStatus);
        Assert.Equal(72.5, report.CompletionPercentage);
        Assert.Equal(1, report.AlarmCount);
        Assert.Equal(146.25, report.AverageTemperatureCelsius);
        Assert.Equal(5.95, report.AveragePressureBar);
        Assert.Equal(448.5, report.AverageCompactionForceNewtons);
        Assert.Equal(117.25, report.AverageFeedRateMillimetersPerSecond);
        Assert.Equal(64.5, report.MinimumProcessHealthPercentage);
        Assert.Null(report.TelemetrySummary);
        Assert.Equal(GeneratedAt, report.GeneratedAt);
        Assert.Equal("0.3.0", report.ApplicationVersion);
        Assert.Equal(0, report.OmittedAlarmCount);

        AlarmHistoryItem projectedAlarm = Assert.Single(report.DetailedAlarms);
        Assert.Same(alarm, projectedAlarm);
        Assert.Equal(alarmId, projectedAlarm.Id);
        Assert.Equal(AlarmCode.HeadPositionError, projectedAlarm.Code);
        Assert.Equal(AlarmSeverity.Critical, projectedAlarm.Severity);
        Assert.Equal("head-position", projectedAlarm.Source);
        Assert.Equal("Écart de position simulé.", projectedAlarm.Message);
        Assert.Equal(StartedAt.AddMinutes(2), projectedAlarm.RaisedAt);
        Assert.Equal(StartedAt.AddMinutes(3), projectedAlarm.AcknowledgedAt);
        Assert.Equal(StartedAt.AddMinutes(4), projectedAlarm.ClearedAt);
    }

    [Fact]
    public void SummarizesUnorderedTelemetryBucketsWithGlobalRanges()
    {
        TelemetryAggregateHistoryItem late = CreateAggregate(
            StartedAt.AddSeconds(2),
            sampleCount: 7,
            minimumTemperature: 149,
            maximumTemperature: 156,
            minimumPressure: 5.8,
            maximumPressure: 6.2,
            minimumForce: 440,
            maximumForce: 460,
            averageFeedRate: 121);
        TelemetryAggregateHistoryItem early = CreateAggregate(
            StartedAt,
            sampleCount: 11,
            minimumTemperature: 139,
            maximumTemperature: 144,
            minimumPressure: 5.9,
            maximumPressure: 6,
            minimumForce: 430,
            maximumForce: 451,
            averageFeedRate: 115);
        TelemetryAggregateHistoryItem middle = CreateAggregate(
            StartedAt.AddSeconds(1),
            sampleCount: 13,
            minimumTemperature: 141,
            maximumTemperature: 165,
            minimumPressure: 5.5,
            maximumPressure: 6.3,
            minimumForce: 445,
            maximumForce: 480,
            averageFeedRate: 118);
        ProductionRunHistoryDetails details = CreateDetails(
            Guid.NewGuid(),
            EndedAt,
            ProductionRunStatus.Completed,
            telemetryAggregates: [late, early, middle]);

        ProductionRunReport report = ProductionRunReportFactory.Create(
            details,
            GeneratedAt,
            "0.3.0");

        ProductionRunReportTelemetrySummary summary =
            Assert.IsType<ProductionRunReportTelemetrySummary>(report.TelemetrySummary);
        Assert.Equal(StartedAt, summary.PeriodStartedAt);
        Assert.Equal(StartedAt.AddSeconds(3), summary.PeriodEndedAt);
        Assert.Equal(3, summary.BucketCount);
        Assert.Equal(31, summary.TotalSampleCount);
        Assert.Equal(new ProductionRunReportRange(139, 165), summary.TemperatureCelsiusRange);
        Assert.Equal(new ProductionRunReportRange(5.5, 6.3), summary.PressureBarRange);
        Assert.Equal(new ProductionRunReportRange(430, 480), summary.CompactionForceNewtonsRange);
        Assert.Equal(
            new ProductionRunReportRange(115, 121),
            summary.AverageFeedRateMillimetersPerSecondRange);
    }

    [Fact]
    public void KeepsMissingEndTelemetryAndAlarmsExplicitlyAbsent()
    {
        ProductionRunHistoryDetails details = CreateDetails(
            Guid.NewGuid(),
            endedAt: null,
            status: ProductionRunStatus.Running);

        ProductionRunReport report = ProductionRunReportFactory.Create(
            details,
            GeneratedAt,
            "0.3.0");

        Assert.Null(report.EndedAt);
        Assert.Null(report.Duration);
        Assert.Null(report.TelemetrySummary);
        Assert.Empty(report.DetailedAlarms);
        Assert.Equal(0, report.AlarmCount);
        Assert.Equal(0, report.OmittedAlarmCount);
    }

    [Fact]
    public void LimitsDetailedAlarmsToOneHundredAndReportsOmittedCount()
    {
        AlarmHistoryItem[] alarms = Enumerable.Range(0, 105)
            .Select(CreateAlarm)
            .ToArray();
        ProductionRunHistoryDetails details = CreateDetails(
            Guid.NewGuid(),
            EndedAt,
            ProductionRunStatus.Faulted,
            alarmCount: alarms.Length,
            alarms: alarms);

        ProductionRunReport report = ProductionRunReportFactory.Create(
            details,
            GeneratedAt,
            "0.3.0");

        Assert.Equal(105, report.AlarmCount);
        Assert.Equal(ProductionRunReportFactory.MaximumDetailedAlarmCount, report.DetailedAlarms.Length);
        Assert.Equal(5, report.OmittedAlarmCount);
        Assert.Equal(alarms[0].Id, report.DetailedAlarms[0].Id);
        Assert.Equal(alarms[99].Id, report.DetailedAlarms[^1].Id);
        Assert.DoesNotContain(report.DetailedAlarms, alarm => alarm.Id == alarms[100].Id);
    }

    [Fact]
    public void ReportsAlarmsOmittedByTheBoundedHistoryQuery()
    {
        AlarmHistoryItem[] loadedAlarms = Enumerable.Range(0, 80)
            .Select(CreateAlarm)
            .ToArray();
        ProductionRunHistoryDetails details = CreateDetails(
            Guid.NewGuid(),
            EndedAt,
            ProductionRunStatus.Faulted,
            alarmCount: 150,
            alarms: loadedAlarms);

        ProductionRunReport report = ProductionRunReportFactory.Create(
            details,
            GeneratedAt,
            "0.3.0");

        Assert.Equal(150, report.AlarmCount);
        Assert.Equal(80, report.DetailedAlarms.Length);
        Assert.Equal(70, report.OmittedAlarmCount);
    }

    private static ProductionRunHistoryDetails CreateDetails(
        Guid runId,
        DateTimeOffset? endedAt,
        ProductionRunStatus status,
        int alarmCount = 0,
        IReadOnlyList<AlarmHistoryItem>? alarms = null,
        IReadOnlyList<TelemetryAggregateHistoryItem>? telemetryAggregates = null) => new(
        new ProductionRunHistoryItem(
            runId,
            "Wing Panel Demo",
            "LP-WING-DEMO-001",
            StartedAt,
            endedAt,
            status,
            72.5,
            alarmCount,
            146.25,
            5.95,
            448.5,
            117.25,
            64.5),
        alarms ?? [],
        telemetryAggregates ?? []);

    private static AlarmHistoryItem CreateAlarm(int index) => new(
        Guid.Parse($"00000000-0000-0000-0000-{index:D12}"),
        AlarmCode.HighTemperature,
        AlarmSeverity.Warning,
        "heater-temperature",
        $"Alarme simulée {index}.",
        StartedAt.AddSeconds(index),
        AcknowledgedAt: null,
        ClearedAt: null);

    private static TelemetryAggregateHistoryItem CreateAggregate(
        DateTimeOffset bucketStartedAt,
        int sampleCount,
        double minimumTemperature,
        double maximumTemperature,
        double minimumPressure,
        double maximumPressure,
        double minimumForce,
        double maximumForce,
        double averageFeedRate) => new(
        bucketStartedAt,
        sampleCount,
        AverageHeaterTemperatureCelsius: (minimumTemperature + maximumTemperature) / 2,
        MinimumHeaterTemperatureCelsius: minimumTemperature,
        MaximumHeaterTemperatureCelsius: maximumTemperature,
        AverageMaterialPressureBar: (minimumPressure + maximumPressure) / 2,
        MinimumMaterialPressureBar: minimumPressure,
        MaximumMaterialPressureBar: maximumPressure,
        AverageCompactionForceNewtons: (minimumForce + maximumForce) / 2,
        MinimumCompactionForceNewtons: minimumForce,
        MaximumCompactionForceNewtons: maximumForce,
        AverageFeedRateMillimetersPerSecond: averageFeedRate,
        AverageProcessHealthPercentage: 90,
        MinimumProcessHealthPercentage: 80,
        EndOfBucketCycleProgressPercentage: 50);
}

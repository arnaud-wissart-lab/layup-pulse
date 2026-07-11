using LayupPulse.Application;
using LayupPulse.Domain;
using Xunit;

namespace LayupPulse.Tests;

public sealed class TelemetryPipelineTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 7, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RollingHistoryNeverExceedsItsConfiguredCapacity()
    {
        MutableTimeProvider time = new(Timestamp);
        TelemetryPipeline pipeline = CreatePipeline(time, historyCapacity: 5);

        for (int sequence = 1; sequence <= 100; sequence++)
        {
            pipeline.Accept(CreateSample(sequence));
            time.Advance(TimeSpan.FromMilliseconds(50));
        }

        IReadOnlyList<TelemetrySample> history = pipeline.GetHistorySnapshot();
        Assert.Equal(5, history.Count);
        Assert.Equal(96, history[0].SequenceNumber);
        Assert.Equal(100, history[^1].SequenceNumber);
        Assert.Equal(5, pipeline.GetCurrentPublication().Metrics.HistoryCount);
    }

    [Fact]
    public void UiPublicationRateIsDecoupledFromAcquisition()
    {
        MutableTimeProvider time = new(Timestamp);
        TelemetryPipeline pipeline = CreatePipeline(time, historyCapacity: 100);
        int publications = 0;
        pipeline.PublicationReady += (_, _) => publications++;

        for (int sequence = 1; sequence <= 20; sequence++)
        {
            pipeline.Accept(CreateSample(sequence));
            time.Advance(TimeSpan.FromMilliseconds(50));
        }

        TelemetryPipelineMetrics metrics = pipeline.GetCurrentPublication().Metrics;
        Assert.Equal(20, metrics.ReceivedSamples);
        Assert.Equal(10, publications);
        Assert.Equal(10, metrics.CoalescedSamples);
        Assert.InRange(metrics.AcquisitionRateHertz, 19.9, 20.1);
        Assert.InRange(metrics.UiPublicationRateHertz, 9.9, 10.1);
    }

    [Fact]
    public void OneSecondAggregateContainsAllSamplesFromTheCompletedWindow()
    {
        MutableTimeProvider time = new(Timestamp);
        TelemetryPipeline pipeline = CreatePipeline(time, historyCapacity: 100);
        Guid productionRunId = Guid.NewGuid();
        pipeline.BeginProductionRun(productionRunId);

        pipeline.Accept(CreateSample(1, force: 400, timestamp: Timestamp.AddMilliseconds(100)));
        time.Advance(TimeSpan.FromMilliseconds(500));
        pipeline.Accept(CreateSample(2, force: 500, timestamp: Timestamp.AddMilliseconds(900)));
        time.Advance(TimeSpan.FromMilliseconds(500));
        pipeline.Accept(CreateSample(3, force: 450, timestamp: Timestamp.AddMilliseconds(1_100)));

        TelemetryAggregate aggregate = Assert.Single(pipeline.GetAggregateSnapshot());
        Assert.Equal(productionRunId, aggregate.ProductionRunId);
        Assert.Equal(Timestamp, aggregate.WindowStartedAt);
        Assert.Equal(Timestamp.AddSeconds(1), aggregate.WindowEndedAt);
        Assert.Equal(2, aggregate.SampleCount);
        Assert.Equal(1, aggregate.FirstSequenceNumber);
        Assert.Equal(2, aggregate.LastSequenceNumber);
        Assert.Equal(450, aggregate.AverageCompactionForceNewtons);
        Assert.Equal(1, pipeline.GetCurrentPublication().Metrics.AggregateCount);
    }

    [Fact]
    public void SequenceGapsAreCountedAsDroppedSamples()
    {
        MutableTimeProvider time = new(Timestamp);
        TelemetryPipeline pipeline = CreatePipeline(time, historyCapacity: 100);

        pipeline.Accept(CreateSample(1));
        time.Advance(TimeSpan.FromMilliseconds(50));
        pipeline.Accept(CreateSample(5));

        Assert.Equal(3, pipeline.GetCurrentPublication().Metrics.DroppedSamples);
    }

    [Fact]
    public void DelayedAlarmKeepsTheFaultedProductionRunAssociation()
    {
        MutableTimeProvider time = new(Timestamp);
        TelemetryPipeline pipeline = CreatePipeline(time, historyCapacity: 100);
        Guid productionRunId = Guid.NewGuid();
        pipeline.BeginProductionRun(productionRunId);
        pipeline.Accept(CreateHighTemperatureSample(1, MachineState.Running));

        pipeline.EndProductionRun(retainAlarmAssociation: true);
        time.Advance(TimeSpan.FromSeconds(1));
        pipeline.Accept(CreateHighTemperatureSample(2, MachineState.Faulted));

        AlarmEvent alarm = Assert.Single(pipeline.GetCurrentPublication().ActiveAlarms);
        Assert.Equal(AlarmCode.HighTemperature, alarm.Code);
        Assert.Equal(productionRunId, alarm.ProductionRunId);
    }

    [Fact]
    public void NewSequenceScopeAcceptsANumberLowerThanThePreviousSession()
    {
        MutableTimeProvider time = new(Timestamp);
        TelemetryPipeline pipeline = CreatePipeline(time, historyCapacity: 100);
        pipeline.Accept(CreateSample(100));

        pipeline.BeginSequenceScope();
        time.Advance(TimeSpan.FromMilliseconds(50));
        pipeline.Accept(CreateSample(1));

        IReadOnlyList<TelemetrySample> history = pipeline.GetHistorySnapshot();
        Assert.Equal([100L, 1L], history.Select(static sample => sample.SequenceNumber));
        Assert.Equal(1, pipeline.GetCurrentPublication().LatestTelemetry?.SequenceNumber);
        Assert.Equal(0, pipeline.GetCurrentPublication().Metrics.DroppedSamples);
    }

    private static TelemetryPipeline CreatePipeline(MutableTimeProvider time, int historyCapacity) => new(
        time,
        new TelemetryPipelineOptions
        {
            UiPublicationInterval = TimeSpan.FromMilliseconds(100),
            AggregateInterval = TimeSpan.FromSeconds(1),
            HistoryDuration = TimeSpan.FromSeconds(60),
            HistoryCapacity = historyCapacity,
            AggregateCapacity = 60,
            RateWindow = TimeSpan.FromSeconds(5),
        },
        new AlarmEngine(time, new AlarmEngineOptions()));

    private static TelemetrySample CreateSample(
        long sequence,
        double force = 450,
        DateTimeOffset? timestamp = null) => new(
        timestamp ?? Timestamp.AddMilliseconds(sequence * 50),
        sequence,
        MachineState.Running,
        100,
        75,
        25,
        120,
        118,
        force,
        145,
        6,
        25,
        98);

    private static TelemetrySample CreateHighTemperatureSample(long sequence, MachineState state) => new(
        Timestamp.AddMilliseconds(sequence * 50),
        sequence,
        state,
        100,
        75,
        25,
        120,
        118,
        450,
        170,
        6,
        25,
        70,
        state == MachineState.Faulted ? [FaultType.HighTemperature] : []);
}

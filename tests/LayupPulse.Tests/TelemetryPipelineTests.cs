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

        pipeline.Accept(CreateSample(1, force: 400));
        time.Advance(TimeSpan.FromMilliseconds(500));
        pipeline.Accept(CreateSample(2, force: 500));
        time.Advance(TimeSpan.FromMilliseconds(500));
        pipeline.Accept(CreateSample(3, force: 450));

        TelemetryAggregate aggregate = Assert.Single(pipeline.GetAggregateSnapshot());
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

    private static TelemetrySample CreateSample(long sequence, double force = 450) => new(
        Timestamp.AddMilliseconds(sequence * 50),
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
}

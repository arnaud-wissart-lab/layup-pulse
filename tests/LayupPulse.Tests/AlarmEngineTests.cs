using LayupPulse.Domain;
using Xunit;

namespace LayupPulse.Tests;

public sealed class AlarmEngineTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 7, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void HighTemperatureRequiresTheConfiguredDebounce()
    {
        MutableTimeProvider time = new(Timestamp);
        AlarmEngine engine = CreateEngine(time);

        engine.EvaluateTelemetry(CreateSample(1, MachineState.Running, temperature: 170));
        time.Advance(TimeSpan.FromMilliseconds(999));
        engine.EvaluateTelemetry(CreateSample(2, MachineState.Running, temperature: 170));

        Assert.Empty(engine.ActiveAlarms);

        time.Advance(TimeSpan.FromMilliseconds(1));
        engine.EvaluateTelemetry(CreateSample(3, MachineState.Running, temperature: 170));

        Assert.Equal(AlarmCode.HighTemperature, Assert.Single(engine.ActiveAlarms).Code);
    }

    [Fact]
    public void HighTemperatureUsesTheLowerHysteresisThresholdToClear()
    {
        MutableTimeProvider time = new(Timestamp);
        AlarmEngine engine = CreateEngine(time);
        RaiseHighTemperature(engine, time);

        engine.EvaluateTelemetry(CreateSample(3, MachineState.Faulted, temperature: 160));

        Assert.Single(engine.ActiveAlarms);

        engine.EvaluateTelemetry(CreateSample(4, MachineState.Faulted, temperature: 154.9));

        Assert.Empty(engine.ActiveAlarms);
        AlarmEvent cleared = Assert.Single(engine.History);
        Assert.Equal(AlarmLifecycleState.Cleared, cleared.LifecycleState);
        Assert.NotNull(cleared.ClearedAt);
    }

    [Fact]
    public void LowPressureIsArmedOnlyWhileRunning()
    {
        MutableTimeProvider time = new(Timestamp);
        AlarmEngine engine = CreateEngine(time);

        engine.EvaluateTelemetry(CreateSample(1, MachineState.Ready, pressure: 3));
        time.Advance(TimeSpan.FromSeconds(2));
        engine.EvaluateTelemetry(CreateSample(2, MachineState.Ready, pressure: 3));

        Assert.DoesNotContain(engine.ActiveAlarms, alarm => alarm.Code == AlarmCode.LowMaterialPressure);

        engine.EvaluateTelemetry(CreateSample(3, MachineState.Running, pressure: 3));
        time.Advance(TimeSpan.FromMilliseconds(750));
        engine.EvaluateTelemetry(CreateSample(4, MachineState.Running, pressure: 3));

        Assert.Contains(engine.ActiveAlarms, alarm => alarm.Code == AlarmCode.LowMaterialPressure);
    }

    [Fact]
    public void ForceInstabilityUsesARollingMultiSampleVariation()
    {
        MutableTimeProvider time = new(Timestamp);
        AlarmEngine engine = CreateEngine(time);
        double[] values = [400, 550, 405, 545, 410, 540];

        for (int index = 0; index < values.Length; index++)
        {
            engine.EvaluateTelemetry(CreateSample(index + 1, MachineState.Running, force: values[index]));
            time.Advance(TimeSpan.FromMilliseconds(100));
        }

        Assert.Contains(engine.ActiveAlarms, alarm => alarm.Code == AlarmCode.UnstableCompactionForce);
    }

    [Fact]
    public void CommunicationTimeoutRaisesAtTheConfiguredDeadlineAndClearsAfterStableSamples()
    {
        MutableTimeProvider time = new(Timestamp);
        AlarmEngine engine = CreateEngine(time);

        time.Advance(TimeSpan.FromMilliseconds(1_999));
        engine.EvaluateCommunication(communicationExpected: true, Timestamp);
        Assert.Empty(engine.ActiveAlarms);

        time.Advance(TimeSpan.FromMilliseconds(1));
        engine.EvaluateCommunication(communicationExpected: true, Timestamp);
        Assert.Contains(engine.ActiveAlarms, alarm => alarm.Code == AlarmCode.CommunicationTimeout);

        engine.EvaluateTelemetry(CreateSample(1, MachineState.Ready));
        engine.EvaluateTelemetry(CreateSample(2, MachineState.Ready));
        Assert.Contains(engine.ActiveAlarms, alarm => alarm.Code == AlarmCode.CommunicationTimeout);

        engine.EvaluateTelemetry(CreateSample(3, MachineState.Ready));
        Assert.DoesNotContain(engine.ActiveAlarms, alarm => alarm.Code == AlarmCode.CommunicationTimeout);
        Assert.Contains(engine.History, alarm => alarm.Code == AlarmCode.CommunicationTimeout);
    }

    [Fact]
    public void AcknowledgementDoesNotClearThePhysicalCondition()
    {
        MutableTimeProvider time = new(Timestamp);
        AlarmEngine engine = CreateEngine(time);
        RaiseHighTemperature(engine, time);
        AlarmEvent raised = Assert.Single(engine.ActiveAlarms);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(engine.Acknowledge(raised.Id));

        AlarmEvent acknowledged = Assert.Single(engine.ActiveAlarms);
        Assert.Equal(AlarmLifecycleState.Acknowledged, acknowledged.LifecycleState);
        Assert.NotNull(acknowledged.AcknowledgedAt);
        Assert.Null(acknowledged.ClearedAt);
        Assert.Empty(engine.History);
    }

    [Fact]
    public void ConditionClearancePreservesAcknowledgementAndTimestampsInHistory()
    {
        MutableTimeProvider time = new(Timestamp);
        AlarmEngine engine = CreateEngine(time);
        RaiseHighTemperature(engine, time);
        AlarmEvent raised = Assert.Single(engine.ActiveAlarms);
        engine.Acknowledge(raised.Id);

        time.Advance(TimeSpan.FromSeconds(1));
        engine.EvaluateTelemetry(CreateSample(3, MachineState.Faulted, temperature: 150));

        AlarmEvent cleared = Assert.Single(engine.History);
        Assert.Equal(AlarmLifecycleState.Cleared, cleared.LifecycleState);
        Assert.NotNull(cleared.AcknowledgedAt);
        Assert.NotNull(cleared.ClearedAt);
        Assert.True(cleared.ClearedAt >= cleared.AcknowledgedAt);
    }

    [Fact]
    public void RepeatedConditionCreatesOnlyOneActiveAlarmPerCodeAndSource()
    {
        MutableTimeProvider time = new(Timestamp);
        AlarmEngine engine = CreateEngine(time);
        RaiseHighTemperature(engine, time);

        for (int index = 0; index < 20; index++)
        {
            time.Advance(TimeSpan.FromMilliseconds(100));
            engine.EvaluateTelemetry(CreateSample(index + 10, MachineState.Faulted, temperature: 180));
        }

        Assert.Single(engine.ActiveAlarms, alarm => alarm.Code == AlarmCode.HighTemperature);
    }

    private static AlarmEngine CreateEngine(MutableTimeProvider time) => new(
        time,
        new AlarmEngineOptions
        {
            HighTemperatureDebounce = TimeSpan.FromSeconds(1),
            LowPressureDebounce = TimeSpan.FromMilliseconds(750),
            CommunicationTimeout = TimeSpan.FromSeconds(2),
            CommunicationRecoverySampleCount = 3,
        });

    private static void RaiseHighTemperature(AlarmEngine engine, MutableTimeProvider time)
    {
        engine.EvaluateTelemetry(CreateSample(1, MachineState.Running, temperature: 170));
        time.Advance(TimeSpan.FromSeconds(1));
        engine.EvaluateTelemetry(CreateSample(2, MachineState.Faulted, temperature: 170));
    }

    private static TelemetrySample CreateSample(
        long sequence,
        MachineState state,
        double temperature = 145,
        double pressure = 6,
        double force = 450,
        IEnumerable<FaultType>? faults = null) => new(
            Timestamp.AddMilliseconds(sequence * 50),
            sequence,
            state,
            100,
            75,
            25,
            120,
            118,
            force,
            temperature,
            pressure,
            25,
            98,
            faults);
}

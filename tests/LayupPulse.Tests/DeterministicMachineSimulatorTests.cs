using LayupPulse.Domain;
using LayupPulse.Simulator;
using Xunit;

namespace LayupPulse.Tests;

public sealed class DeterministicMachineSimulatorTests
{
    private const int TelemetryRateHz = 20;
    private static readonly DateTimeOffset InitialTimestamp =
        new(2026, 7, 10, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid CorrelationId = new("809d11f9-5f52-48f7-aa32-7b5d76879ad5");

    [Fact]
    public void FixedSeedProducesIdenticalTelemetry()
    {
        // Préparation
        DeterministicMachineSimulator first = CreateRunningSimulator(1729);
        DeterministicMachineSimulator second = CreateRunningSimulator(1729);

        // Action
        TelemetrySample[] firstSamples = Advance(first, 25);
        TelemetrySample[] secondSamples = Advance(second, 25);

        // Vérification
        Assert.Equal(firstSamples, secondSamples);
    }

    [Fact]
    public void SequenceNumbersIncreaseStrictly()
    {
        // Préparation
        DeterministicMachineSimulator simulator = CreateRunningSimulator();

        // Action
        long[] sequenceNumbers = Advance(simulator, 10)
            .Select(static sample => sample.SequenceNumber)
            .ToArray();

        // Vérification
        Assert.Equal(Enumerable.Range(1, 10).Select(static value => (long)value), sequenceNumbers);
    }

    [Fact]
    public void ProgressDoesNotAdvanceWhilePaused()
    {
        // Préparation
        DeterministicMachineSimulator simulator = CreateRunningSimulator();
        Advance(simulator, 20);
        Execute(simulator, MachineCommandType.Pause, 3);
        double progressBeforePause = simulator.GetSnapshot().Telemetry.CycleProgressPercentage;

        // Action
        TelemetrySample[] pausedSamples = Advance(simulator, 100, 4);

        // Vérification
        Assert.All(pausedSamples, sample =>
        {
            Assert.Equal(MachineState.Paused, sample.MachineState);
            Assert.Equal(progressBeforePause, sample.CycleProgressPercentage);
        });
    }

    [Fact]
    public void ProgressAdvancesOnlyWhileRunning()
    {
        // Préparation
        DeterministicMachineSimulator simulator = CreateRunningSimulator();

        // Action
        TelemetrySample first = simulator.Advance(InitialTimestamp.AddSeconds(3)).Telemetry;
        TelemetrySample second = simulator.Advance(InitialTimestamp.AddSeconds(3.05)).Telemetry;

        // Vérification
        Assert.Equal(MachineState.Running, second.MachineState);
        Assert.True(first.CycleProgressPercentage > 0);
        Assert.True(second.CycleProgressPercentage > first.CycleProgressPercentage);
    }

    [Fact]
    public void CycleCompletesAtExactlyOneHundredPercent()
    {
        // Préparation
        DeterministicMachineSimulator simulator = CreateRunningSimulator();
        int maximumTicks = (int)(BuiltInRecipes.WingPanelDemo.EstimatedDuration.TotalSeconds * TelemetryRateHz) + 1;
        SimulationSnapshot snapshot = simulator.GetSnapshot();

        // Action
        for (int tick = 1; tick <= maximumTicks && snapshot.Machine.State != MachineState.Completed; tick++)
        {
            snapshot = simulator.Advance(InitialTimestamp.AddSeconds(3 + ((double)tick / TelemetryRateHz)));
        }

        // Vérification
        Assert.Equal(MachineState.Completed, snapshot.Machine.State);
        Assert.Equal(100, snapshot.Telemetry.CycleProgressPercentage);
        Assert.Equal(100, Assert.IsType<ProductionRun>(snapshot.Machine.CurrentRun).CompletionPercentage);
    }

    [Fact]
    public void OverTemperatureFaultIsDeterministicAndFaultsMachine()
    {
        // Préparation
        DeterministicMachineSimulator simulator = CreateRunningSimulator();

        // Action
        CommandResult result = Execute(
            simulator,
            MachineCommandType.CriticalFaultRaised,
            4,
            fault: FaultType.HighTemperature);
        SimulationSnapshot snapshot = simulator.Advance(InitialTimestamp.AddSeconds(4.05));

        // Vérification
        Assert.True(result.IsAccepted);
        Assert.Equal(MachineState.Faulted, snapshot.Machine.State);
        Assert.Equal(FaultType.HighTemperature, snapshot.ActiveFault);
        Assert.Equal(
            BuiltInRecipes.WingPanelDemo.TargetTemperatureCelsius + 35,
            snapshot.Telemetry.HeaterTemperatureCelsius);
    }

    [Fact]
    public void LowPressureFaultIsDeterministicAndFaultsMachine()
    {
        // Préparation
        DeterministicMachineSimulator simulator = CreateRunningSimulator();

        // Action
        Execute(
            simulator,
            MachineCommandType.CriticalFaultRaised,
            4,
            fault: FaultType.LowMaterialPressure);
        SimulationSnapshot snapshot = simulator.Advance(InitialTimestamp.AddSeconds(4.05));

        // Vérification
        Assert.Equal(MachineState.Faulted, snapshot.Machine.State);
        Assert.Equal(FaultType.LowMaterialPressure, snapshot.ActiveFault);
        Assert.Equal(2.2, snapshot.Telemetry.MaterialPressureBar);
    }

    [Fact]
    public void ClearedFaultCanBeResetToReady()
    {
        // Préparation
        DeterministicMachineSimulator simulator = CreateRunningSimulator();
        Execute(
            simulator,
            MachineCommandType.CriticalFaultRaised,
            4,
            fault: FaultType.HighTemperature);

        // Action
        CommandResult clearance = Execute(
            simulator,
            MachineCommandType.FaultCleared,
            5,
            fault: FaultType.HighTemperature);
        CommandResult reset = Execute(simulator, MachineCommandType.Reset, 6);
        SimulationSnapshot snapshot = simulator.GetSnapshot();

        // Vérification
        Assert.True(clearance.IsAccepted);
        Assert.True(reset.IsAccepted);
        Assert.Null(snapshot.ActiveFault);
        Assert.Equal(MachineState.Ready, snapshot.Machine.State);
    }

    [Fact]
    public void CommunicationDropInterruptsTelemetryAndCanRecover()
    {
        // Préparation
        DeterministicMachineSimulator simulator = CreateRunningSimulator();

        // Action
        CommandResult injected = Execute(
            simulator,
            MachineCommandType.CriticalFaultRaised,
            4,
            fault: FaultType.CommunicationTimeout);
        InvalidOperationException interruption = Assert.Throws<InvalidOperationException>(
            () => simulator.Advance(InitialTimestamp.AddSeconds(4.05)));
        CommandResult cleared = Execute(
            simulator,
            MachineCommandType.FaultCleared,
            5,
            fault: FaultType.CommunicationTimeout);
        CommandResult reset = Execute(simulator, MachineCommandType.Reset, 6);
        SimulationSnapshot recovered = simulator.Advance(InitialTimestamp.AddSeconds(6.05));

        // Vérification
        Assert.True(injected.IsAccepted);
        Assert.False(string.IsNullOrWhiteSpace(interruption.Message));
        Assert.True(cleared.IsAccepted);
        Assert.True(reset.IsAccepted);
        Assert.False(simulator.IsCommunicationDropped);
        Assert.Equal(MachineState.Ready, recovered.Machine.State);
    }

    [Fact]
    public void InvalidCommandIsRejectedWithoutChangingState()
    {
        // Préparation
        DeterministicMachineSimulator simulator = CreateConnectedSimulator();

        // Action
        CommandResult result = Execute(simulator, MachineCommandType.Pause, 2);

        // Vérification
        Assert.False(result.IsAccepted);
        Assert.Equal(MachineState.Ready, result.Transition.Snapshot.State);
        Assert.Equal(
            StateTransitionRejectionCode.InvalidState,
            Assert.IsType<StateTransitionRejection>(result.Transition.Rejection).Code);
    }

    [Fact]
    public void StopAbortsCycleAndReturnsMachineToReady()
    {
        // Préparation
        DeterministicMachineSimulator simulator = CreateRunningSimulator();
        Advance(simulator, 50);

        // Action
        CommandResult result = Execute(simulator, MachineCommandType.Stop, 5);
        SimulationSnapshot snapshot = simulator.GetSnapshot();

        // Vérification
        Assert.True(result.IsAccepted);
        Assert.Equal(MachineState.Ready, snapshot.Machine.State);
        Assert.Equal(0, snapshot.Telemetry.CycleProgressPercentage);
        Assert.Equal(
            ProductionRunStatus.Aborted,
            Assert.IsType<ProductionRun>(snapshot.Machine.CurrentRun).Status);
    }

    [Fact]
    public void FeedRateRampsAndNominalSignalsRemainBounded()
    {
        // Préparation
        DeterministicMachineSimulator simulator = CreateRunningSimulator();

        // Action
        TelemetrySample first = simulator.Advance(InitialTimestamp.AddSeconds(3)).Telemetry;
        TelemetrySample second = simulator.Advance(InitialTimestamp.AddSeconds(3.05)).Telemetry;

        // Vérification
        Assert.Equal(4, first.ActualFeedRateMillimetersPerSecond);
        Assert.Equal(8, second.ActualFeedRateMillimetersPerSecond);
        Assert.InRange(second.CompactionForceNewtons, 445, 455);
        Assert.InRange(second.MaterialPressureBar, 5.96, 6.04);
        Assert.True(second.HeaterTemperatureCelsius > AmbientTemperatureCelsius);
        Assert.True(second.HeaterTemperatureCelsius < BuiltInRecipes.WingPanelDemo.TargetTemperatureCelsius);
    }

    private const double AmbientTemperatureCelsius = 22;

    private static DeterministicMachineSimulator CreateConnectedSimulator(int seed = 1729)
    {
        DeterministicMachineSimulator simulator = new(seed, TelemetryRateHz, InitialTimestamp);
        CommandResult connected = Execute(simulator, MachineCommandType.ConnectRequested, 1);
        Assert.True(connected.IsAccepted);
        return simulator;
    }

    private static DeterministicMachineSimulator CreateRunningSimulator(int seed = 1729)
    {
        DeterministicMachineSimulator simulator = CreateConnectedSimulator(seed);
        CommandResult loaded = Execute(
            simulator,
            MachineCommandType.LoadRecipe,
            2,
            BuiltInRecipes.WingPanelDemo);
        CommandResult started = Execute(simulator, MachineCommandType.Start, 3);
        Assert.True(loaded.IsAccepted);
        Assert.True(started.IsAccepted);
        return simulator;
    }

    private static TelemetrySample[] Advance(
        DeterministicMachineSimulator simulator,
        int count,
        int startSecond = 3)
    {
        TelemetrySample[] samples = new TelemetrySample[count];
        for (int index = 0; index < count; index++)
        {
            samples[index] = simulator.Advance(
                InitialTimestamp.AddSeconds(startSecond + ((double)(index + 1) / TelemetryRateHz))).Telemetry;
        }

        return samples;
    }

    private static CommandResult Execute(
        DeterministicMachineSimulator simulator,
        MachineCommandType commandType,
        int second,
        ProductionRecipe? recipe = null,
        FaultType? fault = null) =>
        simulator.ExecuteCommand(new MachineCommand(
            CorrelationId,
            commandType,
            InitialTimestamp.AddSeconds(second),
            recipe,
            fault: fault));
}

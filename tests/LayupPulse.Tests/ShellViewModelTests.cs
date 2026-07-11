using LayupPulse.Application;
using LayupPulse.Desktop;
using LayupPulse.Domain;
using Xunit;

namespace LayupPulse.Tests;

public sealed class ShellViewModelTests
{
    private static readonly DateTimeOffset Timestamp =
        new(2026, 7, 11, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StatePublicationsAreCoalescedBeforeTheyReachTheUiDispatcher()
    {
        TestSessionService sessionService = new(CreateState(MachineState.Ready));
        QueuedDispatcher dispatcher = new();
        TimeProvider timeProvider = TimeProvider.System;
        OverviewViewModel overview = new(sessionService, timeProvider);
        DiagnosticsViewModel diagnostics = new(
            new Uri("http://127.0.0.1:5057"),
            timeProvider,
            sessionService,
            new DemoModeOptions());
        AlarmsViewModel alarms = new(sessionService);
        using ShellViewModel shell = new(
            sessionService,
            dispatcher,
            overview,
            diagnostics,
            alarms);

        for (int index = 0; index < 100; index++)
        {
            MachineState state = index == 99 ? MachineState.Completed : MachineState.Running;
            sessionService.Publish(CreateState(state));
        }

        Assert.Equal(1, dispatcher.PendingCount);
        dispatcher.ExecuteNext();
        Assert.Equal("Terminée", shell.MachineState);
        Assert.Equal(0, dispatcher.PendingCount);
    }

    private static MachineSessionState CreateState(MachineState machineState) => new(
        MachineConnectionStatus.Connected,
        new MachineSnapshot(machineState, Timestamp, BuiltInRecipes.WingPanelDemo),
        null,
        Guid.NewGuid(),
        Timestamp,
        Timestamp,
        0,
        null,
        null,
        Array.Empty<MachineDiagnosticMessage>(),
        TelemetryPipelineMetrics.Empty(3_000),
        null,
        Array.Empty<AlarmEvent>(),
        Array.Empty<AlarmEvent>());

    private sealed class QueuedDispatcher : IUiDispatcher
    {
        private readonly Queue<Action> _actions = [];

        public int PendingCount => _actions.Count;

        public void Post(Action action) => _actions.Enqueue(action);

        public void ExecuteNext() => _actions.Dequeue()();
    }

    private sealed class TestSessionService : IMachineSessionService
    {
        public TestSessionService(MachineSessionState state)
        {
            State = state;
        }

        public event EventHandler<MachineSessionStateChangedEventArgs>? StateChanged;

        public MachineSessionState State { get; private set; }

        public void Publish(MachineSessionState state)
        {
            State = state;
            StateChanged?.Invoke(this, new MachineSessionStateChangedEventArgs(state));
        }

        public IReadOnlyList<TelemetrySample> GetTelemetryHistorySnapshot() =>
            Array.Empty<TelemetrySample>();

        public Task<MachineSessionOperationResult> ConnectAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MachineSessionOperationResult> DisconnectAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MachineCommandExecutionResult> ExecuteCommandAsync(
            MachineCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<MachineSessionOperationResult> SetDemoFaultAsync(
            FaultType fault,
            bool active,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public bool AcknowledgeAlarm(Guid alarmId) => false;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

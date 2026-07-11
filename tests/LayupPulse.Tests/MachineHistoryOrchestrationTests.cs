using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using LayupPulse.Application;
using LayupPulse.Domain;
using Xunit;

namespace LayupPulse.Tests;

public sealed class MachineHistoryOrchestrationTests
{
    private static readonly DateTimeOffset Timestamp =
        new(2026, 7, 11, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ProcessFaultFinalizesRunAfterFirstTerminalTelemetry()
    {
        MutableTimeProvider timeProvider = new(Timestamp);
        ControlledMachineGateway gateway = new(timeProvider);
        RecordingHistoryWriter history = new();
        await using MachineSessionService service = CreateService(gateway, timeProvider, history);

        await StartRunAsync(service, gateway, timeProvider);
        Guid runId = history.GetSingleRun().Id;

        MachineSessionOperationResult injected = await service.SetDemoFaultAsync(
            FaultType.HighTemperature,
            active: true,
            CancellationToken.None);

        Assert.True(injected.IsSuccessful);
        Assert.Equal(ProductionRunStatus.Running, history.GetRun(runId).Status);

        await gateway.PublishAsync(CreateTelemetry(
            sequence: 2,
            timeProvider.GetUtcNow(),
            MachineState.Faulted,
            temperature: 180,
            progress: 32,
            health: 24,
            faults: [FaultType.HighTemperature]));
        await WaitUntilAsync(
            () => history.GetRun(runId).Status == ProductionRunStatus.Faulted
                && history.Aggregates.Any(aggregate => aggregate.ProductionRunId == runId),
            TimeSpan.FromSeconds(2));

        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await gateway.PublishAsync(CreateTelemetry(
            sequence: 3,
            timeProvider.GetUtcNow(),
            MachineState.Faulted,
            temperature: 181,
            progress: 32,
            health: 20,
            faults: [FaultType.HighTemperature]));
        await WaitUntilAsync(
            () => history.Alarms.Any(alarm => alarm.Code == AlarmCode.HighTemperature),
            TimeSpan.FromSeconds(2));

        ProductionRun faulted = history.GetRun(runId);
        AlarmEvent alarm = Assert.Single(
            history.Alarms,
            alarm => alarm.Code == AlarmCode.HighTemperature);
        TelemetryAggregate terminalAggregate = Assert.Single(
            history.Aggregates,
            aggregate => aggregate.ProductionRunId == runId);

        Assert.Equal(ProductionRunStatus.Faulted, faulted.Status);
        Assert.Equal(MachineState.Faulted, faulted.TerminalMachineState);
        Assert.Equal(runId, alarm.ProductionRunId);
        Assert.Equal(1, faulted.AlarmCount);
        Assert.True(faulted.MinimumProcessHealthPercentage < 100);
        Assert.Equal(162.5, faulted.AverageTemperatureCelsius);
        Assert.Equal(180, terminalAggregate.MaximumHeaterTemperatureCelsius);
        Assert.Equal(24, terminalAggregate.MinimumProcessHealthPercentage);
        Assert.Equal(32, terminalAggregate.EndOfBucketCycleProgressPercentage);
    }

    [Fact]
    public async Task CommunicationDropFinalizesRunWithoutTerminalTelemetry()
    {
        MutableTimeProvider timeProvider = new(Timestamp);
        ControlledMachineGateway gateway = new(timeProvider);
        RecordingHistoryWriter history = new();
        await using MachineSessionService service = CreateService(gateway, timeProvider, history);

        await StartRunAsync(service, gateway, timeProvider);
        Guid runId = history.GetSingleRun().Id;

        MachineSessionOperationResult injected = await service.SetDemoFaultAsync(
            FaultType.CommunicationTimeout,
            active: true,
            CancellationToken.None);

        Assert.True(injected.IsSuccessful);
        ProductionRun faulted = history.GetRun(runId);
        Assert.Equal(ProductionRunStatus.Faulted, faulted.Status);
        Assert.Equal(MachineState.Faulted, faulted.TerminalMachineState);
        Assert.Equal("Coupure de communication simulée.", faulted.EndReason);
    }

    [Theory]
    [InlineData(MachineState.Ready)]
    [InlineData(MachineState.Disconnected)]
    public async Task FreshIdleSimulatorAbortsOldRunAndAllowsDistinctNextRun(MachineState replacementState)
    {
        ControlledMachineGateway gateway = new(TimeProvider.System);
        RecordingHistoryWriter history = new();
        await using MachineSessionService service = CreateService(gateway, TimeProvider.System, history);

        await StartRunAsync(service, gateway, TimeProvider.System);
        ProductionRun oldRun = history.GetSingleRun();
        await gateway.PublishAsync(CreateTelemetry(
            sequence: 2,
            DateTimeOffset.UtcNow,
            MachineState.Running,
            temperature: 150,
            progress: 20,
            health: 95));
        await WaitUntilAsync(
            () => service.State.ReceivedSampleCount >= 2,
            TimeSpan.FromSeconds(2));

        gateway.ReplaceSimulator(replacementState);
        await WaitUntilAsync(
            () => gateway.AttachCount >= 2,
            TimeSpan.FromSeconds(2));
        await gateway.WaitForCurrentStreamAsync();
        await gateway.PublishAsync(CreateTelemetry(
            sequence: 1,
            DateTimeOffset.UtcNow,
            MachineState.Ready,
            temperature: 42,
            progress: 0,
            health: 100));
        await WaitUntilAsync(
            () => service.State.LatestTelemetry?.MachineState == MachineState.Ready,
            TimeSpan.FromSeconds(2));

        ProductionRun aborted = history.GetRun(oldRun.Id);
        Assert.Equal(ProductionRunStatus.Aborted, aborted.Status);
        Assert.Equal(20, aborted.CompletionPercentage);
        Assert.DoesNotContain(
            history.Aggregates,
            aggregate => aggregate.ProductionRunId == oldRun.Id
                && aggregate.AverageHeaterTemperatureCelsius == 42);

        Guid nextRunId = Guid.NewGuid();
        await LoadAndStartAsync(service, TimeProvider.System.GetUtcNow(), nextRunId);

        Assert.NotEqual(oldRun.Id, nextRunId);
        Assert.Equal(ProductionRunStatus.Running, history.GetRun(nextRunId).Status);
        Assert.Equal(2, history.Runs.Length);
    }

    [Theory]
    [InlineData(MachineState.Running)]
    [InlineData(MachineState.Paused)]
    public async Task ActiveReplacementSnapshotKeepsExistingLocalRun(MachineState replacementState)
    {
        ControlledMachineGateway gateway = new(TimeProvider.System);
        RecordingHistoryWriter history = new();
        await using MachineSessionService service = CreateService(gateway, TimeProvider.System, history);

        await StartRunAsync(service, gateway, TimeProvider.System);
        Guid runId = history.GetSingleRun().Id;

        gateway.ReplaceSimulator(replacementState);
        await WaitUntilAsync(
            () => gateway.AttachCount >= 2,
            TimeSpan.FromSeconds(2));
        await gateway.WaitForCurrentStreamAsync();
        await gateway.PublishAsync(CreateTelemetry(
            sequence: 1,
            DateTimeOffset.UtcNow,
            replacementState,
            temperature: 150,
            progress: 30,
            health: 95));
        await WaitUntilAsync(
            () => service.State.LatestTelemetry?.MachineState == replacementState,
            TimeSpan.FromSeconds(2));

        Assert.Equal(ProductionRunStatus.Running, history.GetRun(runId).Status);
        Assert.Single(history.Runs);
    }

    private static MachineSessionService CreateService(
        ControlledMachineGateway gateway,
        TimeProvider timeProvider,
        RecordingHistoryWriter history) =>
        new(gateway, timeProvider, CreateOptions(), gateway, history);

    private static MachineSessionOptions CreateOptions() => new()
    {
        StaleAfter = TimeSpan.FromSeconds(5),
        StaleCheckInterval = TimeSpan.FromMilliseconds(50),
        ReconnectInitialDelay = TimeSpan.FromMilliseconds(1),
        ReconnectMaximumDelay = TimeSpan.FromMilliseconds(1),
        DiagnosticCapacity = 20,
        Telemetry = new TelemetryPipelineOptions
        {
            UiPublicationInterval = TimeSpan.FromTicks(1),
        },
        Alarms = new AlarmEngineOptions
        {
            HighTemperatureDebounce = TimeSpan.FromSeconds(1),
            CommunicationTimeout = TimeSpan.FromSeconds(10),
        },
    };

    private static async Task StartRunAsync(
        MachineSessionService service,
        ControlledMachineGateway gateway,
        TimeProvider timeProvider)
    {
        Assert.True((await service.ConnectAsync(CancellationToken.None)).IsSuccessful);
        await gateway.WaitForCurrentStreamAsync();
        await LoadAndStartAsync(service, timeProvider.GetUtcNow(), Guid.NewGuid());
        await gateway.PublishAsync(CreateTelemetry(
            sequence: 1,
            timeProvider.GetUtcNow(),
            MachineState.Running,
            temperature: 145,
            progress: 10,
            health: 98));
        await WaitUntilAsync(
            () => service.State.LatestTelemetry?.MachineState == MachineState.Running,
            TimeSpan.FromSeconds(2));
    }

    private static async Task LoadAndStartAsync(
        MachineSessionService service,
        DateTimeOffset timestamp,
        Guid runId)
    {
        MachineCommandExecutionResult loaded = await service.ExecuteCommandAsync(
            new MachineCommand(
                Guid.NewGuid(),
                MachineCommandType.LoadRecipe,
                timestamp,
                BuiltInRecipes.WingPanelDemo),
            CancellationToken.None);
        MachineCommandExecutionResult started = await service.ExecuteCommandAsync(
            new MachineCommand(
                Guid.NewGuid(),
                MachineCommandType.Start,
                timestamp,
                productionRunId: runId),
            CancellationToken.None);

        Assert.True(loaded.IsAccepted);
        Assert.True(started.IsAccepted);
    }

    private static TelemetrySample CreateTelemetry(
        long sequence,
        DateTimeOffset timestamp,
        MachineState state,
        double temperature,
        double progress,
        double health,
        IEnumerable<FaultType>? faults = null) =>
        new(
            timestamp,
            sequence,
            state,
            100,
            75,
            25,
            120,
            118,
            450,
            temperature,
            6,
            progress,
            health,
            faults);

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        using CancellationTokenSource cancellation = new(timeout);
        while (!predicate())
        {
            await Task.Delay(10, cancellation.Token);
        }
    }

    private sealed class RecordingHistoryWriter : IHistoryWriter
    {
        private readonly object _gate = new();
        private readonly Dictionary<Guid, ProductionRun> _runs = [];
        private readonly Dictionary<Guid, AlarmEvent> _alarms = [];
        private readonly Dictionary<Guid, TelemetryAggregate> _aggregates = [];

        public event EventHandler<HistoryPersistenceDiagnosticEventArgs>? DiagnosticOccurred
        {
            add { }
            remove { }
        }

        public string? LastDiagnosticMessage => null;

        public ProductionRun[] Runs
        {
            get
            {
                lock (_gate)
                {
                    return _runs.Values.ToArray();
                }
            }
        }

        public IReadOnlyList<AlarmEvent> Alarms
        {
            get
            {
                lock (_gate)
                {
                    return _alarms.Values.ToArray();
                }
            }
        }

        public IReadOnlyList<TelemetryAggregate> Aggregates
        {
            get
            {
                lock (_gate)
                {
                    return _aggregates.Values.ToArray();
                }
            }
        }

        public bool TryRecordProductionRun(ProductionRun run)
        {
            lock (_gate)
            {
                _runs[run.Id] = run;
            }

            return true;
        }

        public bool TryRecordTelemetryAggregate(TelemetryAggregate aggregate)
        {
            if (aggregate.ProductionRunId == Guid.Empty)
            {
                return false;
            }

            lock (_gate)
            {
                _aggregates[aggregate.Id] = aggregate;
            }

            return true;
        }

        public bool TryRecordAlarm(AlarmEvent alarm)
        {
            lock (_gate)
            {
                _alarms[alarm.Id] = alarm;
            }

            return true;
        }

        public ProductionRun GetRun(Guid id)
        {
            lock (_gate)
            {
                return _runs[id];
            }
        }

        public ProductionRun GetSingleRun()
        {
            lock (_gate)
            {
                return Assert.Single(_runs.Values);
            }
        }
    }

    private sealed class ControlledMachineGateway : IMachineGateway, IDemoFaultGateway
    {
        private readonly object _gate = new();
        private readonly TimeProvider _timeProvider;
        private SimulatorContext _current;
        private int _attachCount;

        public ControlledMachineGateway(TimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
            _current = new SimulatorContext(CreateSnapshot(MachineState.Ready, timeProvider.GetUtcNow()));
        }

        public int AttachCount => Volatile.Read(ref _attachCount);

        public Task<MachineTransportAttachment> AttachAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SimulatorContext context;
            lock (_gate)
            {
                context = _current;
            }

            Interlocked.Increment(ref _attachCount);
            TestSession session = new(Guid.NewGuid(), _timeProvider.GetUtcNow(), context);
            return Task.FromResult(new MachineTransportAttachment(session, context.GetSnapshot()));
        }

        public Task DisconnectAsync(IMachineSession session, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public ValueTask AbandonAsync(IMachineSession session) => ValueTask.CompletedTask;

        public Task<CommandResult> ExecuteCommandAsync(
            IMachineSession session,
            MachineCommand command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SimulatorContext context = GetContext(session);
            StateTransitionResult transition = MachineStateMachine.Transition(context.GetSnapshot(), command);
            if (transition.IsAccepted)
            {
                context.SetSnapshot(transition.Snapshot);
            }

            return Task.FromResult(new CommandResult(command.CorrelationId, transition));
        }

        public Task<MachineSnapshot> GetSnapshotAsync(
            IMachineSession session,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SimulatorContext context = GetContext(session);
            if (!context.IsAvailable)
            {
                throw new MachineGatewayException(
                    MachineGatewayFailureKind.Unavailable,
                    "Le contexte du simulateur a été remplacé.");
            }

            return Task.FromResult(context.GetSnapshot());
        }

        public async IAsyncEnumerable<TelemetrySample> StreamTelemetryAsync(
            IMachineSession session,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            SimulatorContext context = GetContext(session);
            context.StreamStarted.TrySetResult();
            await foreach (StreamEvent streamEvent in context.Events.Reader.ReadAllAsync(cancellationToken))
            {
                if (streamEvent.Failure is not null)
                {
                    throw streamEvent.Failure;
                }

                yield return streamEvent.Sample!;
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<CommandResult> InjectFaultAsync(
            IMachineSession session,
            Guid correlationId,
            FaultType fault,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SimulatorContext context = GetContext(session);
            MachineSnapshot before = context.GetSnapshot();
            MachineSnapshot after = before with
            {
                State = MachineState.Faulted,
                Timestamp = _timeProvider.GetUtcNow(),
                ActiveFaults = before.ActiveFaults.Add(fault),
            };
            context.SetSnapshot(after);
            return Task.FromResult(new CommandResult(
                correlationId,
                StateTransitionResult.Accepted(before.State, after)));
        }

        public Task<CommandResult> ClearFaultAsync(
            IMachineSession session,
            Guid correlationId,
            FaultType fault,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SimulatorContext context = GetContext(session);
            MachineSnapshot before = context.GetSnapshot();
            MachineSnapshot after = before with
            {
                ActiveFaults = before.ActiveFaults.Remove(fault),
                Timestamp = _timeProvider.GetUtcNow(),
            };
            context.SetSnapshot(after);
            return Task.FromResult(new CommandResult(
                correlationId,
                StateTransitionResult.Accepted(before.State, after)));
        }

        public async Task PublishAsync(TelemetrySample sample)
        {
            SimulatorContext context;
            lock (_gate)
            {
                context = _current;
            }

            context.SetSnapshot(context.GetSnapshot() with
            {
                State = sample.MachineState,
                Timestamp = sample.Timestamp,
                ActiveFaults = sample.GetActiveFaults().ToImmutableHashSet(),
            });
            await context.Events.Writer.WriteAsync(new StreamEvent(sample, null));
        }

        public Task WaitForCurrentStreamAsync()
        {
            lock (_gate)
            {
                return _current.StreamStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            }
        }

        public void ReplaceSimulator(MachineState state)
        {
            SimulatorContext previous;
            lock (_gate)
            {
                previous = _current;
                _current = new SimulatorContext(CreateSnapshot(state, _timeProvider.GetUtcNow()));
            }

            previous.IsAvailable = false;
            previous.Events.Writer.TryWrite(new StreamEvent(
                null,
                new MachineGatewayException(
                    MachineGatewayFailureKind.Unavailable,
                    "Le processus simulateur a été remplacé.")));
        }

        private static SimulatorContext GetContext(IMachineSession session) =>
            Assert.IsType<TestSession>(session).Context;

        private static MachineSnapshot CreateSnapshot(MachineState state, DateTimeOffset timestamp)
        {
            ProductionRun? run = state is MachineState.Running or MachineState.Paused
                ? new ProductionRun(
                    Guid.NewGuid(),
                    BuiltInRecipes.WingPanelDemo,
                    ProductionRunStatus.Running,
                    timestamp)
                : null;
            ProductionRecipe? recipe = state == MachineState.Disconnected
                ? null
                : BuiltInRecipes.WingPanelDemo;
            return new MachineSnapshot(state, timestamp, recipe, run);
        }

        private sealed class SimulatorContext(MachineSnapshot snapshot)
        {
            private readonly object _snapshotGate = new();
            private MachineSnapshot _snapshot = snapshot;

            public Channel<StreamEvent> Events { get; } =
                Channel.CreateBounded<StreamEvent>(new BoundedChannelOptions(20)
                {
                    SingleReader = true,
                    SingleWriter = false,
                });

            public TaskCompletionSource StreamStarted { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public bool IsAvailable { get; set; } = true;

            public MachineSnapshot GetSnapshot()
            {
                lock (_snapshotGate)
                {
                    return _snapshot;
                }
            }

            public void SetSnapshot(MachineSnapshot value)
            {
                lock (_snapshotGate)
                {
                    _snapshot = value;
                }
            }
        }

        private sealed record TestSession(
            Guid SessionId,
            DateTimeOffset ConnectedAt,
            SimulatorContext Context) : IMachineSession;

        private sealed record StreamEvent(
            TelemetrySample? Sample,
            MachineGatewayException? Failure);
    }
}

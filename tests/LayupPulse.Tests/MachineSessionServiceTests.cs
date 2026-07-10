using System.Runtime.CompilerServices;
using System.Threading.Channels;
using LayupPulse.Application;
using LayupPulse.Domain;
using Xunit;

namespace LayupPulse.Tests;

public sealed class MachineSessionServiceTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 7, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ConnectAndDisconnectOwnTheGatewaySessionLifecycle()
    {
        TestMachineGateway gateway = new();
        await using MachineSessionService service = CreateService(gateway);

        MachineSessionOperationResult connected = await service.ConnectAsync(CancellationToken.None);
        MachineSessionOperationResult disconnected = await service.DisconnectAsync(CancellationToken.None);

        Assert.True(connected.IsSuccessful);
        Assert.True(disconnected.IsSuccessful);
        Assert.Equal(1, gateway.ConnectCount);
        Assert.Equal(1, gateway.DisconnectCount);
        Assert.Equal(MachineConnectionStatus.Disconnected, service.State.ConnectionStatus);
    }

    [Fact]
    public async Task CommandRejectionIsPropagatedWithoutTransportException()
    {
        TestMachineGateway gateway = new();
        await using MachineSessionService service = CreateService(gateway);
        await service.ConnectAsync(CancellationToken.None);
        gateway.CommandHandler = command => CreateRejectedResult(command, "Commande refusée pour ce test.");

        MachineCommandExecutionResult result = await service.ExecuteCommandAsync(
            new MachineCommand(Guid.NewGuid(), MachineCommandType.Pause, Timestamp),
            CancellationToken.None);

        Assert.Equal(MachineCommandExecutionStatus.Rejected, result.Status);
        Assert.Equal("Commande refusée pour ce test.", result.Message);
        Assert.Contains(
            service.State.RecentDiagnostics,
            message => message.Message == "Commande refusée pour ce test.");
    }

    [Fact]
    public async Task SessionBecomesStaleWhenNoTelemetryArrives()
    {
        TestMachineGateway gateway = new();
        MachineSessionOptions options = CreateOptions();
        options.StaleAfter = TimeSpan.FromMilliseconds(80);
        options.StaleCheckInterval = TimeSpan.FromMilliseconds(20);
        await using MachineSessionService service = new(gateway, TimeProvider.System, options);
        await service.ConnectAsync(CancellationToken.None);

        await WaitUntilAsync(
            () => service.State.ConnectionStatus == MachineConnectionStatus.Stale,
            TimeSpan.FromSeconds(2));

        Assert.Equal(MachineConnectionStatus.Stale, service.State.ConnectionStatus);
        Assert.Contains(
            service.State.RecentDiagnostics,
            message => message.Message.Contains("périmée", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DisconnectCancelsTheTelemetryReader()
    {
        TestMachineGateway gateway = new();
        await using MachineSessionService service = CreateService(gateway);
        await service.ConnectAsync(CancellationToken.None);

        await service.DisconnectAsync(CancellationToken.None);
        await gateway.StreamCancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(gateway.StreamCancellationObserved.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task LatestTelemetryUpdatesThePublishedSnapshotAndCounters()
    {
        TestMachineGateway gateway = new();
        await using MachineSessionService service = CreateService(gateway);
        await service.ConnectAsync(CancellationToken.None);
        TelemetrySample sample = CreateTelemetry(42, MachineState.Running);

        await gateway.Telemetry.Writer.WriteAsync(sample, CancellationToken.None);
        await WaitUntilAsync(() => service.State.LatestTelemetry?.SequenceNumber == 42, TimeSpan.FromSeconds(1));

        Assert.Equal(sample, service.State.LatestTelemetry);
        Assert.Equal(MachineState.Running, service.State.LatestSnapshot.State);
        Assert.Equal(1, service.State.ReceivedSampleCount);
    }

    private static MachineSessionService CreateService(TestMachineGateway gateway) =>
        new(gateway, TimeProvider.System, CreateOptions());

    private static MachineSessionOptions CreateOptions() => new()
    {
        StaleAfter = TimeSpan.FromSeconds(5),
        StaleCheckInterval = TimeSpan.FromMilliseconds(50),
        NotificationInterval = TimeSpan.Zero,
        DiagnosticCapacity = 10,
    };

    private static CommandResult CreateRejectedResult(MachineCommand command, string message)
    {
        MachineSnapshot snapshot = new(MachineState.Running, Timestamp, BuiltInRecipes.WingPanelDemo);
        StateTransitionResult transition = StateTransitionResult.Rejected(
            snapshot,
            new StateTransitionRejection(StateTransitionRejectionCode.InvalidState, message));
        return new CommandResult(command.CorrelationId, transition);
    }

    private static TelemetrySample CreateTelemetry(long sequence, MachineState state) =>
        new(
            Timestamp.AddMilliseconds(sequence * 50),
            sequence,
            state,
            100,
            75,
            25,
            120,
            118,
            450,
            145,
            6,
            25,
            98);

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        using CancellationTokenSource cancellation = new(timeout);
        while (!predicate())
        {
            await Task.Delay(10, cancellation.Token);
        }
    }

    private sealed class TestMachineGateway : IMachineGateway
    {
        private readonly TestMachineSession _session = new(Guid.NewGuid(), Timestamp);

        public Channel<TelemetrySample> Telemetry { get; } = Channel.CreateUnbounded<TelemetrySample>();

        public TaskCompletionSource StreamCancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ConnectCount { get; private set; }

        public int DisconnectCount { get; private set; }

        public Func<MachineCommand, CommandResult>? CommandHandler { get; set; }

        public Task<IMachineSession> ConnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectCount++;
            return Task.FromResult<IMachineSession>(_session);
        }

        public Task DisconnectAsync(IMachineSession session, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DisconnectCount++;
            return Task.CompletedTask;
        }

        public Task<CommandResult> ExecuteCommandAsync(
            IMachineSession session,
            MachineCommand command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CommandResult result = CommandHandler?.Invoke(command)
                ?? new CommandResult(
                    command.CorrelationId,
                    StateTransitionResult.Accepted(MachineState.Ready, CreateReadySnapshot()));
            return Task.FromResult(result);
        }

        public Task<MachineSnapshot> GetSnapshotAsync(
            IMachineSession session,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateReadySnapshot());
        }

        public async IAsyncEnumerable<TelemetrySample> StreamTelemetryAsync(
            IMachineSession session,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            try
            {
                await foreach (TelemetrySample sample in Telemetry.Reader.ReadAllAsync(cancellationToken))
                {
                    yield return sample;
                }
            }
            finally
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    StreamCancellationObserved.TrySetResult();
                }
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static MachineSnapshot CreateReadySnapshot() =>
            new(MachineState.Ready, Timestamp, BuiltInRecipes.WingPanelDemo);
    }

    private sealed record TestMachineSession(Guid SessionId, DateTimeOffset ConnectedAt) : IMachineSession;
}

using System.Runtime.CompilerServices;
using LayupPulse.Application;
using LayupPulse.Domain;
using Xunit;

namespace LayupPulse.Tests;

public sealed class MachineReconnectionTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 7, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExplicitDisconnectCancelsAPendingReconnectDelay()
    {
        ReconnectGateway gateway = new();
        MachineSessionOptions options = CreateOptions(TimeSpan.FromMinutes(1));
        await using MachineSessionService service = new(gateway, TimeProvider.System, options);
        TaskCompletionSource reconnecting = ObserveReconnecting(service);

        await service.ConnectAsync(CancellationToken.None);
        await reconnecting.Task.WaitAsync(TimeSpan.FromSeconds(2));
        long attemptsBeforeDisconnect = service.State.TelemetryMetrics.ReconnectCount;

        await service.DisconnectAsync(CancellationToken.None);

        Assert.Equal(MachineConnectionStatus.Disconnected, service.State.ConnectionStatus);
        Assert.Equal(1, gateway.ConnectCount);
        Assert.Equal(attemptsBeforeDisconnect, service.State.TelemetryMetrics.ReconnectCount);
        Assert.Equal(1, gateway.DisconnectCount);
    }

    [Fact]
    public async Task ReconnectSupervisorNeverOverlapsStreamAttempts()
    {
        ReconnectGateway gateway = new(requiredStreamAttempts: 5);
        MachineSessionOptions options = CreateOptions(TimeSpan.FromMilliseconds(1));
        options.ReconnectMaximumDelay = TimeSpan.FromMilliseconds(1);
        await using MachineSessionService service = new(gateway, TimeProvider.System, options);

        await service.ConnectAsync(CancellationToken.None);
        await gateway.RequiredStreamAttemptsReached.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, gateway.MaximumConcurrentStreamAttempts);
        Assert.Equal(1, gateway.MaximumConcurrentConnectAttempts);
        Assert.True(service.State.TelemetryMetrics.ReconnectCount >= 4);

        await service.DisconnectAsync(CancellationToken.None);
    }

    private static TaskCompletionSource ObserveReconnecting(MachineSessionService service)
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        service.StateChanged += (_, args) =>
        {
            if (args.State.ConnectionStatus == MachineConnectionStatus.Reconnecting)
            {
                completion.TrySetResult();
            }
        };
        return completion;
    }

    private static MachineSessionOptions CreateOptions(TimeSpan reconnectDelay) => new()
    {
        StaleAfter = TimeSpan.FromSeconds(5),
        StaleCheckInterval = TimeSpan.FromSeconds(1),
        ReconnectInitialDelay = reconnectDelay,
        ReconnectMaximumDelay = reconnectDelay,
        ReconnectBackoffMultiplier = 2,
        DiagnosticCapacity = 20,
        Alarms = new AlarmEngineOptions
        {
            CommunicationTimeout = TimeSpan.FromSeconds(10),
        },
    };

    private sealed class ReconnectGateway : IMachineGateway
    {
        private readonly int _requiredStreamAttempts;
        private int _activeConnectAttempts;
        private int _activeStreamAttempts;
        private int _connectCount;
        private int _streamAttemptCount;
        private int _maximumConcurrentConnectAttempts;
        private int _maximumConcurrentStreamAttempts;

        public ReconnectGateway(int requiredStreamAttempts = int.MaxValue)
        {
            _requiredStreamAttempts = requiredStreamAttempts;
        }

        public TaskCompletionSource RequiredStreamAttemptsReached { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ConnectCount => Volatile.Read(ref _connectCount);

        public int DisconnectCount { get; private set; }

        public int MaximumConcurrentConnectAttempts => Volatile.Read(ref _maximumConcurrentConnectAttempts);

        public int MaximumConcurrentStreamAttempts => Volatile.Read(ref _maximumConcurrentStreamAttempts);

        public async Task<IMachineSession> ConnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int concurrent = Interlocked.Increment(ref _activeConnectAttempts);
            UpdateMaximum(ref _maximumConcurrentConnectAttempts, concurrent);
            try
            {
                Interlocked.Increment(ref _connectCount);
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                return new TestSession(Guid.NewGuid(), Timestamp);
            }
            finally
            {
                Interlocked.Decrement(ref _activeConnectAttempts);
            }
        }

        public Task DisconnectAsync(IMachineSession session, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DisconnectCount++;
            return Task.CompletedTask;
        }

        public ValueTask AbandonAsync(IMachineSession session) => ValueTask.CompletedTask;

        public Task<CommandResult> ExecuteCommandAsync(
            IMachineSession session,
            MachineCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<MachineSnapshot> GetSnapshotAsync(
            IMachineSession session,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new MachineSnapshot(MachineState.Ready, Timestamp));
        }

        public async IAsyncEnumerable<TelemetrySample> StreamTelemetryAsync(
            IMachineSession session,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            int concurrent = Interlocked.Increment(ref _activeStreamAttempts);
            UpdateMaximum(ref _maximumConcurrentStreamAttempts, concurrent);
            int attempt = Interlocked.Increment(ref _streamAttemptCount);
            if (attempt >= _requiredStreamAttempts)
            {
                RequiredStreamAttemptsReached.TrySetResult();
            }

            try
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
            }
            finally
            {
                Interlocked.Decrement(ref _activeStreamAttempts);
            }

            yield break;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static void UpdateMaximum(ref int target, int value)
        {
            int current;
            do
            {
                current = Volatile.Read(ref target);
                if (current >= value)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref target, value, current) != current);
        }
    }

    private sealed record TestSession(Guid SessionId, DateTimeOffset ConnectedAt) : IMachineSession;
}

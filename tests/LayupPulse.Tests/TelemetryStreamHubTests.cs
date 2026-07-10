using LayupPulse.Simulator;
using Xunit;

namespace LayupPulse.Tests;

public sealed class TelemetryStreamHubTests
{
    [Fact]
    public async Task CancellationTerminatesTelemetryReadImmediately()
    {
        // Préparation
        TelemetryStreamHub hub = new();
        hub.MarkConnected();
        using CancellationTokenSource cancellation = new();
        await using IAsyncEnumerator<SimulationSnapshot> enumerator =
            hub.ReadAllAsync(cancellation.Token).GetAsyncEnumerator();

        // Action
        Task<bool> pendingRead = enumerator.MoveNextAsync().AsTask();
        cancellation.Cancel();

        // Vérification
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pendingRead);
    }

    [Fact]
    public async Task DisconnectCompletesTelemetryReadCleanly()
    {
        // Préparation
        TelemetryStreamHub hub = new();
        hub.MarkConnected();
        await using IAsyncEnumerator<SimulationSnapshot> enumerator =
            hub.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();
        Task<bool> pendingRead = enumerator.MoveNextAsync().AsTask();

        // Action
        hub.MarkDisconnected();

        // Vérification
        Assert.False(await pendingRead);
    }
}

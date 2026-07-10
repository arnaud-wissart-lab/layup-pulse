using Grpc.Core;
using Grpc.Net.Client;
using LayupPulse.Contracts.Grpc;
using LayupPulse.Domain;
using LayupPulse.Simulator;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace LayupPulse.Tests;

public sealed class GrpcSimulatorIntegrationTests
{
    [Fact]
    public async Task LocalGrpcProcessSupportsSnapshotCommandsStreamingAndCancellation()
    {
        // Préparation
        string[] arguments =
        [
            "--Simulator:Endpoint=http://127.0.0.1:0",
            "--Simulator:Seed=31415",
            "--Simulator:TelemetryRateHz=50",
            "--Logging:LogLevel:Default=Warning",
        ];
        await using WebApplication application = SimulatorHost.BuildApplication(arguments);
        await application.StartAsync();

        try
        {
            string address = Assert.Single(application.Urls);
            using GrpcChannel channel = GrpcChannel.ForAddress(address);
            MachineSimulator.MachineSimulatorClient client = new(channel);

            // Action
            CommandResultMessage connected = await client.ExecuteCommandAsync(CreateCommand(CommandType.Connect));
            MachineSnapshotMessage readySnapshot = await client.GetSnapshotAsync(new GetSnapshotRequest());
            CommandResultMessage loaded = await client.ExecuteCommandAsync(
                CreateCommand(CommandType.LoadRecipe, BuiltInRecipes.WingPanelDemo.Id.ToString("D")));
            CommandResultMessage started = await client.ExecuteCommandAsync(CreateCommand(CommandType.Start));

            using CancellationTokenSource interruptionTimeout = new(TimeSpan.FromSeconds(2));
            using AsyncServerStreamingCall<TelemetryMessage> interruptedStream = client.StreamTelemetry(
                new StreamTelemetryRequest());
            bool received = await interruptedStream.ResponseStream.MoveNext(interruptionTimeout.Token);
            TelemetryMessage telemetry = interruptedStream.ResponseStream.Current;
            CommandResultMessage communicationDrop = await client.InjectFaultAsync(new InjectFaultRequest
            {
                CorrelationId = Guid.NewGuid().ToString("D"),
                Fault = SimulatedFault.CommunicationDrop,
            });
            RpcException interruption = await Assert.ThrowsAsync<RpcException>(() =>
                interruptedStream.ResponseStream.MoveNext(interruptionTimeout.Token));
            CommandResultMessage cleared = await client.ClearFaultAsync(new ClearFaultRequest
            {
                CorrelationId = Guid.NewGuid().ToString("D"),
                Fault = SimulatedFault.CommunicationDrop,
            });
            CommandResultMessage reset = await client.ExecuteCommandAsync(CreateCommand(CommandType.Reset));
            CommandResultMessage restarted = await client.ExecuteCommandAsync(CreateCommand(CommandType.Start));

            using CancellationTokenSource streamCancellation = new(TimeSpan.FromSeconds(2));
            using AsyncServerStreamingCall<TelemetryMessage> cancellableStream = client.StreamTelemetry(
                new StreamTelemetryRequest(),
                cancellationToken: streamCancellation.Token);
            Assert.True(await cancellableStream.ResponseStream.MoveNext(streamCancellation.Token));
            streamCancellation.Cancel();
            Task<bool> terminalRead = cancellableStream.ResponseStream.MoveNext(CancellationToken.None);
            Task completed = await Task.WhenAny(terminalRead, Task.Delay(TimeSpan.FromSeconds(2)));

            // Vérification
            Assert.True(connected.Accepted);
            Assert.Equal(LayupPulse.Contracts.Grpc.MachineState.Ready, readySnapshot.MachineState);
            Assert.True(loaded.Accepted);
            Assert.True(started.Accepted);
            Assert.True(received);
            Assert.True(telemetry.SequenceNumber > 0);
            Assert.Equal(LayupPulse.Contracts.Grpc.MachineState.Running, telemetry.MachineState);
            Assert.True(communicationDrop.Accepted);
            Assert.Equal(StatusCode.Unavailable, interruption.StatusCode);
            Assert.True(cleared.Accepted);
            Assert.True(reset.Accepted);
            Assert.True(restarted.Accepted);
            Assert.Same(terminalRead, completed);
            Assert.True(terminalRead.IsCanceled || terminalRead.IsFaulted || !await terminalRead);
        }
        finally
        {
            await application.StopAsync();
        }
    }

    private static ExecuteCommandRequest CreateCommand(CommandType command, string recipeId = "") => new()
    {
        CorrelationId = Guid.NewGuid().ToString("D"),
        Command = command,
        RecipeId = recipeId,
    };
}

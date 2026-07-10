using LayupPulse.Application;
using LayupPulse.Domain;
using LayupPulse.Infrastructure;
using LayupPulse.Simulator;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LayupPulse.Tests;

public sealed class GrpcMachineGatewayIntegrationTests
{
    [Fact]
    public async Task GatewayConnectsMapsCommandsStreamsTelemetryAndDisconnects()
    {
        string[] arguments =
        [
            "--Simulator:Endpoint=http://127.0.0.1:0",
            "--Simulator:Seed=2718",
            "--Simulator:TelemetryRateHz=50",
            "--Logging:LogLevel:Default=Warning",
        ];
        await using WebApplication application = SimulatorHost.BuildApplication(arguments);
        await application.StartAsync(CancellationToken.None);
        await using GrpcMachineGateway gateway = new(
            new GrpcMachineGatewayOptions
            {
                Endpoint = Assert.Single(application.Urls),
                RequestTimeout = TimeSpan.FromSeconds(2),
            },
            TimeProvider.System,
            NullLogger<GrpcMachineGateway>.Instance);
        IMachineSession? session = null;

        try
        {
            session = await gateway.ConnectAsync(CancellationToken.None);
            MachineSnapshot ready = await gateway.GetSnapshotAsync(session, CancellationToken.None);
            MachineCommand loadRecipe = new(
                Guid.NewGuid(),
                MachineCommandType.LoadRecipe,
                DateTimeOffset.UtcNow,
                BuiltInRecipes.WingPanelDemo);
            CommandResult loaded = await gateway.ExecuteCommandAsync(
                session,
                loadRecipe,
                CancellationToken.None);
            CommandResult started = await gateway.ExecuteCommandAsync(
                session,
                new MachineCommand(Guid.NewGuid(), MachineCommandType.Start, DateTimeOffset.UtcNow),
                CancellationToken.None);

            using CancellationTokenSource streamTimeout = new(TimeSpan.FromSeconds(2));
            TelemetrySample firstSample = await gateway.StreamTelemetryAsync(session, streamTimeout.Token)
                .FirstAsync(streamTimeout.Token);

            Assert.Equal(MachineState.Ready, ready.State);
            Assert.True(loaded.IsAccepted);
            Assert.Equal(BuiltInRecipes.WingPanelDemo, loaded.Transition.Snapshot.LoadedRecipe);
            Assert.True(started.IsAccepted);
            Assert.Equal(MachineState.Running, started.Transition.Snapshot.State);
            Assert.True(firstSample.SequenceNumber > 0);
            Assert.Equal(MachineState.Running, firstSample.MachineState);

            await gateway.DisconnectAsync(session, CancellationToken.None);
            session = null;
        }
        finally
        {
            if (session is not null)
            {
                await gateway.DisconnectAsync(session, CancellationToken.None);
            }

            await application.StopAsync(CancellationToken.None);
        }
    }
}

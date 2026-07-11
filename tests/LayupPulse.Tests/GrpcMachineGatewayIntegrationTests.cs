using LayupPulse.Application;
using LayupPulse.Domain;
using LayupPulse.Infrastructure;
using LayupPulse.Simulator;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LayupPulse.Tests;

public sealed class GrpcMachineGatewayIntegrationTests
{
    [Fact]
    public async Task ReplacementGatewayAttachesToRunningSimulatorWithoutResettingMachineContext()
    {
        // Préparation
        string[] arguments =
        [
            "--Simulator:Endpoint=http://127.0.0.1:0",
            "--Simulator:Seed=1618",
            "--Simulator:TelemetryRateHz=50",
            "--Logging:LogLevel:Default=Warning",
        ];
        await using WebApplication application = SimulatorHost.BuildApplication(arguments);
        await application.StartAsync(CancellationToken.None);
        string endpoint = Assert.Single(application.Urls);
        DeterministicMachineSimulator simulator = application.Services
            .GetRequiredService<DeterministicMachineSimulator>();
        GrpcMachineGateway gatewayA = CreateGateway(endpoint);
        await using GrpcMachineGateway gatewayB = CreateGateway(endpoint);
        IMachineSession? sessionB = null;

        try
        {
            MachineTransportAttachment attachmentA = await AttachAndConnectMachineAsync(gatewayA);
            IMachineSession sessionA = attachmentA.Session;
            CommandResult loaded = await gatewayA.ExecuteCommandAsync(
                sessionA,
                new MachineCommand(
                    Guid.NewGuid(),
                    MachineCommandType.LoadRecipe,
                    DateTimeOffset.UtcNow,
                    BuiltInRecipes.WingPanelDemo),
                CancellationToken.None);
            CommandResult started = await gatewayA.ExecuteCommandAsync(
                sessionA,
                new MachineCommand(Guid.NewGuid(), MachineCommandType.Start, DateTimeOffset.UtcNow),
                CancellationToken.None);
            using CancellationTokenSource progressTimeout = new(TimeSpan.FromSeconds(2));
            TelemetrySample sampleA = await gatewayA.StreamTelemetryAsync(sessionA, progressTimeout.Token)
                .FirstAsync(
                    sample => sample.CycleProgressPercentage > 0,
                    progressTimeout.Token);
            SimulationSnapshot beforeReplacement = simulator.GetSnapshot();

            // Action : fermeture locale de A sans commande machine Disconnect, puis attachement de B.
            await gatewayA.DisposeAsync();
            MachineTransportAttachment attachmentB = await gatewayB.AttachAsync(CancellationToken.None);
            sessionB = attachmentB.Session;
            MachineSnapshot snapshotB = attachmentB.Snapshot;
            using CancellationTokenSource streamTimeout = new(TimeSpan.FromSeconds(2));
            TelemetrySample sampleB = await gatewayB.StreamTelemetryAsync(sessionB, streamTimeout.Token)
                .FirstAsync(streamTimeout.Token);
            SimulationSnapshot afterReplacement = simulator.GetSnapshot();

            // Vérification
            Assert.True(loaded.IsAccepted);
            Assert.True(started.IsAccepted);
            Assert.Equal(MachineState.Running, snapshotB.State);
            Assert.Equal(BuiltInRecipes.WingPanelDemo, snapshotB.LoadedRecipe);
            Assert.Equal(MachineState.Running, afterReplacement.Machine.State);
            Assert.Equal(beforeReplacement.Machine.LoadedRecipe, afterReplacement.Machine.LoadedRecipe);
            Assert.Equal(beforeReplacement.Machine.CurrentRun?.Id, afterReplacement.Machine.CurrentRun?.Id);
            Assert.Equal(ProductionRunStatus.Running, afterReplacement.Machine.CurrentRun?.Status);
            Assert.Equal(beforeReplacement.Machine.ActiveFaults, afterReplacement.Machine.ActiveFaults);
            Assert.True(sampleB.CycleProgressPercentage >= sampleA.CycleProgressPercentage);

            await gatewayB.DisconnectAsync(sessionB, CancellationToken.None);
            sessionB = null;
            Assert.Equal(MachineState.Disconnected, simulator.State);
        }
        finally
        {
            await gatewayA.DisposeAsync();
            if (sessionB is not null)
            {
                await gatewayB.DisconnectAsync(sessionB, CancellationToken.None);
            }

            await application.StopAsync(CancellationToken.None);
        }
    }

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
            MachineTransportAttachment attachment = await AttachAndConnectMachineAsync(gateway);
            session = attachment.Session;
            MachineSnapshot ready = attachment.Snapshot;
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

    private static GrpcMachineGateway CreateGateway(string endpoint) => new(
        new GrpcMachineGatewayOptions
        {
            Endpoint = endpoint,
            RequestTimeout = TimeSpan.FromSeconds(2),
        },
        TimeProvider.System,
        NullLogger<GrpcMachineGateway>.Instance);

    private static async Task<MachineTransportAttachment> AttachAndConnectMachineAsync(
        GrpcMachineGateway gateway)
    {
        MachineTransportAttachment attachment = await gateway.AttachAsync(CancellationToken.None);
        Assert.Equal(MachineState.Disconnected, attachment.Snapshot.State);
        CommandResult connected = await gateway.ExecuteCommandAsync(
            attachment.Session,
            new MachineCommand(
                Guid.NewGuid(),
                MachineCommandType.ConnectRequested,
                DateTimeOffset.UtcNow),
            CancellationToken.None);
        Assert.True(connected.IsAccepted);
        return attachment with { Snapshot = connected.Transition.Snapshot };
    }
}

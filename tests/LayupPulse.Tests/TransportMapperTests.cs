using LayupPulse.Contracts.Grpc;
using LayupPulse.Domain;
using LayupPulse.Simulator;
using Xunit;
using DomainFaultType = LayupPulse.Domain.FaultType;
using DomainMachineState = LayupPulse.Domain.MachineState;

namespace LayupPulse.Tests;

public sealed class TransportMapperTests
{
    private static readonly DateTimeOffset Timestamp =
        new(2026, 7, 10, 8, 0, 0, 125, TimeSpan.Zero);

    [Fact]
    public void DomainTelemetryRoundTripsThroughProtobuf()
    {
        // Préparation
        TelemetrySample domain = new(
            Timestamp,
            42,
            DomainMachineState.Running,
            123.4,
            234.5,
            25.6,
            120,
            117.5,
            451.2,
            143.8,
            6.02,
            37.5,
            98.2);

        // Action
        TelemetryMessage transport = domain.ToTransport([DomainFaultType.HighTemperature]);
        TelemetrySample roundTrip = transport.ToDomain();

        // Vérification
        Assert.Equal(domain, roundTrip);
        Assert.Equal(SimulatedFault.OverTemperature, Assert.Single(transport.ActiveFaults));
        Assert.Equal(Timestamp, transport.TimestampUtc.ToDateTimeOffset());
    }

    [Fact]
    public void LoadRecipeRequestMapsToBuiltInDomainRecipe()
    {
        // Préparation
        Guid correlationId = new("ce85e321-a954-4a7e-8eb8-486895c52353");
        ExecuteCommandRequest request = new()
        {
            CorrelationId = correlationId.ToString("D"),
            Command = CommandType.LoadRecipe,
            RecipeId = BuiltInRecipes.WingPanelDemo.Id.ToString("D"),
        };

        // Action
        MachineCommand command = request.ToDomain(Timestamp);

        // Vérification
        Assert.Equal(correlationId, command.CorrelationId);
        Assert.Equal(MachineCommandType.LoadRecipe, command.Type);
        Assert.Equal(Timestamp, command.Timestamp);
        Assert.Equal(BuiltInRecipes.WingPanelDemo, command.Recipe);
    }

    [Fact]
    public void MachineStatesRoundTripBetweenDomainAndProtobuf()
    {
        foreach (DomainMachineState state in Enum.GetValues<DomainMachineState>())
        {
            Assert.Equal(state, state.ToTransport().ToDomain());
        }
    }

    [Theory]
    [InlineData(DomainFaultType.HighTemperature, SimulatedFault.OverTemperature)]
    [InlineData(DomainFaultType.LowMaterialPressure, SimulatedFault.LowMaterialPressure)]
    [InlineData(DomainFaultType.UnstableCompactionForce, SimulatedFault.UnstableCompactionForce)]
    [InlineData(DomainFaultType.HeadPositionError, SimulatedFault.HeadPositionError)]
    [InlineData(DomainFaultType.CommunicationTimeout, SimulatedFault.CommunicationDrop)]
    public void FaultMappingsAreExplicitAndReversible(
        DomainFaultType domain,
        SimulatedFault transport)
    {
        Assert.Equal(transport, ((DomainFaultType?)domain).ToTransport());
        Assert.Equal(domain, transport.ToDomain());
    }

    [Fact]
    public void UnknownRecipeProducesStructuredTransportRejection()
    {
        // Préparation
        ExecuteCommandRequest request = new()
        {
            CorrelationId = Guid.NewGuid().ToString("D"),
            Command = CommandType.LoadRecipe,
            RecipeId = Guid.NewGuid().ToString("D"),
        };

        // Action
        TransportMappingException exception = Assert.Throws<TransportMappingException>(
            () => request.ToDomain(Timestamp));

        // Vérification
        Assert.Equal(RejectionReason.UnknownRecipe, exception.RejectionReason);
    }
}

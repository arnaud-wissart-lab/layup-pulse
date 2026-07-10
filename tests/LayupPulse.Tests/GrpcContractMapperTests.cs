using Google.Protobuf.WellKnownTypes;
using LayupPulse.Application;
using LayupPulse.Contracts.Grpc;
using LayupPulse.Domain;
using LayupPulse.Infrastructure;
using Xunit;
using DomainMachineState = LayupPulse.Domain.MachineState;
using TransportMachineState = LayupPulse.Contracts.Grpc.MachineState;

namespace LayupPulse.Tests;

public sealed class GrpcContractMapperTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 7, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SnapshotMappingPreservesLatestStateRecipeTelemetryTimestampAndFaults()
    {
        ProductionRecipe recipe = BuiltInRecipes.WingPanelDemo;
        MachineSnapshotMessage message = new()
        {
            MachineState = TransportMachineState.Faulted,
            Telemetry = new TelemetryMessage
            {
                TimestampUtc = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(Timestamp),
                MachineState = TransportMachineState.Faulted,
                SequenceNumber = 17,
            },
            LoadedRecipe = new RecipeSummaryMessage
            {
                Id = recipe.Id.ToString("D"),
                Name = recipe.Name,
                PartReference = recipe.PartReference,
                TargetTemperatureCelsius = recipe.TargetTemperatureCelsius,
                TargetPressureBar = recipe.TargetPressureBar,
                TargetFeedRateMillimetersPerSecond = recipe.FeedRateMillimetersPerSecond,
                PassCount = recipe.PassCount,
                EstimatedDurationSeconds = recipe.EstimatedDuration.TotalSeconds,
            },
        };
        message.ActiveFaults.Add(SimulatedFault.OverTemperature);

        MachineSnapshot snapshot = message.ToDomain(Timestamp.AddMinutes(1));

        Assert.Equal(DomainMachineState.Faulted, snapshot.State);
        Assert.Equal(Timestamp, snapshot.Timestamp);
        Assert.Equal(recipe, snapshot.LoadedRecipe);
        Assert.Contains(FaultType.HighTemperature, snapshot.ActiveFaults);
    }

    [Fact]
    public void TelemetryMappingPreservesEveryDisplayedSignal()
    {
        TelemetryMessage message = new()
        {
            TimestampUtc = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(Timestamp),
            SequenceNumber = 81,
            MachineState = TransportMachineState.Running,
            HeadXMillimeters = 450.1,
            HeadYMillimeters = 205.2,
            HeadZMillimeters = 25.3,
            TargetFeedRateMillimetersPerSecond = 120,
            ActualFeedRateMillimetersPerSecond = 118.5,
            CompactionForceNewtons = 452,
            HeaterTemperatureCelsius = 144.8,
            MaterialPressureBar = 6.01,
            CycleProgressPercentage = 48.2,
            ProcessHealthPercentage = 97.6,
        };

        TelemetrySample sample = message.ToDomain();

        Assert.Equal(Timestamp, sample.Timestamp);
        Assert.Equal(81, sample.SequenceNumber);
        Assert.Equal(DomainMachineState.Running, sample.MachineState);
        Assert.Equal(450.1, sample.HeadXMillimeters);
        Assert.Equal(205.2, sample.HeadYMillimeters);
        Assert.Equal(25.3, sample.HeadZMillimeters);
        Assert.Equal(120, sample.TargetFeedRateMillimetersPerSecond);
        Assert.Equal(118.5, sample.ActualFeedRateMillimetersPerSecond);
        Assert.Equal(452, sample.CompactionForceNewtons);
        Assert.Equal(144.8, sample.HeaterTemperatureCelsius);
        Assert.Equal(6.01, sample.MaterialPressureBar);
        Assert.Equal(48.2, sample.CycleProgressPercentage);
        Assert.Equal(97.6, sample.ProcessHealthPercentage);
    }

    [Fact]
    public void RejectedCommandMappingKeepsStructuredApplicationRejection()
    {
        Guid correlationId = Guid.NewGuid();
        MachineSnapshot snapshot = new(DomainMachineState.Ready, Timestamp, BuiltInRecipes.WingPanelDemo);
        CommandResultMessage message = new()
        {
            CorrelationId = correlationId.ToString("D"),
            Accepted = false,
            RejectionReason = RejectionReason.InvalidState,
            RejectionDetail = "État invalide pour ce test.",
            MachineState = TransportMachineState.Ready,
        };

        CommandResult result = message.ToDomain(correlationId, DomainMachineState.Ready, snapshot);

        Assert.False(result.IsAccepted);
        Assert.Equal(
            StateTransitionRejectionCode.InvalidState,
            Assert.IsType<StateTransitionRejection>(result.Transition.Rejection).Code);
        Assert.Equal("État invalide pour ce test.", result.Transition.Rejection.Message);
    }
}

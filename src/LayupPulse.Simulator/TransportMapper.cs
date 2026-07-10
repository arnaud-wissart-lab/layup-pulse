using Google.Protobuf.WellKnownTypes;
using LayupPulse.Contracts.Grpc;
using LayupPulse.Domain;
using DomainCommandType = LayupPulse.Domain.MachineCommandType;
using DomainFaultType = LayupPulse.Domain.FaultType;
using DomainMachineState = LayupPulse.Domain.MachineState;
using TransportCommandType = LayupPulse.Contracts.Grpc.CommandType;
using TransportMachineState = LayupPulse.Contracts.Grpc.MachineState;

namespace LayupPulse.Simulator;

/// <summary>
/// Convertit explicitement les messages protobuf sans exposer les objets métier sur le réseau.
/// </summary>
public static class TransportMapper
{
    public static MachineCommand ToDomain(
        this ExecuteCommandRequest request,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid correlationId = ParseCorrelationId(request.CorrelationId);
        DomainCommandType commandType = request.Command.ToDomain();
        ProductionRecipe? recipe = null;

        if (commandType == DomainCommandType.LoadRecipe)
        {
            recipe = ResolveRecipe(request.RecipeId);
        }

        return new MachineCommand(correlationId, commandType, timestamp, recipe);
    }

    public static MachineCommand ToDomain(
        this InjectFaultRequest request,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new MachineCommand(
            ParseCorrelationId(request.CorrelationId),
            DomainCommandType.CriticalFaultRaised,
            timestamp,
            fault: request.Fault.ToDomain());
    }

    public static MachineCommand ToDomain(
        this ClearFaultRequest request,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new MachineCommand(
            ParseCorrelationId(request.CorrelationId),
            DomainCommandType.FaultCleared,
            timestamp,
            fault: request.Fault.ToDomain());
    }

    public static DomainCommandType ToDomain(this TransportCommandType commandType) => commandType switch
    {
        TransportCommandType.Connect => DomainCommandType.ConnectRequested,
        TransportCommandType.Disconnect => DomainCommandType.Disconnected,
        TransportCommandType.LoadRecipe => DomainCommandType.LoadRecipe,
        TransportCommandType.Start => DomainCommandType.Start,
        TransportCommandType.Pause => DomainCommandType.Pause,
        TransportCommandType.Resume => DomainCommandType.Resume,
        TransportCommandType.Stop => DomainCommandType.Stop,
        TransportCommandType.Reset => DomainCommandType.Reset,
        _ => throw new TransportMappingException(
            RejectionReason.UnsupportedCommand,
            "La commande de transport n’est pas prise en charge."),
    };

    public static DomainMachineState ToDomain(this TransportMachineState machineState) => machineState switch
    {
        TransportMachineState.Disconnected => DomainMachineState.Disconnected,
        TransportMachineState.Connecting => DomainMachineState.Connecting,
        TransportMachineState.Ready => DomainMachineState.Ready,
        TransportMachineState.Running => DomainMachineState.Running,
        TransportMachineState.Paused => DomainMachineState.Paused,
        TransportMachineState.Faulted => DomainMachineState.Faulted,
        TransportMachineState.Completed => DomainMachineState.Completed,
        _ => throw new TransportMappingException(
            RejectionReason.InvalidRequest,
            "L’état machine de transport n’est pas défini."),
    };

    public static DomainFaultType ToDomain(this SimulatedFault fault) => fault switch
    {
        SimulatedFault.OverTemperature => DomainFaultType.HighTemperature,
        SimulatedFault.LowMaterialPressure => DomainFaultType.LowMaterialPressure,
        SimulatedFault.UnstableCompactionForce => DomainFaultType.UnstableCompactionForce,
        SimulatedFault.HeadPositionError => DomainFaultType.HeadPositionError,
        SimulatedFault.CommunicationDrop => DomainFaultType.CommunicationTimeout,
        _ => throw new TransportMappingException(
            RejectionReason.FaultRequired,
            "Un défaut simulé explicite est requis."),
    };

    public static TransportMachineState ToTransport(this DomainMachineState machineState) => machineState switch
    {
        DomainMachineState.Disconnected => TransportMachineState.Disconnected,
        DomainMachineState.Connecting => TransportMachineState.Connecting,
        DomainMachineState.Ready => TransportMachineState.Ready,
        DomainMachineState.Running => TransportMachineState.Running,
        DomainMachineState.Paused => TransportMachineState.Paused,
        DomainMachineState.Faulted => TransportMachineState.Faulted,
        DomainMachineState.Completed => TransportMachineState.Completed,
        _ => throw new ArgumentOutOfRangeException(nameof(machineState), machineState, null),
    };

    public static SimulatedFault ToTransport(this DomainFaultType? fault) => fault switch
    {
        DomainFaultType.HighTemperature => SimulatedFault.OverTemperature,
        DomainFaultType.LowMaterialPressure => SimulatedFault.LowMaterialPressure,
        DomainFaultType.UnstableCompactionForce => SimulatedFault.UnstableCompactionForce,
        DomainFaultType.HeadPositionError => SimulatedFault.HeadPositionError,
        DomainFaultType.CommunicationTimeout => SimulatedFault.CommunicationDrop,
        null => SimulatedFault.None,
        _ => throw new ArgumentOutOfRangeException(nameof(fault), fault, null),
    };

    public static CommandResultMessage ToTransport(
        this CommandResult result,
        TransportCommandType commandType)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new CommandResultMessage
        {
            CorrelationId = result.CorrelationId.ToString("D"),
            Command = commandType,
            Accepted = result.IsAccepted,
            RejectionReason = result.Transition.Rejection?.Code.ToTransport() ?? RejectionReason.None,
            RejectionDetail = result.Transition.Rejection?.Message ?? string.Empty,
            MachineState = result.Transition.Snapshot.State.ToTransport(),
        };
    }

    public static MachineSnapshotMessage ToTransport(this SimulationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        MachineSnapshotMessage message = new()
        {
            MachineState = snapshot.Machine.State.ToTransport(),
            Telemetry = snapshot.Telemetry.ToTransport(snapshot.Machine.ActiveFaults),
        };
        message.ActiveFaults.Add(snapshot.Machine.ActiveFaults
            .Order()
            .Select(static fault => ((DomainFaultType?)fault).ToTransport()));

        if (snapshot.Machine.LoadedRecipe is not null)
        {
            message.LoadedRecipe = snapshot.Machine.LoadedRecipe.ToTransport();
        }

        return message;
    }

    public static TelemetryMessage ToTransport(
        this TelemetrySample telemetry,
        IEnumerable<DomainFaultType> activeFaults)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(activeFaults);

        TelemetryMessage message = new()
        {
            SequenceNumber = telemetry.SequenceNumber,
            TimestampUtc = Timestamp.FromDateTimeOffset(telemetry.Timestamp),
            MachineState = telemetry.MachineState.ToTransport(),
            HeadXMillimeters = telemetry.HeadXMillimeters,
            HeadYMillimeters = telemetry.HeadYMillimeters,
            HeadZMillimeters = telemetry.HeadZMillimeters,
            TargetFeedRateMillimetersPerSecond = telemetry.TargetFeedRateMillimetersPerSecond,
            ActualFeedRateMillimetersPerSecond = telemetry.ActualFeedRateMillimetersPerSecond,
            CompactionForceNewtons = telemetry.CompactionForceNewtons,
            HeaterTemperatureCelsius = telemetry.HeaterTemperatureCelsius,
            MaterialPressureBar = telemetry.MaterialPressureBar,
            CycleProgressPercentage = telemetry.CycleProgressPercentage,
            ProcessHealthPercentage = telemetry.ProcessHealthPercentage,
        };
        message.ActiveFaults.Add(activeFaults
            .Order()
            .Select(static fault => ((DomainFaultType?)fault).ToTransport()));
        return message;
    }

    public static TelemetrySample ToDomain(this TelemetryMessage telemetry)
    {
        ArgumentNullException.ThrowIfNull(telemetry);

        if (telemetry.TimestampUtc is null)
        {
            throw new TransportMappingException(
                RejectionReason.InvalidRequest,
                "L’horodatage UTC de la télémétrie est requis.");
        }

        return new TelemetrySample(
            telemetry.TimestampUtc.ToDateTimeOffset(),
            telemetry.SequenceNumber,
            telemetry.MachineState.ToDomain(),
            telemetry.HeadXMillimeters,
            telemetry.HeadYMillimeters,
            telemetry.HeadZMillimeters,
            telemetry.TargetFeedRateMillimetersPerSecond,
            telemetry.ActualFeedRateMillimetersPerSecond,
            telemetry.CompactionForceNewtons,
            telemetry.HeaterTemperatureCelsius,
            telemetry.MaterialPressureBar,
            telemetry.CycleProgressPercentage,
            telemetry.ProcessHealthPercentage,
            telemetry.ActiveFaults
                .Where(static fault => fault != SimulatedFault.None)
                .Select(static fault => fault.ToDomain()));
    }

    public static CommandResultMessage ToRejectedTransport(
        this TransportMappingException exception,
        string correlationId,
        TransportCommandType commandType,
        DomainMachineState machineState) => new()
        {
            CorrelationId = correlationId,
            Command = commandType,
            Accepted = false,
            RejectionReason = exception.RejectionReason,
            RejectionDetail = exception.Message,
            MachineState = machineState.ToTransport(),
        };

    private static RecipeSummaryMessage ToTransport(this ProductionRecipe recipe) => new()
    {
        Id = recipe.Id.ToString("D"),
        Name = recipe.Name,
        PartReference = recipe.PartReference,
        TargetTemperatureCelsius = recipe.TargetTemperatureCelsius,
        TargetPressureBar = recipe.TargetPressureBar,
        TargetFeedRateMillimetersPerSecond = recipe.FeedRateMillimetersPerSecond,
        PassCount = recipe.PassCount,
        EstimatedDurationSeconds = recipe.EstimatedDuration.TotalSeconds,
    };

    private static RejectionReason ToTransport(this StateTransitionRejectionCode rejectionCode) =>
        rejectionCode switch
        {
            StateTransitionRejectionCode.InvalidState => RejectionReason.InvalidState,
            StateTransitionRejectionCode.RecipeRequired => RejectionReason.RecipeRequired,
            StateTransitionRejectionCode.InvalidRecipe => RejectionReason.InvalidRecipe,
            StateTransitionRejectionCode.ProductionRunMissing => RejectionReason.ProductionRunMissing,
            StateTransitionRejectionCode.FaultRequired => RejectionReason.FaultRequired,
            StateTransitionRejectionCode.FaultNotActive => RejectionReason.FaultNotActive,
            StateTransitionRejectionCode.ActiveFaultsRemain => RejectionReason.ActiveFaultRemains,
            StateTransitionRejectionCode.UnsupportedCommand => RejectionReason.UnsupportedCommand,
            _ => RejectionReason.InvalidRequest,
        };

    private static Guid ParseCorrelationId(string correlationId)
    {
        if (!Guid.TryParse(correlationId, out Guid parsed) || parsed == Guid.Empty)
        {
            throw new TransportMappingException(
                RejectionReason.InvalidRequest,
                "L’identifiant de corrélation doit être un GUID non vide.");
        }

        return parsed;
    }

    private static ProductionRecipe ResolveRecipe(string recipeId)
    {
        if (string.IsNullOrWhiteSpace(recipeId))
        {
            throw new TransportMappingException(
                RejectionReason.RecipeRequired,
                "L’identifiant de la recette est requis.");
        }

        ProductionRecipe recipe = BuiltInRecipes.WingPanelDemo;
        if (!Guid.TryParse(recipeId, out Guid parsed) || parsed != recipe.Id)
        {
            throw new TransportMappingException(
                RejectionReason.UnknownRecipe,
                "La recette demandée n’existe pas dans ce simulateur.");
        }

        return recipe;
    }
}

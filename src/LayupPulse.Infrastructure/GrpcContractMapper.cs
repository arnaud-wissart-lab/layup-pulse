using Google.Protobuf.WellKnownTypes;
using LayupPulse.Application;
using LayupPulse.Contracts.Grpc;
using LayupPulse.Domain;
using DomainMachineState = LayupPulse.Domain.MachineState;
using TransportCommandType = LayupPulse.Contracts.Grpc.CommandType;
using TransportMachineState = LayupPulse.Contracts.Grpc.MachineState;

namespace LayupPulse.Infrastructure;

/// <summary>
/// Convertit explicitement le contrat gRPC en objets métier côté client.
/// </summary>
public static class GrpcContractMapper
{
    public static ExecuteCommandRequest ToTransport(this MachineCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        ExecuteCommandRequest request = new()
        {
            CorrelationId = command.CorrelationId.ToString("D"),
            Command = command.Type.ToTransport(),
        };

        if (command.Type == MachineCommandType.LoadRecipe)
        {
            request.RecipeId = command.Recipe?.Id.ToString("D") ?? string.Empty;
        }

        return request;
    }

    public static MachineSnapshot ToDomain(
        this MachineSnapshotMessage message,
        DateTimeOffset receivedAt)
    {
        ArgumentNullException.ThrowIfNull(message);

        ProductionRecipe? recipe = message.LoadedRecipe is null
            ? null
            : message.LoadedRecipe.ToDomain();
        DateTimeOffset timestamp = message.Telemetry?.TimestampUtc is null
            ? receivedAt
            : message.Telemetry.TimestampUtc.ToDateTimeOffset();
        FaultType[] activeFaults = message.ActiveFaults
            .Where(static fault => fault != SimulatedFault.None)
            .Select(static fault => fault.ToDomain())
            .ToArray();

        return new MachineSnapshot(
            message.MachineState.ToDomain(),
            timestamp,
            recipe,
            activeFaults: activeFaults);
    }

    public static TelemetrySample ToDomain(this TelemetryMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.TimestampUtc is null)
        {
            throw InvalidResponse("La télémétrie reçue ne contient aucun horodatage UTC.");
        }

        try
        {
            return new TelemetrySample(
                message.TimestampUtc.ToDateTimeOffset(),
                message.SequenceNumber,
                message.MachineState.ToDomain(),
                message.HeadXMillimeters,
                message.HeadYMillimeters,
                message.HeadZMillimeters,
                message.TargetFeedRateMillimetersPerSecond,
                message.ActualFeedRateMillimetersPerSecond,
                message.CompactionForceNewtons,
                message.HeaterTemperatureCelsius,
                message.MaterialPressureBar,
                message.CycleProgressPercentage,
                message.ProcessHealthPercentage);
        }
        catch (ArgumentException exception)
        {
            throw InvalidResponse("La télémétrie reçue contient une valeur invalide.", exception);
        }
    }

    public static CommandResult ToDomain(
        this CommandResultMessage message,
        Guid expectedCorrelationId,
        DomainMachineState previousState,
        MachineSnapshot latestSnapshot)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(latestSnapshot);

        if (!Guid.TryParse(message.CorrelationId, out Guid correlationId)
            || correlationId == Guid.Empty
            || correlationId != expectedCorrelationId)
        {
            throw InvalidResponse("L’identifiant de corrélation de la réponse gRPC est invalide.");
        }

        StateTransitionResult transition;
        if (message.Accepted)
        {
            transition = StateTransitionResult.Accepted(previousState, latestSnapshot);
        }
        else
        {
            StateTransitionRejection rejection = new(
                message.RejectionReason.ToDomain(),
                string.IsNullOrWhiteSpace(message.RejectionDetail)
                    ? "Le simulateur a rejeté la commande sans détail."
                    : message.RejectionDetail);
            transition = StateTransitionResult.Rejected(latestSnapshot, rejection);
        }

        return new CommandResult(correlationId, transition);
    }

    public static SimulatedFault ToTransport(this FaultType fault) => fault switch
    {
        FaultType.HighTemperature => SimulatedFault.OverTemperature,
        FaultType.LowMaterialPressure => SimulatedFault.LowMaterialPressure,
        FaultType.UnstableCompactionForce => SimulatedFault.UnstableCompactionForce,
        FaultType.HeadPositionError => SimulatedFault.HeadPositionError,
        FaultType.CommunicationTimeout => SimulatedFault.CommunicationDrop,
        _ => throw new ArgumentOutOfRangeException(nameof(fault), fault, null),
    };

    private static TransportCommandType ToTransport(this MachineCommandType commandType) => commandType switch
    {
        MachineCommandType.LoadRecipe => TransportCommandType.LoadRecipe,
        MachineCommandType.Start => TransportCommandType.Start,
        MachineCommandType.Pause => TransportCommandType.Pause,
        MachineCommandType.Resume => TransportCommandType.Resume,
        MachineCommandType.Stop => TransportCommandType.Stop,
        MachineCommandType.Reset => TransportCommandType.Reset,
        _ => throw new MachineGatewayException(
            MachineGatewayFailureKind.CommandRejected,
            $"La commande {commandType} ne peut pas être envoyée par la session cliente."),
    };

    private static DomainMachineState ToDomain(this TransportMachineState state) => state switch
    {
        TransportMachineState.Disconnected => DomainMachineState.Disconnected,
        TransportMachineState.Connecting => DomainMachineState.Connecting,
        TransportMachineState.Ready => DomainMachineState.Ready,
        TransportMachineState.Running => DomainMachineState.Running,
        TransportMachineState.Paused => DomainMachineState.Paused,
        TransportMachineState.Faulted => DomainMachineState.Faulted,
        TransportMachineState.Completed => DomainMachineState.Completed,
        _ => throw InvalidResponse("L’état machine reçu n’est pas défini."),
    };

    private static FaultType ToDomain(this SimulatedFault fault) => fault switch
    {
        SimulatedFault.OverTemperature => FaultType.HighTemperature,
        SimulatedFault.LowMaterialPressure => FaultType.LowMaterialPressure,
        SimulatedFault.UnstableCompactionForce => FaultType.UnstableCompactionForce,
        SimulatedFault.HeadPositionError => FaultType.HeadPositionError,
        SimulatedFault.CommunicationDrop => FaultType.CommunicationTimeout,
        _ => throw InvalidResponse("Le type de défaut reçu n’est pas défini."),
    };

    private static ProductionRecipe ToDomain(this RecipeSummaryMessage recipe)
    {
        if (!Guid.TryParse(recipe.Id, out Guid recipeId) || recipeId == Guid.Empty)
        {
            throw InvalidResponse("L’identifiant de la recette reçue est invalide.");
        }

        try
        {
            ProductionRecipe mapped = new(
                recipeId,
                recipe.Name,
                recipe.PartReference,
                recipe.TargetTemperatureCelsius,
                recipe.TargetPressureBar,
                recipe.TargetFeedRateMillimetersPerSecond,
                recipe.PassCount,
                TimeSpan.FromSeconds(recipe.EstimatedDurationSeconds));

            if (!mapped.Validate().IsValid)
            {
                throw InvalidResponse("La recette reçue ne respecte pas les limites du démonstrateur.");
            }

            return mapped;
        }
        catch (ArgumentException exception)
        {
            throw InvalidResponse("La recette reçue contient une valeur invalide.", exception);
        }
    }

    private static StateTransitionRejectionCode ToDomain(this RejectionReason reason) => reason switch
    {
        RejectionReason.InvalidState => StateTransitionRejectionCode.InvalidState,
        RejectionReason.RecipeRequired => StateTransitionRejectionCode.RecipeRequired,
        RejectionReason.UnknownRecipe => StateTransitionRejectionCode.InvalidRecipe,
        RejectionReason.InvalidRecipe => StateTransitionRejectionCode.InvalidRecipe,
        RejectionReason.ProductionRunMissing => StateTransitionRejectionCode.ProductionRunMissing,
        RejectionReason.FaultRequired => StateTransitionRejectionCode.FaultRequired,
        RejectionReason.FaultNotActive => StateTransitionRejectionCode.FaultNotActive,
        RejectionReason.ActiveFaultRemains => StateTransitionRejectionCode.ActiveFaultsRemain,
        _ => StateTransitionRejectionCode.UnsupportedCommand,
    };

    private static MachineGatewayException InvalidResponse(string message, Exception? innerException = null) =>
        new(MachineGatewayFailureKind.InvalidResponse, message, innerException);
}

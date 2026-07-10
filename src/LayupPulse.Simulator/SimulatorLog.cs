using LayupPulse.Contracts.Grpc;

namespace LayupPulse.Simulator;

/// <summary>
/// Définit les événements structurés stables du processus de simulation.
/// </summary>
public static partial class SimulatorLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "LayupPulse Simulator — cellule fictive uniquement, incompatible avec du matériel réel | " +
            "Endpoint: {Endpoint} | Seed: {Seed} | TelemetryRateHz: {TelemetryRateHz}")]
    public static partial void Started(
        ILogger logger,
        string endpoint,
        int seed,
        int telemetryRateHz);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Debug,
        Message = "La publication de télémétrie s’arrête sur demande du processus.")]
    public static partial void TelemetryPublisherStopped(ILogger logger);

    [LoggerMessage(
        EventId = 1100,
        Level = LogLevel.Information,
        Message = "Le flux télémétrique est terminé après la déconnexion simulée.")]
    public static partial void TelemetryStreamDisconnected(ILogger logger);

    [LoggerMessage(
        EventId = 1101,
        Level = LogLevel.Warning,
        Message = "Le flux télémétrique est interrompu par le défaut CommunicationDrop.")]
    public static partial void TelemetryStreamInterrupted(ILogger logger);

    [LoggerMessage(
        EventId = 1102,
        Level = LogLevel.Debug,
        Message = "Le client a annulé le flux télémétrique.")]
    public static partial void TelemetryStreamCancelled(ILogger logger);

    [LoggerMessage(
        EventId = 1200,
        Level = LogLevel.Information,
        Message = "Commande simulée traitée : {CorrelationId} {Command} {Accepted} " +
            "{MachineState} {RejectionReason}")]
    public static partial void CommandProcessed(
        ILogger logger,
        string correlationId,
        CommandType command,
        bool accepted,
        MachineState machineState,
        RejectionReason rejectionReason);

    [LoggerMessage(
        EventId = 1201,
        Level = LogLevel.Warning,
        Message = "Requête de transport rejetée : {CorrelationId} {Command} {RejectionReason}")]
    public static partial void TransportRequestRejected(
        ILogger logger,
        string correlationId,
        CommandType command,
        RejectionReason rejectionReason);
}

using Grpc.Core;
using LayupPulse.Contracts.Grpc;
using LayupPulse.Domain;
using TransportCommandType = LayupPulse.Contracts.Grpc.CommandType;

namespace LayupPulse.Simulator;

/// <summary>
/// Adapte le contrat gRPC versionné vers le moteur de simulation déterministe.
/// </summary>
public sealed class MachineSimulatorGrpcService : MachineSimulator.MachineSimulatorBase
{
    private readonly DeterministicMachineSimulator _simulator;
    private readonly TelemetryStreamHub _streamHub;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MachineSimulatorGrpcService> _logger;

    public MachineSimulatorGrpcService(
        DeterministicMachineSimulator simulator,
        TelemetryStreamHub streamHub,
        TimeProvider timeProvider,
        ILogger<MachineSimulatorGrpcService> logger)
    {
        _simulator = simulator;
        _streamHub = streamHub;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public override Task<MachineSnapshotMessage> GetSnapshot(
        GetSnapshotRequest request,
        ServerCallContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_simulator.GetSnapshot().ToTransport());
    }

    public override async Task StreamTelemetry(
        StreamTelemetryRequest request,
        IServerStreamWriter<TelemetryMessage> responseStream,
        ServerCallContext context)
    {
        CancellationToken cancellationToken = context.CancellationToken;

        try
        {
            await foreach (SimulationSnapshot snapshot in _streamHub.ReadAllAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await responseStream.WriteAsync(
                    snapshot.Telemetry.ToTransport(snapshot.Machine.ActiveFaults));
            }
        }
        catch (TelemetryStreamInterruptedException exception)
            when (exception.Reason == TelemetryInterruptionReason.Disconnected)
        {
            SimulatorLog.TelemetryStreamDisconnected(_logger);
        }
        catch (TelemetryStreamInterruptedException exception)
            when (exception.Reason == TelemetryInterruptionReason.CommunicationDrop)
        {
            SimulatorLog.TelemetryStreamInterrupted(_logger);
            throw new RpcException(new Status(
                StatusCode.Unavailable,
                "Coupure de communication injectée par le simulateur fictif."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SimulatorLog.TelemetryStreamCancelled(_logger);
        }
    }

    public override Task<CommandResultMessage> ExecuteCommand(
        ExecuteCommandRequest request,
        ServerCallContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        CommandResultMessage response = ExecuteMappedCommand(
            request.CorrelationId,
            request.Command,
            () => request.ToDomain(_timeProvider.GetUtcNow()));

        if (response.Accepted && request.Command == TransportCommandType.Connect)
        {
            _streamHub.MarkConnected();
        }
        else if (response.Accepted && request.Command == TransportCommandType.Disconnect)
        {
            _streamHub.MarkDisconnected();
        }

        return Task.FromResult(response);
    }

    public override Task<CommandResultMessage> InjectFault(
        InjectFaultRequest request,
        ServerCallContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        CommandResultMessage response = ExecuteMappedCommand(
            request.CorrelationId,
            TransportCommandType.Unspecified,
            () => request.ToDomain(_timeProvider.GetUtcNow()));

        if (response.Accepted && request.Fault == SimulatedFault.CommunicationDrop)
        {
            _streamHub.InterruptCommunication();
        }

        return Task.FromResult(response);
    }

    public override Task<CommandResultMessage> ClearFault(
        ClearFaultRequest request,
        ServerCallContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        CommandResultMessage response = ExecuteMappedCommand(
            request.CorrelationId,
            TransportCommandType.Unspecified,
            () => request.ToDomain(_timeProvider.GetUtcNow()));

        if (response.Accepted && request.Fault == SimulatedFault.CommunicationDrop)
        {
            _streamHub.RestoreCommunication();
        }

        return Task.FromResult(response);
    }

    private CommandResultMessage ExecuteMappedCommand(
        string correlationId,
        TransportCommandType transportCommand,
        Func<MachineCommand> mapCommand)
    {
        try
        {
            MachineCommand command = mapCommand();
            CommandResult result = _simulator.ExecuteCommand(command);
            CommandResultMessage response = result.ToTransport(transportCommand);

            SimulatorLog.CommandProcessed(
                _logger,
                response.CorrelationId,
                transportCommand,
                response.Accepted,
                response.MachineState,
                response.RejectionReason);

            return response;
        }
        catch (TransportMappingException exception)
        {
            SimulatorLog.TransportRequestRejected(
                _logger,
                correlationId,
                transportCommand,
                exception.RejectionReason);
            return exception.ToRejectedTransport(
                correlationId,
                transportCommand,
                _simulator.State);
        }
    }
}

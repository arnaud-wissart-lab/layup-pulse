using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Grpc.Net.Client;
using LayupPulse.Application;
using LayupPulse.Contracts.Grpc;
using LayupPulse.Domain;
using Microsoft.Extensions.Logging;
using TransportCommandType = LayupPulse.Contracts.Grpc.CommandType;

namespace LayupPulse.Infrastructure;

/// <summary>
/// Implémente la session machine au moyen du contrat gRPC versionné du simulateur.
/// </summary>
public sealed class GrpcMachineGateway : IMachineGateway, IDemoFaultGateway
{
    private static readonly Action<ILogger, Guid, Uri, Exception?> SessionConnectedLog =
        LoggerMessage.Define<Guid, Uri>(
            LogLevel.Information,
            new EventId(1, nameof(SessionConnectedLog)),
            "Session gRPC {SessionId} connectée à {Endpoint}.");
    private static readonly Action<ILogger, Guid, Exception?> SessionClosedLog =
        LoggerMessage.Define<Guid>(
            LogLevel.Information,
            new EventId(2, nameof(SessionClosedLog)),
            "Session gRPC {SessionId} fermée.");
    private static readonly Action<ILogger, string, StatusCode, Exception?> TransportFailureLog =
        LoggerMessage.Define<string, StatusCode>(
            LogLevel.Warning,
            new EventId(3, nameof(TransportFailureLog)),
            "Échec gRPC pendant {OperationName} avec le statut {StatusCode}.");
    private readonly GrpcMachineGatewayOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GrpcMachineGateway> _logger;
    private readonly ConcurrentDictionary<Guid, GrpcMachineSession> _sessions = new();
    private readonly Uri _endpoint;
    private int _isDisposed;

    public GrpcMachineGateway(
        GrpcMachineGatewayOptions options,
        TimeProvider timeProvider,
        ILogger<GrpcMachineGateway> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
        _endpoint = options.GetValidatedEndpoint();
    }

    public async Task<IMachineSession> ConnectAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        GrpcChannel? channel = null;

        try
        {
            channel = GrpcChannel.ForAddress(_endpoint);
            MachineSimulator.MachineSimulatorClient client = new(channel);
            Guid correlationId = Guid.NewGuid();
            CommandResultMessage response = await ExecuteTransportAsync(
                () => client.ExecuteCommandAsync(
                    new ExecuteCommandRequest
                    {
                        CorrelationId = correlationId.ToString("D"),
                        Command = TransportCommandType.Connect,
                    },
                    deadline: CreateDeadline(),
                    cancellationToken: cancellationToken).ResponseAsync,
                "connexion",
                cancellationToken)
                .ConfigureAwait(false);

            EnsureAccepted(response, "connexion");
            DateTimeOffset connectedAt = _timeProvider.GetUtcNow();
            GrpcMachineSession session = new(Guid.NewGuid(), connectedAt, channel, client);
            if (!_sessions.TryAdd(session.SessionId, session))
            {
                throw new MachineGatewayException(
                    MachineGatewayFailureKind.Unexpected,
                    "La session gRPC locale n’a pas pu être enregistrée.");
            }

            channel = null;
            SessionConnectedLog(_logger, session.SessionId, _endpoint, null);
            return session;
        }
        finally
        {
            channel?.Dispose();
        }
    }

    public async Task DisconnectAsync(
        IMachineSession session,
        CancellationToken cancellationToken)
    {
        GrpcMachineSession grpcSession = GetSession(session);

        try
        {
            Guid correlationId = Guid.NewGuid();
            CommandResultMessage response = await ExecuteTransportAsync(
                () => grpcSession.Client.ExecuteCommandAsync(
                    new ExecuteCommandRequest
                    {
                        CorrelationId = correlationId.ToString("D"),
                        Command = TransportCommandType.Disconnect,
                    },
                    deadline: CreateDeadline(),
                    cancellationToken: cancellationToken).ResponseAsync,
                "déconnexion",
                cancellationToken)
                .ConfigureAwait(false);
            EnsureAccepted(response, "déconnexion");
        }
        finally
        {
            _sessions.TryRemove(grpcSession.SessionId, out _);
            grpcSession.Dispose();
            SessionClosedLog(_logger, grpcSession.SessionId, null);
        }
    }

    public ValueTask AbandonAsync(IMachineSession session)
    {
        GrpcMachineSession grpcSession = GetSession(session);
        _sessions.TryRemove(grpcSession.SessionId, out _);
        grpcSession.Dispose();
        SessionClosedLog(_logger, grpcSession.SessionId, null);
        return ValueTask.CompletedTask;
    }

    public async Task<CommandResult> ExecuteCommandAsync(
        IMachineSession session,
        MachineCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        GrpcMachineSession grpcSession = GetSession(session);
        MachineSnapshot before = await GetSnapshotAsync(grpcSession, cancellationToken).ConfigureAwait(false);
        CommandResultMessage response = await ExecuteTransportAsync(
            () => grpcSession.Client.ExecuteCommandAsync(
                command.ToTransport(),
                deadline: CreateDeadline(),
                cancellationToken: cancellationToken).ResponseAsync,
            $"commande {command.Type}",
            cancellationToken)
            .ConfigureAwait(false);
        MachineSnapshot after = await GetSnapshotAsync(grpcSession, cancellationToken).ConfigureAwait(false);
        return response.ToDomain(command.CorrelationId, before.State, after);
    }

    public async Task<MachineSnapshot> GetSnapshotAsync(
        IMachineSession session,
        CancellationToken cancellationToken)
    {
        GrpcMachineSession grpcSession = GetSession(session);
        MachineSnapshotMessage response = await ExecuteTransportAsync(
            () => grpcSession.Client.GetSnapshotAsync(
                new GetSnapshotRequest(),
                deadline: CreateDeadline(),
                cancellationToken: cancellationToken).ResponseAsync,
            "lecture de l’état machine",
            cancellationToken)
            .ConfigureAwait(false);
        return response.ToDomain(_timeProvider.GetUtcNow());
    }

    public async IAsyncEnumerable<TelemetrySample> StreamTelemetryAsync(
        IMachineSession session,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        GrpcMachineSession grpcSession = GetSession(session);
        using AsyncServerStreamingCall<TelemetryMessage> call = grpcSession.Client.StreamTelemetry(
            new StreamTelemetryRequest(),
            cancellationToken: cancellationToken);

        while (await MoveNextAsync(call.ResponseStream, cancellationToken).ConfigureAwait(false))
        {
            yield return call.ResponseStream.Current.ToDomain();
        }
    }

    public async Task<CommandResult> InjectFaultAsync(
        IMachineSession session,
        Guid correlationId,
        FaultType fault,
        CancellationToken cancellationToken)
    {
        ValidateCorrelationId(correlationId);
        GrpcMachineSession grpcSession = GetSession(session);
        MachineSnapshot before = await GetSnapshotAsync(grpcSession, cancellationToken).ConfigureAwait(false);
        CommandResultMessage response = await ExecuteTransportAsync(
            () => grpcSession.Client.InjectFaultAsync(
                new InjectFaultRequest
                {
                    CorrelationId = correlationId.ToString("D"),
                    Fault = fault.ToTransport(),
                },
                deadline: CreateDeadline(),
                cancellationToken: cancellationToken).ResponseAsync,
            $"injection du défaut de démonstration {fault}",
            cancellationToken)
            .ConfigureAwait(false);
        MachineSnapshot after = await GetSnapshotAsync(grpcSession, cancellationToken).ConfigureAwait(false);
        return response.ToDomain(correlationId, before.State, after);
    }

    public async Task<CommandResult> ClearFaultAsync(
        IMachineSession session,
        Guid correlationId,
        FaultType fault,
        CancellationToken cancellationToken)
    {
        ValidateCorrelationId(correlationId);
        GrpcMachineSession grpcSession = GetSession(session);
        MachineSnapshot before = await GetSnapshotAsync(grpcSession, cancellationToken).ConfigureAwait(false);
        CommandResultMessage response = await ExecuteTransportAsync(
            () => grpcSession.Client.ClearFaultAsync(
                new ClearFaultRequest
                {
                    CorrelationId = correlationId.ToString("D"),
                    Fault = fault.ToTransport(),
                },
                deadline: CreateDeadline(),
                cancellationToken: cancellationToken).ResponseAsync,
            $"levée du défaut de démonstration {fault}",
            cancellationToken)
            .ConfigureAwait(false);
        MachineSnapshot after = await GetSnapshotAsync(grpcSession, cancellationToken).ConfigureAwait(false);
        return response.ToDomain(correlationId, before.State, after);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        foreach (GrpcMachineSession session in _sessions.Values)
        {
            session.Dispose();
        }

        _sessions.Clear();
        return ValueTask.CompletedTask;
    }

    private async Task<bool> MoveNextAsync(
        IAsyncStreamReader<TelemetryMessage> responseStream,
        CancellationToken cancellationToken) =>
        await ExecuteTransportAsync(
            () => responseStream.MoveNext(cancellationToken),
            "lecture de la télémétrie",
            cancellationToken)
            .ConfigureAwait(false);

    private async Task<T> ExecuteTransportAsync<T>(
        Func<Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RpcException exception)
            when (exception.StatusCode == StatusCode.Cancelled && cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("L’appel gRPC a été annulé.", exception, cancellationToken);
        }
        catch (RpcException exception)
        {
            MachineGatewayFailureKind failureKind = exception.StatusCode switch
            {
                StatusCode.Unavailable => MachineGatewayFailureKind.Unavailable,
                StatusCode.DeadlineExceeded => MachineGatewayFailureKind.Timeout,
                StatusCode.Cancelled => MachineGatewayFailureKind.Interrupted,
                StatusCode.DataLoss => MachineGatewayFailureKind.InvalidResponse,
                _ => MachineGatewayFailureKind.Unexpected,
            };
            string message = failureKind switch
            {
                MachineGatewayFailureKind.Unavailable =>
                    $"Le simulateur est indisponible pendant l’opération « {operationName} ».",
                MachineGatewayFailureKind.Timeout =>
                    $"Le simulateur n’a pas répondu à temps pendant l’opération « {operationName} ».",
                MachineGatewayFailureKind.Interrupted =>
                    $"La communication a été interrompue pendant l’opération « {operationName} ».",
                MachineGatewayFailureKind.InvalidResponse =>
                    $"Le simulateur a envoyé une réponse invalide pendant l’opération « {operationName} ».",
                _ => $"L’opération gRPC « {operationName} » a échoué.",
            };

            TransportFailureLog(_logger, operationName, exception.StatusCode, exception);
            throw new MachineGatewayException(failureKind, message, exception);
        }
        catch (HttpRequestException exception)
        {
            throw new MachineGatewayException(
                MachineGatewayFailureKind.Unavailable,
                $"Le simulateur est inaccessible pendant l’opération « {operationName} ».",
                exception);
        }
        catch (IOException exception)
        {
            throw new MachineGatewayException(
                MachineGatewayFailureKind.Interrupted,
                $"La communication a été interrompue pendant l’opération « {operationName} ».",
                exception);
        }
    }

    private GrpcMachineSession GetSession(IMachineSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ThrowIfDisposed();

        if (session is not GrpcMachineSession grpcSession
            || grpcSession.IsDisposed
            || !_sessions.TryGetValue(grpcSession.SessionId, out GrpcMachineSession? registered)
            || !ReferenceEquals(grpcSession, registered))
        {
            throw new MachineGatewayException(
                MachineGatewayFailureKind.Interrupted,
                "La session gRPC indiquée n’est plus active.");
        }

        return grpcSession;
    }

    private static void EnsureAccepted(CommandResultMessage response, string operationName)
    {
        if (!response.Accepted)
        {
            throw new MachineGatewayException(
                MachineGatewayFailureKind.CommandRejected,
                string.IsNullOrWhiteSpace(response.RejectionDetail)
                    ? $"Le simulateur a rejeté l’opération « {operationName} »."
                    : response.RejectionDetail);
        }
    }

    private DateTime CreateDeadline() => DateTime.UtcNow.Add(_options.RequestTimeout);

    private static void ValidateCorrelationId(Guid correlationId)
    {
        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException(
                "L’identifiant de corrélation ne peut pas être vide.",
                nameof(correlationId));
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
    }
}

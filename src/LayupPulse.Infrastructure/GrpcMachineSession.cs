using Grpc.Net.Client;
using LayupPulse.Application;
using LayupPulse.Contracts.Grpc;

namespace LayupPulse.Infrastructure;

internal sealed class GrpcMachineSession : IMachineSession, IDisposable
{
    private int _isDisposed;

    public GrpcMachineSession(
        Guid sessionId,
        DateTimeOffset connectedAt,
        GrpcChannel channel,
        MachineSimulator.MachineSimulatorClient client)
    {
        SessionId = sessionId;
        ConnectedAt = connectedAt;
        Channel = channel;
        Client = client;
    }

    public Guid SessionId { get; }

    public DateTimeOffset ConnectedAt { get; }

    public GrpcChannel Channel { get; }

    public MachineSimulator.MachineSimulatorClient Client { get; }

    public bool IsDisposed => Volatile.Read(ref _isDisposed) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
        {
            Channel.Dispose();
        }
    }
}

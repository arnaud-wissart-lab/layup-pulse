using LayupPulse.Domain;

namespace LayupPulse.Application;

/// <summary>
/// Définit les échanges asynchrones avec une machine sans exposer de technologie de transport.
/// </summary>
public interface IMachineGateway : IAsyncDisposable
{
    public Task<IMachineSession> ConnectAsync(CancellationToken cancellationToken);

    public Task DisconnectAsync(IMachineSession session, CancellationToken cancellationToken);

    public Task<CommandResult> ExecuteCommandAsync(
        IMachineSession session,
        MachineCommand command,
        CancellationToken cancellationToken);

    public Task<MachineSnapshot> GetSnapshotAsync(
        IMachineSession session,
        CancellationToken cancellationToken);

    public IAsyncEnumerable<TelemetrySample> StreamTelemetryAsync(
        IMachineSession session,
        CancellationToken cancellationToken);
}

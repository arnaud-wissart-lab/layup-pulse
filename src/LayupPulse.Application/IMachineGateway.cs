using LayupPulse.Domain;

namespace LayupPulse.Application;

/// <summary>
/// Définit les échanges asynchrones avec une machine sans exposer de technologie de transport.
/// </summary>
public interface IMachineGateway : IAsyncDisposable
{
    /// <summary>
    /// Ouvre et vérifie une session de transport locale sans modifier le cycle de vie de la machine.
    /// </summary>
    public Task<MachineTransportAttachment> AttachAsync(CancellationToken cancellationToken);

    public Task DisconnectAsync(IMachineSession session, CancellationToken cancellationToken);

    /// <summary>
    /// Libère une session cliente devenue inutilisable sans envoyer de commande à la machine distante.
    /// </summary>
    public ValueTask AbandonAsync(IMachineSession session);

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

namespace LayupPulse.Application;

/// <summary>
/// Observe la session et sérialise les écritures durables sans exposer la persistance à WPF.
/// </summary>
public interface IProductionRunRecordingService : IAsyncDisposable
{
    public void Observe(MachineSessionState state);

    public Task FlushAsync(CancellationToken cancellationToken);
}

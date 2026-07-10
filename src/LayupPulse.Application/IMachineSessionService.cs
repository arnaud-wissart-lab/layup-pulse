using LayupPulse.Domain;

namespace LayupPulse.Application;

/// <summary>
/// Orchestre une unique session machine pour l’application de bureau.
/// </summary>
public interface IMachineSessionService : IAsyncDisposable
{
    public event EventHandler<MachineSessionStateChangedEventArgs>? StateChanged;

    public MachineSessionState State { get; }

    public Task<MachineSessionOperationResult> ConnectAsync(CancellationToken cancellationToken);

    public Task<MachineSessionOperationResult> DisconnectAsync(CancellationToken cancellationToken);

    public Task<MachineCommandExecutionResult> ExecuteCommandAsync(
        MachineCommand command,
        CancellationToken cancellationToken);

    public Task<MachineSessionOperationResult> SetDemoFaultAsync(
        FaultType fault,
        bool active,
        CancellationToken cancellationToken);

    public bool AcknowledgeAlarm(Guid alarmId);
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LayupPulse.Application;
using LayupPulse.Domain;

namespace LayupPulse.Desktop;

public sealed class SimulationFaultControlViewModel : ObservableObject
{
    private readonly IMachineSessionService _sessionService;
    private readonly Action<string, bool> _reportResult;
    private bool _isActive;
    private bool _isAvailable;

    public SimulationFaultControlViewModel(
        string name,
        FaultType fault,
        IMachineSessionService sessionService,
        Action<string, bool> reportResult)
    {
        Name = name;
        Fault = fault;
        _sessionService = sessionService;
        _reportResult = reportResult;
        InjectCommand = new AsyncRelayCommand(
            cancellationToken => SetFaultAsync(active: true, cancellationToken),
            () => IsAvailable && !IsActive);
        ClearCommand = new AsyncRelayCommand(
            cancellationToken => SetFaultAsync(active: false, cancellationToken),
            () => IsAvailable && IsActive);
    }

    public string Name { get; }

    public FaultType Fault { get; }

    public IAsyncRelayCommand InjectCommand { get; }

    public IAsyncRelayCommand ClearCommand { get; }

    public bool IsActive
    {
        get => _isActive;
        private set => SetProperty(ref _isActive, value);
    }

    public bool IsAvailable
    {
        get => _isAvailable;
        private set => SetProperty(ref _isAvailable, value);
    }

    public string StateText => IsActive ? "Actif" : "Inactif";

    public void ApplyState(MachineSessionState state)
    {
        IsActive = state.LatestSnapshot.ActiveFaults.Contains(Fault);
        IsAvailable = state.ConnectionStatus is
            MachineConnectionStatus.Connected
            or MachineConnectionStatus.Stale
            or MachineConnectionStatus.Reconnecting;
        OnPropertyChanged(nameof(StateText));
        InjectCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }

    private async Task SetFaultAsync(bool active, CancellationToken cancellationToken)
    {
        try
        {
            MachineSessionOperationResult result = await _sessionService
                .SetDemoFaultAsync(Fault, active, cancellationToken);
            _reportResult(result.Message, !result.IsSuccessful);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _reportResult("Modification du défaut simulé annulée.", false);
        }
    }
}

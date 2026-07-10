using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LayupPulse.Application;
using LayupPulse.Domain;

namespace LayupPulse.Desktop;

public sealed class AlarmsViewModel : ObservableObject
{
    private readonly IMachineSessionService _sessionService;
    private MachineSessionState _state;
    private AlarmRowViewModel? _selectedAlarm;
    private string _selectedSeverity = "Toutes";
    private bool _showHistory;
    private int _activeAlarmCount;
    private bool _hasVisibleAlarms;
    private string _emptyStateMessage = "Aucune alarme active.";
    private string? _collectionIdentity;

    public AlarmsViewModel(IMachineSessionService sessionService)
    {
        _sessionService = sessionService;
        _state = sessionService.State;
        AcknowledgeCommand = new RelayCommand(AcknowledgeSelected, CanAcknowledgeSelected);
        ApplyState(_state);
    }

    public IReadOnlyList<string> SeverityFilters { get; } =
        ["Toutes", "Critique", "Avertissement", "Information"];

    public ObservableCollection<AlarmRowViewModel> VisibleAlarms { get; } = new();

    public IRelayCommand AcknowledgeCommand { get; }

    public AlarmRowViewModel? SelectedAlarm
    {
        get => _selectedAlarm;
        set
        {
            if (SetProperty(ref _selectedAlarm, value))
            {
                AcknowledgeCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string SelectedSeverity
    {
        get => _selectedSeverity;
        set
        {
            if (SetProperty(ref _selectedSeverity, value))
            {
                RefreshCollection(force: true);
            }
        }
    }

    public bool ShowHistory
    {
        get => _showHistory;
        set
        {
            if (SetProperty(ref _showHistory, value))
            {
                RefreshCollection(force: true);
                OnPropertyChanged(nameof(ViewTitle));
            }
        }
    }

    public string ViewTitle => ShowHistory ? "Historique en mémoire" : "Alarmes actives";

    public int ActiveAlarmCount
    {
        get => _activeAlarmCount;
        private set => SetProperty(ref _activeAlarmCount, value);
    }

    public bool HasVisibleAlarms
    {
        get => _hasVisibleAlarms;
        private set => SetProperty(ref _hasVisibleAlarms, value);
    }

    public string EmptyStateMessage
    {
        get => _emptyStateMessage;
        private set => SetProperty(ref _emptyStateMessage, value);
    }

    public void ApplyState(MachineSessionState state)
    {
        _state = state;
        ActiveAlarmCount = state.ActiveAlarms.Count;
        RefreshCollection(force: false);
    }

    private void RefreshCollection(bool force)
    {
        IReadOnlyList<AlarmEvent> source = ShowHistory ? _state.AlarmHistory : _state.ActiveAlarms;
        IEnumerable<AlarmRowViewModel> filtered = source
            .Select(static alarm => new AlarmRowViewModel(alarm));
        if (SelectedSeverity != "Toutes")
        {
            filtered = filtered.Where(alarm => alarm.Severity == SelectedSeverity);
        }

        AlarmRowViewModel[] rows = filtered.ToArray();
        string identity = string.Join(
            '|',
            rows.Select(static row => $"{row.Id:D}:{row.Acknowledged}:{row.Cleared}"));
        identity = $"{ShowHistory}:{SelectedSeverity}:{identity}";
        if (!force && identity == _collectionIdentity)
        {
            return;
        }

        _collectionIdentity = identity;
        SelectedAlarm = null;
        VisibleAlarms.Clear();
        foreach (AlarmRowViewModel row in rows)
        {
            VisibleAlarms.Add(row);
        }

        HasVisibleAlarms = VisibleAlarms.Count > 0;
        EmptyStateMessage = source.Count == 0
            ? ShowHistory
                ? "Aucune alarme historique en mémoire."
                : "Aucune alarme active."
            : "Aucune alarme ne correspond au filtre de sévérité.";
        AcknowledgeCommand.NotifyCanExecuteChanged();
    }

    private bool CanAcknowledgeSelected() => SelectedAlarm is
    {
        IsActive: true,
        IsAcknowledged: false,
    };

    private void AcknowledgeSelected()
    {
        if (SelectedAlarm is not null)
        {
            _sessionService.AcknowledgeAlarm(SelectedAlarm.Id);
        }
    }
}

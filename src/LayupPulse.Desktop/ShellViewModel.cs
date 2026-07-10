using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LayupPulse.Application;

namespace LayupPulse.Desktop;

public sealed class ShellViewModel : ObservableObject, IDisposable
{
    private readonly IMachineSessionService _sessionService;
    private readonly IUiDispatcher _dispatcher;
    private NavigationItemViewModel? _selectedNavigation;
    private object? _currentPage;
    private string _connectionStatus = string.Empty;
    private string _connectionTone = "Neutral";
    private string _connectionGlyph = "○";
    private string _machineState = string.Empty;
    private string _machineTone = "Neutral";
    private string _machineGlyph = "○";
    private string _loadedRecipe = string.Empty;
    private string _headerProgress = "0,0 %";
    private string _currentLocalTime = string.Empty;
    private string _telemetryAge = "Aucune donnée";
    private MachineSessionState _sessionState;
    private int _isDisposed;

    public ShellViewModel(
        IMachineSessionService sessionService,
        IUiDispatcher dispatcher,
        OverviewViewModel overview,
        DiagnosticsViewModel diagnostics,
        AlarmsViewModel alarms)
    {
        _sessionService = sessionService;
        _sessionState = sessionService.State;
        _dispatcher = dispatcher;
        Overview = overview;
        Diagnostics = diagnostics;
        Alarms = alarms;

        NavigationItems =
        [
            new NavigationItemViewModel("Vue d’ensemble", "◉", overview),
            new NavigationItemViewModel(
                "Alarmes",
                "!",
                alarms),
            new NavigationItemViewModel(
                "Historique",
                "▤",
                new PlaceholderPageViewModel(
                    "Historique",
                    "La persistance et l’historique ne sont pas implémentés dans cet incrément.",
                    "▤")),
            new NavigationItemViewModel("Diagnostics", "⌁", diagnostics),
        ];

        _sessionService.StateChanged += OnSessionStateChanged;
        SelectedNavigation = NavigationItems[0];
        ApplyState(_sessionService.State);
        RefreshClock();
    }

    public IReadOnlyList<NavigationItemViewModel> NavigationItems { get; }

    public OverviewViewModel Overview { get; }

    public DiagnosticsViewModel Diagnostics { get; }

    public AlarmsViewModel Alarms { get; }

    public NavigationItemViewModel? SelectedNavigation
    {
        get => _selectedNavigation;
        set
        {
            if (SetProperty(ref _selectedNavigation, value) && value is not null)
            {
                CurrentPage = value.Page;
            }
        }
    }

    public object? CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        private set => SetProperty(ref _connectionStatus, value);
    }

    public string ConnectionTone
    {
        get => _connectionTone;
        private set => SetProperty(ref _connectionTone, value);
    }

    public string ConnectionGlyph
    {
        get => _connectionGlyph;
        private set => SetProperty(ref _connectionGlyph, value);
    }

    public string MachineState
    {
        get => _machineState;
        private set => SetProperty(ref _machineState, value);
    }

    public string MachineTone
    {
        get => _machineTone;
        private set => SetProperty(ref _machineTone, value);
    }

    public string MachineGlyph
    {
        get => _machineGlyph;
        private set => SetProperty(ref _machineGlyph, value);
    }

    public string LoadedRecipe
    {
        get => _loadedRecipe;
        private set => SetProperty(ref _loadedRecipe, value);
    }

    public string HeaderProgress
    {
        get => _headerProgress;
        private set => SetProperty(ref _headerProgress, value);
    }

    public string CurrentLocalTime
    {
        get => _currentLocalTime;
        private set => SetProperty(ref _currentLocalTime, value);
    }

    public string TelemetryAge
    {
        get => _telemetryAge;
        private set => SetProperty(ref _telemetryAge, value);
    }

    public void RefreshClock()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        CurrentLocalTime = now.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.CurrentCulture);
        TelemetryAge = _sessionState.LastSuccessfulCommunication is null
            ? "Aucune donnée"
            : $"{Math.Max(0, (now - _sessionState.LastSuccessfulCommunication.Value.ToLocalTime()).TotalSeconds):F1} s";
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
        {
            _sessionService.StateChanged -= OnSessionStateChanged;
        }
    }

    private void OnSessionStateChanged(object? sender, MachineSessionStateChangedEventArgs eventArgs)
    {
        if (Volatile.Read(ref _isDisposed) != 0)
        {
            return;
        }

        _dispatcher.Post(() => ApplyState(eventArgs.State));
    }

    private void ApplyState(MachineSessionState state)
    {
        _sessionState = state;
        ConnectionStatus = MachineDisplayText.ConnectionStatus(state.ConnectionStatus);
        ConnectionTone = MachineDisplayText.ConnectionTone(state.ConnectionStatus);
        ConnectionGlyph = MachineDisplayText.ConnectionGlyph(state.ConnectionStatus);
        MachineState = MachineDisplayText.MachineState(state.LatestSnapshot.State);
        MachineTone = MachineDisplayText.MachineTone(state.LatestSnapshot.State);
        MachineGlyph = MachineDisplayText.MachineGlyph(state.LatestSnapshot.State);
        LoadedRecipe = state.LatestSnapshot.LoadedRecipe?.Name ?? "Aucune recette";
        HeaderProgress = state.LatestTelemetry is null
            ? "0,0 %"
            : $"{state.LatestTelemetry.CycleProgressPercentage:F1} %";
        RefreshClock();
        Overview.ApplyState(state);
        Diagnostics.ApplyState(state);
        Alarms.ApplyState(state);
    }
}

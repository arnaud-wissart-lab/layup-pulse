using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LayupPulse.Application;
using LayupPulse.Desktop.Reporting;
using LayupPulse.Domain;

namespace LayupPulse.Desktop;

public sealed class HistoryViewModel : ObservableObject
{
    private const int MaximumRunCount = 200;
    private const int MaximumAlarmCount = 500;
    private const int MaximumAggregateCount = 3_600;

    private readonly IHistoryQueryService _historyQuery;
    private readonly IProductionRunReportPresenter _reportPresenter;
    private HistoryStatusFilter _selectedStatusFilter;
    private HistoryRunRowViewModel? _selectedRun;
    private ProductionRunHistoryDetails? _selectedRunDetails;
    private bool _isLoading;
    private bool _hasRuns;
    private bool _hasSelection;
    private string _statusMessage = "Chargement de l’historique local…";
    private int _refreshRequestGeneration;
    private int _detailsRequestGeneration;

    public HistoryViewModel(
        IHistoryQueryService historyQuery,
        IProductionRunReportPresenter reportPresenter)
    {
        _historyQuery = historyQuery;
        _reportPresenter = reportPresenter;
        StatusFilters =
        [
            new HistoryStatusFilter("Tous les états", null),
            new HistoryStatusFilter("Terminés", ProductionRunStatus.Completed),
            new HistoryStatusFilter("Interrompus", ProductionRunStatus.Aborted),
            new HistoryStatusFilter("En défaut", ProductionRunStatus.Faulted),
            new HistoryStatusFilter("En cours", ProductionRunStatus.Running),
        ];
        _selectedStatusFilter = StatusFilters[0];
        RefreshCommand = new AsyncRelayCommand(
            RefreshAsync,
            AsyncRelayCommandOptions.AllowConcurrentExecutions);
        LoadDetailsCommand = new AsyncRelayCommand(
            LoadDetailsAsync,
            AsyncRelayCommandOptions.AllowConcurrentExecutions);
        ShowReportCommand = new RelayCommand(ShowReport, CanShowReport);
        RefreshCommand.Execute(null);
    }

    public IReadOnlyList<HistoryStatusFilter> StatusFilters { get; }

    public ObservableCollection<HistoryRunRowViewModel> Runs { get; } = [];

    public ObservableCollection<HistoryAlarmRowViewModel> SelectedRunAlarms { get; } = [];

    public ObservableCollection<HistoryTelemetryRowViewModel> SelectedRunTelemetry { get; } = [];

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand LoadDetailsCommand { get; }

    public IRelayCommand ShowReportCommand { get; }

    public HistoryStatusFilter SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set
        {
            if (SetProperty(ref _selectedStatusFilter, value))
            {
                RefreshCommand.Cancel();
                RefreshCommand.Execute(null);
            }
        }
    }

    public HistoryRunRowViewModel? SelectedRun
    {
        get => _selectedRun;
        set
        {
            if (SetProperty(ref _selectedRun, value))
            {
                HasSelection = value is not null;
                ClearSelectedRunDetails();
                LoadDetailsCommand.Cancel();
                LoadDetailsCommand.Execute(null);
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool HasRuns
    {
        get => _hasRuns;
        private set => SetProperty(ref _hasRuns, value);
    }

    public bool HasSelection
    {
        get => _hasSelection;
        private set => SetProperty(ref _hasSelection, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        int generation = Interlocked.Increment(ref _refreshRequestGeneration);
        ProductionRunStatus? status = SelectedStatusFilter.Status;
        Interlocked.Increment(ref _detailsRequestGeneration);
        ClearSelectedRunDetails();
        LoadDetailsCommand.Cancel();
        IsLoading = true;
        StatusMessage = "Chargement de l’historique local…";

        try
        {
            IReadOnlyList<ProductionRunHistoryItem> runs = await _historyQuery.GetRecentRunsAsync(
                status,
                MaximumRunCount,
                cancellationToken);
            if (!IsLatestRefresh(generation))
            {
                return;
            }

            Runs.Clear();
            foreach (ProductionRunHistoryItem run in runs)
            {
                Runs.Add(new HistoryRunRowViewModel(run));
            }

            HasRuns = Runs.Count > 0;
            StatusMessage = HasRuns
                ? $"{Runs.Count} exécution(s) récente(s), de la plus récente à la plus ancienne."
                : "Aucune exécution persistée ne correspond au filtre.";
            SelectedRun = Runs.FirstOrDefault();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Une nouvelle requête de filtre ou de sélection remplace la précédente.
        }
        catch (Exception exception)
        {
            if (!IsLatestRefresh(generation))
            {
                return;
            }

            Runs.Clear();
            SelectedRun = null;
            HasRuns = false;
            StatusMessage = $"Historique local indisponible : {exception.Message}";
        }
        finally
        {
            if (IsLatestRefresh(generation))
            {
                IsLoading = false;
            }
        }
    }

    private async Task LoadDetailsAsync(CancellationToken cancellationToken)
    {
        int generation = Interlocked.Increment(ref _detailsRequestGeneration);
        HistoryRunRowViewModel? selectedRun = SelectedRun;
        ClearSelectedRunDetails();
        if (selectedRun is null)
        {
            return;
        }

        try
        {
            ProductionRunHistoryDetails? details = await _historyQuery.GetRunDetailsAsync(
                selectedRun.Id,
                MaximumAlarmCount,
                MaximumAggregateCount,
                cancellationToken);
            if (!IsLatestDetailsRequest(generation)
                || details is null
                || details.Run.Id != selectedRun.Id
                || SelectedRun?.Id != selectedRun.Id)
            {
                return;
            }

            _selectedRunDetails = details;
            foreach (AlarmHistoryItem alarm in details.Alarms)
            {
                SelectedRunAlarms.Add(new HistoryAlarmRowViewModel(alarm));
            }

            foreach (TelemetryAggregateHistoryItem aggregate in details.TelemetryAggregates)
            {
                SelectedRunTelemetry.Add(new HistoryTelemetryRowViewModel(aggregate));
            }

            ShowReportCommand.NotifyCanExecuteChanged();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Une autre sélection remplace la requête en cours.
        }
        catch (Exception exception)
        {
            if (IsLatestDetailsRequest(generation))
            {
                StatusMessage = $"Détails d’historique indisponibles : {exception.Message}";
            }
        }
    }

    private bool IsLatestRefresh(int generation) =>
        generation == Volatile.Read(ref _refreshRequestGeneration);

    private bool IsLatestDetailsRequest(int generation) =>
        generation == Volatile.Read(ref _detailsRequestGeneration);

    private bool CanShowReport() =>
        _selectedRunDetails is not null
        && SelectedRun?.Id == _selectedRunDetails.Run.Id;

    private void ShowReport()
    {
        ProductionRunHistoryDetails? details = _selectedRunDetails;
        if (details is null || SelectedRun?.Id != details.Run.Id)
        {
            return;
        }

        _reportPresenter.Show(details);
    }

    private void ClearSelectedRunDetails()
    {
        _selectedRunDetails = null;
        SelectedRunAlarms.Clear();
        SelectedRunTelemetry.Clear();
        ShowReportCommand.NotifyCanExecuteChanged();
    }
}

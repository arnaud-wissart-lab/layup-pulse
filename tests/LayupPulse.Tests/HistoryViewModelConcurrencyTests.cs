using System.Threading.Channels;
using LayupPulse.Application;
using LayupPulse.Desktop;
using LayupPulse.Domain;
using Xunit;

namespace LayupPulse.Tests;

public sealed class HistoryViewModelConcurrencyTests
{
    private static readonly DateTimeOffset Timestamp =
        new(2026, 7, 11, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OlderFilterRequestCannotEndLoadingWhileLatestRequestIsActive()
    {
        DelayedHistoryQuery query = new();
        HistoryViewModel viewModel = new(query);
        await CompleteInitialRefreshAsync(query, viewModel, CreateRun("Initial", ProductionRunStatus.Completed));

        viewModel.SelectedStatusFilter = GetFilter(viewModel, ProductionRunStatus.Completed);
        RunRequest older = await query.NextRunRequestAsync();
        viewModel.SelectedStatusFilter = GetFilter(viewModel, ProductionRunStatus.Faulted);
        RunRequest latest = await query.NextRunRequestAsync();

        older.Complete([CreateRun("Ancien", ProductionRunStatus.Completed)]);
        await older.Finished.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(viewModel.IsLoading);
        Assert.Equal("Initial", Assert.Single(viewModel.Runs).RecipeName);

        latest.Complete([CreateRun("Récent", ProductionRunStatus.Faulted)]);
        await latest.Finished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => !viewModel.IsLoading, TimeSpan.FromSeconds(2));

        Assert.Equal("Récent", Assert.Single(viewModel.Runs).RecipeName);
    }

    [Fact]
    public async Task OlderFilterRequestCannotReplaceLatestRunsOrSelection()
    {
        DelayedHistoryQuery query = new();
        HistoryViewModel viewModel = new(query);
        await CompleteInitialRefreshAsync(query, viewModel, CreateRun("Initial", ProductionRunStatus.Completed));

        viewModel.SelectedStatusFilter = GetFilter(viewModel, ProductionRunStatus.Completed);
        RunRequest older = await query.NextRunRequestAsync();
        viewModel.SelectedStatusFilter = GetFilter(viewModel, ProductionRunStatus.Faulted);
        RunRequest latest = await query.NextRunRequestAsync();
        ProductionRunHistoryItem latestRun = CreateRun("Récent", ProductionRunStatus.Faulted);

        latest.Complete([latestRun]);
        await latest.Finished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(
            () => viewModel.SelectedRun?.Id == latestRun.Id,
            TimeSpan.FromSeconds(2));
        older.Complete([CreateRun("Ancien", ProductionRunStatus.Completed)]);
        await older.Finished.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(latestRun.Id, Assert.Single(viewModel.Runs).Id);
        Assert.Equal(latestRun.Id, viewModel.SelectedRun?.Id);
        Assert.False(viewModel.IsLoading);
    }

    [Fact]
    public async Task OlderSelectionRequestCannotReplaceLatestDetails()
    {
        DelayedHistoryQuery query = new();
        HistoryViewModel viewModel = new(query);
        ProductionRunHistoryItem first = CreateRun("Premier", ProductionRunStatus.Completed);
        ProductionRunHistoryItem second = CreateRun("Second", ProductionRunStatus.Faulted);
        RunRequest initial = await query.NextRunRequestAsync();
        initial.Complete([first, second]);
        await initial.Finished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        DetailRequest initialDetails = await query.NextDetailRequestAsync();
        initialDetails.Complete(CreateDetails(first, "initial"));
        await initialDetails.Finished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(
            () => viewModel.SelectedRunAlarms.Any(alarm => alarm.Message == "initial"),
            TimeSpan.FromSeconds(2));

        viewModel.SelectedRun = viewModel.Runs.Single(run => run.Id == second.Id);
        DetailRequest older = await query.NextDetailRequestAsync();
        viewModel.SelectedRun = viewModel.Runs.Single(run => run.Id == first.Id);
        DetailRequest latest = await query.NextDetailRequestAsync();

        latest.Complete(CreateDetails(first, "récent"));
        await latest.Finished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(
            () => viewModel.SelectedRunAlarms.Any(alarm => alarm.Message == "récent"),
            TimeSpan.FromSeconds(2));
        older.Complete(CreateDetails(second, "ancien"));
        await older.Finished.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(first.Id, viewModel.SelectedRun?.Id);
        Assert.Equal("récent", Assert.Single(viewModel.SelectedRunAlarms).Message);
        Assert.Equal(20, Assert.Single(viewModel.SelectedRunTelemetry).SampleCount);
    }

    private static async Task CompleteInitialRefreshAsync(
        DelayedHistoryQuery query,
        HistoryViewModel viewModel,
        ProductionRunHistoryItem run)
    {
        RunRequest request = await query.NextRunRequestAsync();
        request.Complete([run]);
        await request.Finished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => !viewModel.IsLoading, TimeSpan.FromSeconds(2));
    }

    private static HistoryStatusFilter GetFilter(
        HistoryViewModel viewModel,
        ProductionRunStatus status) =>
        Assert.Single(viewModel.StatusFilters, filter => filter.Status == status);

    private static ProductionRunHistoryItem CreateRun(
        string recipeName,
        ProductionRunStatus status) =>
        new(
            Guid.NewGuid(),
            recipeName,
            "REF-TEST",
            Timestamp,
            Timestamp.AddMinutes(1),
            status,
            100,
            1,
            145,
            6,
            450,
            118,
            90);

    private static ProductionRunHistoryDetails CreateDetails(
        ProductionRunHistoryItem run,
        string alarmMessage) =>
        new(
            run,
            [
                new AlarmHistoryItem(
                    Guid.NewGuid(),
                    AlarmCode.HighTemperature,
                    AlarmSeverity.Critical,
                    AlarmEngine.TemperatureSource,
                    alarmMessage,
                    Timestamp,
                    null,
                    null),
            ],
            [
                new TelemetryAggregateHistoryItem(
                    Timestamp,
                    20,
                    145,
                    144,
                    146,
                    6,
                    5.9,
                    6.1,
                    450,
                    445,
                    455,
                    118,
                    95,
                    90,
                    10),
            ]);

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        using CancellationTokenSource cancellation = new(timeout);
        while (!predicate())
        {
            await Task.Delay(10, cancellation.Token);
        }
    }

    private sealed class DelayedHistoryQuery : IHistoryQueryService
    {
        private readonly Channel<RunRequest> _runRequests =
            Channel.CreateBounded<RunRequest>(10);
        private readonly Channel<DetailRequest> _detailRequests =
            Channel.CreateBounded<DetailRequest>(10);

        public async Task<IReadOnlyList<ProductionRunHistoryItem>> GetRecentRunsAsync(
            ProductionRunStatus? status,
            int maximumCount,
            CancellationToken cancellationToken)
        {
            RunRequest request = new(status);
            await _runRequests.Writer.WriteAsync(request, CancellationToken.None);
            try
            {
                return await request.Result.Task;
            }
            finally
            {
                request.Finished.TrySetResult();
            }
        }

        public async Task<ProductionRunHistoryDetails?> GetRunDetailsAsync(
            Guid productionRunId,
            int maximumAlarmCount,
            int maximumAggregateCount,
            CancellationToken cancellationToken)
        {
            DetailRequest request = new(productionRunId);
            await _detailRequests.Writer.WriteAsync(request, CancellationToken.None);
            try
            {
                return await request.Result.Task;
            }
            finally
            {
                request.Finished.TrySetResult();
            }
        }

        public Task<RunRequest> NextRunRequestAsync() =>
            _runRequests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        public Task<DetailRequest> NextDetailRequestAsync() =>
            _detailRequests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed record RunRequest(ProductionRunStatus? Status)
    {
        public TaskCompletionSource<IReadOnlyList<ProductionRunHistoryItem>> Result { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Finished { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete(IReadOnlyList<ProductionRunHistoryItem> runs) => Result.TrySetResult(runs);
    }

    private sealed record DetailRequest(Guid ProductionRunId)
    {
        public TaskCompletionSource<ProductionRunHistoryDetails?> Result { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Finished { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete(ProductionRunHistoryDetails details) => Result.TrySetResult(details);
    }
}

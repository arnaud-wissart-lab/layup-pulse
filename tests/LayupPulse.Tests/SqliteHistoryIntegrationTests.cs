using System.IO;
using LayupPulse.Application;
using LayupPulse.Domain;
using LayupPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LayupPulse.Tests;

public sealed class SqliteHistoryIntegrationTests
{
    private static readonly DateTimeOffset StartedAt =
        new(2026, 7, 11, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HistorySurvivesStoreShutdownAndAContextReopen()
    {
        string directory = Path.Combine(Path.GetTempPath(), "LayupPulse.Tests", Guid.NewGuid().ToString("N"));
        string databasePath = Path.Combine(directory, "history.db");
        Directory.CreateDirectory(directory);

        try
        {
            TestHistoryDbContextFactory firstFactory = new(databasePath);
            SqliteHistoryStore firstStore = new(
                firstFactory,
                new HistoryStorageOptions(databasePath),
                TimeProvider.System,
                NullLogger<SqliteHistoryStore>.Instance);
            await firstStore.StartAsync(CancellationToken.None);

            Guid runId = Guid.NewGuid();
            ProductionRun running = new(
                runId,
                BuiltInRecipes.WingPanelDemo,
                ProductionRunStatus.Running,
                StartedAt);
            Assert.True(firstStore.TryRecordProductionRun(running));
            Assert.True(firstStore.TryRecordProductionRun(running));

            TelemetryAggregate aggregate = new(
                Guid.NewGuid(),
                runId,
                StartedAt,
                StartedAt.AddSeconds(1),
                20,
                1,
                20,
                118,
                450,
                145,
                144,
                146,
                6,
                5.9,
                6.1,
                445,
                455,
                97,
                94,
                25);
            Assert.True(firstStore.TryRecordTelemetryAggregate(aggregate));

            Guid alarmId = Guid.NewGuid();
            AlarmEvent alarm = new AlarmEvent(
                alarmId,
                AlarmCode.HighTemperature,
                AlarmSeverity.Critical,
                AlarmEngine.TemperatureSource,
                "Température simulée élevée.",
                StartedAt.AddMilliseconds(500),
                productionRunId: runId)
                .Acknowledge(StartedAt.AddSeconds(1))
                .Clear(StartedAt.AddSeconds(2));
            Assert.True(firstStore.TryRecordAlarm(alarm));

            ProductionRun completed = new(
                runId,
                BuiltInRecipes.WingPanelDemo,
                ProductionRunStatus.Completed,
                StartedAt,
                StartedAt.AddSeconds(2),
                MachineState.Completed,
                100,
                averageTemperatureCelsius: 145,
                averagePressureBar: 6,
                averageCompactionForceNewtons: 450,
                averageFeedRateMillimetersPerSecond: 118,
                minimumProcessHealthPercentage: 94,
                alarmCount: 1);
            Assert.True(firstStore.TryRecordProductionRun(completed));
            Guid laterAbortedRunId = Guid.NewGuid();
            Assert.True(firstStore.TryRecordProductionRun(new ProductionRun(
                laterAbortedRunId,
                BuiltInRecipes.WingPanelDemo,
                ProductionRunStatus.Aborted,
                StartedAt.AddMinutes(10),
                StartedAt.AddMinutes(11),
                MachineState.Ready,
                40)));
            await firstStore.StopAsync(CancellationToken.None);

            TestHistoryDbContextFactory reopenedFactory = new(databasePath);
            await using (HistoryDbContext reopenedContext = reopenedFactory.CreateDbContext())
            {
                Assert.Single(await reopenedContext.Database.GetAppliedMigrationsAsync());
                Assert.Equal(2, await reopenedContext.ProductionRuns.CountAsync());
                Assert.Equal(1, await reopenedContext.TelemetryAggregates.CountAsync());
                Assert.Equal(1, await reopenedContext.Alarms.CountAsync());
            }

            SqliteHistoryQueryService query = new(
                reopenedFactory,
                NullLogger<SqliteHistoryQueryService>.Instance);
            ProductionRunHistoryItem item = Assert.Single(
                await query.GetRecentRunsAsync(
                    ProductionRunStatus.Completed,
                    20,
                    CancellationToken.None));
            Assert.Equal(runId, item.Id);
            Assert.Equal(100, item.CompletionPercentage);
            Assert.Equal(1, item.AlarmCount);
            Assert.Equal(94, item.MinimumProcessHealthPercentage);

            ProductionRunHistoryItem newest = Assert.Single(
                await query.GetRecentRunsAsync(null, 1, CancellationToken.None));
            Assert.Equal(laterAbortedRunId, newest.Id);
            Assert.Equal(ProductionRunStatus.Aborted, newest.Status);

            ProductionRunHistoryDetails details = Assert.IsType<ProductionRunHistoryDetails>(
                await query.GetRunDetailsAsync(runId, 50, 100, CancellationToken.None));
            Assert.Single(details.Alarms);
            Assert.Single(details.TelemetryAggregates);
            Assert.NotNull(details.Alarms[0].AcknowledgedAt);
            Assert.NotNull(details.Alarms[0].ClearedAt);
            Assert.Equal(20, details.TelemetryAggregates[0].SampleCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DatabaseInitializationFailureRemainsNonFatalAndObservable()
    {
        string directory = Path.Combine(Path.GetTempPath(), "LayupPulse.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            TestHistoryDbContextFactory factory = new(directory);
            SqliteHistoryStore store = new(
                factory,
                new HistoryStorageOptions(directory),
                TimeProvider.System,
                NullLogger<SqliteHistoryStore>.Instance);

            await store.StartAsync(CancellationToken.None);

            Assert.NotNull(store.LastDiagnosticMessage);
            Assert.False(store.TryRecordProductionRun(new ProductionRun(
                Guid.NewGuid(),
                BuiltInRecipes.WingPanelDemo,
                ProductionRunStatus.Running,
                StartedAt)));
            await store.StopAsync(CancellationToken.None);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class TestHistoryDbContextFactory(string databasePath) :
        IDbContextFactory<HistoryDbContext>
    {
        private readonly DbContextOptions<HistoryDbContext> _options =
            new DbContextOptionsBuilder<HistoryDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False;Foreign Keys=True")
                .Options;

        public HistoryDbContext CreateDbContext() => new(_options);

        public Task<HistoryDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}

using LayupPulse.Application;
using LayupPulse.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LayupPulse.Infrastructure.Persistence;

/// <summary>
/// Lit l’historique SQLite au moyen de contextes courts sans suivi EF Core.
/// </summary>
public sealed class SqliteHistoryQueryService : IHistoryQueryService
{
    private const int MaximumRuns = 200;
    private const int MaximumAlarms = 500;
    private const int MaximumAggregates = 7_200;

    private static readonly Action<ILogger, string, Exception?> QueryFailureLog =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(20, nameof(QueryFailureLog)),
            "La lecture de l’historique local a échoué : {FailureMessage}");

    private readonly IDbContextFactory<HistoryDbContext> _contextFactory;
    private readonly ILogger<SqliteHistoryQueryService> _logger;

    public SqliteHistoryQueryService(
        IDbContextFactory<HistoryDbContext> contextFactory,
        ILogger<SqliteHistoryQueryService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ProductionRunHistoryItem>> GetRecentRunsAsync(
        ProductionRunStatus? status,
        int maximumCount,
        CancellationToken cancellationToken)
    {
        int boundedCount = ValidateAndBound(maximumCount, MaximumRuns, nameof(maximumCount));

        try
        {
            await using HistoryDbContext context = await _contextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);
            IQueryable<ProductionRunRecord> query = context.ProductionRuns.AsNoTracking();
            if (status is not null)
            {
                query = query.Where(run => run.FinalStatus == status.Value);
            }

            ProductionRunRecord[] records = await query
                .OrderByDescending(static run => run.StartedAtUtc)
                .Take(boundedCount)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            return records.Select(MapRun).ToArray();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            QueryFailureLog(_logger, exception.Message, exception);
            throw;
        }
    }

    public async Task<ProductionRunHistoryDetails?> GetRunDetailsAsync(
        Guid productionRunId,
        int maximumAlarmCount,
        int maximumAggregateCount,
        CancellationToken cancellationToken)
    {
        if (productionRunId == Guid.Empty)
        {
            throw new ArgumentException(
                "L’identifiant d’exécution ne peut pas être vide.",
                nameof(productionRunId));
        }

        int boundedAlarmCount = ValidateAndBound(
            maximumAlarmCount,
            MaximumAlarms,
            nameof(maximumAlarmCount));
        int boundedAggregateCount = ValidateAndBound(
            maximumAggregateCount,
            MaximumAggregates,
            nameof(maximumAggregateCount));

        try
        {
            await using HistoryDbContext context = await _contextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);
            ProductionRunRecord? run = await context.ProductionRuns
                .AsNoTracking()
                .SingleOrDefaultAsync(record => record.Id == productionRunId, cancellationToken)
                .ConfigureAwait(false);
            if (run is null)
            {
                return null;
            }

            AlarmRecord[] alarms = await context.Alarms
                .AsNoTracking()
                .Where(alarm => alarm.ProductionRunId == productionRunId)
                .OrderByDescending(static alarm => alarm.RaisedAtUtc)
                .Take(boundedAlarmCount)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            TelemetryAggregateRecord[] aggregates = await context.TelemetryAggregates
                .AsNoTracking()
                .Where(aggregate => aggregate.ProductionRunId == productionRunId)
                .OrderBy(static aggregate => aggregate.BucketStartedAtUtc)
                .Take(boundedAggregateCount)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            return new ProductionRunHistoryDetails(
                MapRun(run),
                alarms.Select(MapAlarm).ToArray(),
                aggregates.Select(MapAggregate).ToArray());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            QueryFailureLog(_logger, exception.Message, exception);
            throw;
        }
    }

    private static ProductionRunHistoryItem MapRun(ProductionRunRecord run) => new(
        run.Id,
        run.RecipeName,
        run.PartReference,
        ToDateTimeOffset(run.StartedAtUtc),
        run.EndedAtUtc is null ? null : ToDateTimeOffset(run.EndedAtUtc.Value),
        run.FinalStatus,
        run.CompletionPercentage,
        run.AlarmCount,
        run.AverageTemperatureCelsius,
        run.AveragePressureBar,
        run.AverageCompactionForceNewtons,
        run.AverageFeedRateMillimetersPerSecond,
        run.MinimumProcessHealthPercentage);

    private static AlarmHistoryItem MapAlarm(AlarmRecord alarm) => new(
        alarm.Id,
        alarm.Code,
        alarm.Severity,
        alarm.Source,
        alarm.Message,
        ToDateTimeOffset(alarm.RaisedAtUtc),
        alarm.AcknowledgedAtUtc is null ? null : ToDateTimeOffset(alarm.AcknowledgedAtUtc.Value),
        alarm.ClearedAtUtc is null ? null : ToDateTimeOffset(alarm.ClearedAtUtc.Value));

    private static TelemetryAggregateHistoryItem MapAggregate(TelemetryAggregateRecord aggregate) => new(
        ToDateTimeOffset(aggregate.BucketStartedAtUtc),
        aggregate.SampleCount,
        aggregate.AverageHeaterTemperatureCelsius,
        aggregate.MinimumHeaterTemperatureCelsius,
        aggregate.MaximumHeaterTemperatureCelsius,
        aggregate.AverageMaterialPressureBar,
        aggregate.MinimumMaterialPressureBar,
        aggregate.MaximumMaterialPressureBar,
        aggregate.AverageCompactionForceNewtons,
        aggregate.MinimumCompactionForceNewtons,
        aggregate.MaximumCompactionForceNewtons,
        aggregate.AverageFeedRateMillimetersPerSecond,
        aggregate.AverageProcessHealthPercentage,
        aggregate.MinimumProcessHealthPercentage,
        aggregate.EndOfBucketCycleProgressPercentage);

    private static int ValidateAndBound(int requested, int maximum, string parameterName)
    {
        if (requested <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "La limite doit être strictement positive.");
        }

        return Math.Min(requested, maximum);
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime timestamp) =>
        new(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc));
}

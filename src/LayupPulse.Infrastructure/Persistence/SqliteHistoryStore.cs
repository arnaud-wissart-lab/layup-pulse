using System.Threading.Channels;
using LayupPulse.Application;
using LayupPulse.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LayupPulse.Infrastructure.Persistence;

/// <summary>
/// Draine une file bornée vers SQLite avec un contexte court par écriture.
/// </summary>
public sealed class SqliteHistoryStore : IHistoryWriter, IHostedService, IAsyncDisposable
{
    internal const int QueueCapacity = 2_048;

    private static readonly Action<ILogger, string, Exception?> PersistenceFailureLog =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(10, nameof(PersistenceFailureLog)),
            "La persistance de l’historique local a échoué : {FailureMessage}");
    private static readonly Action<ILogger, int, Exception?> QueueSaturatedLog =
        LoggerMessage.Define<int>(
            LogLevel.Warning,
            new EventId(11, nameof(QueueSaturatedLog)),
            "La file de persistance bornée est saturée (capacité {Capacity}).");

    private readonly IDbContextFactory<HistoryDbContext> _contextFactory;
    private readonly HistoryStorageOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SqliteHistoryStore> _logger;
    private readonly Channel<HistoryWriteRequest> _queue = Channel.CreateBounded<HistoryWriteRequest>(
        new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    private readonly object _diagnosticLock = new();
    private readonly CancellationTokenSource _workerCancellation = new();
    private Task? _worker;
    private string? _lastDiagnosticMessage;
    private bool _storageAvailable;
    private int _started;
    private int _stopped;

    public SqliteHistoryStore(
        IDbContextFactory<HistoryDbContext> contextFactory,
        HistoryStorageOptions options,
        TimeProvider timeProvider,
        ILogger<SqliteHistoryStore> logger)
    {
        _contextFactory = contextFactory;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public event EventHandler<HistoryPersistenceDiagnosticEventArgs>? DiagnosticOccurred;

    public string? LastDiagnosticMessage
    {
        get
        {
            lock (_diagnosticLock)
            {
                return _lastDiagnosticMessage;
            }
        }
    }

    public bool TryRecordProductionRun(ProductionRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return TryEnqueue(new ProductionRunWriteRequest(run));
    }

    public bool TryRecordTelemetryAggregate(TelemetryAggregate aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        return aggregate.ProductionRunId == Guid.Empty
            || TryEnqueue(new TelemetryAggregateWriteRequest(aggregate));
    }

    public bool TryRecordAlarm(AlarmEvent alarm)
    {
        ArgumentNullException.ThrowIfNull(alarm);
        return TryEnqueue(new AlarmWriteRequest(alarm));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        try
        {
            string? directory = Path.GetDirectoryName(_options.DatabasePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using HistoryDbContext context = await _contextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);
            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            await RecoverInterruptedRunsAsync(context, cancellationToken).ConfigureAwait(false);
            _storageAvailable = true;
        }
        catch (Exception exception)
        {
            ReportFailure("La base SQLite n’a pas pu être initialisée.", exception);
        }

        _worker = ProcessQueueAsync(_workerCancellation.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        _queue.Writer.TryComplete();
        Task? worker = _worker;
        if (worker is null)
        {
            return;
        }

        try
        {
            await worker.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _workerCancellation.Cancel();
            try
            {
                await worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // L’annulation borne explicitement les écritures restantes à l’arrêt.
            }

            ReportFailure(
                "Le drainage de l’historique local a dépassé le délai d’arrêt.",
                new TimeoutException("Délai d’arrêt de la persistance dépassé."));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _workerCancellation.Dispose();
    }

    private bool TryEnqueue(HistoryWriteRequest request)
    {
        if (!_storageAvailable || Volatile.Read(ref _stopped) != 0)
        {
            return false;
        }

        if (_queue.Writer.TryWrite(request))
        {
            return true;
        }

        QueueSaturatedLog(_logger, QueueCapacity, null);
        PublishDiagnostic(
            $"La file de persistance locale est saturée (capacité {QueueCapacity}) ; " +
            "un événement d’historique n’a pas été enregistré.");
        return false;
    }

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        await foreach (HistoryWriteRequest request in _queue.Reader
            .ReadAllAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            try
            {
                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                await using HistoryDbContext context = await _contextFactory
                    .CreateDbContextAsync(timeout.Token)
                    .ConfigureAwait(false);
                await PersistAsync(context, request, timeout.Token).ConfigureAwait(false);
                await context.SaveChangesAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                ReportFailure("Une écriture SQLite a échoué ; la télémétrie continue.", exception);
            }
        }
    }

    private static async Task PersistAsync(
        HistoryDbContext context,
        HistoryWriteRequest request,
        CancellationToken cancellationToken)
    {
        switch (request)
        {
            case ProductionRunWriteRequest productionRun:
                await UpsertProductionRunAsync(context, productionRun.Run, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case TelemetryAggregateWriteRequest telemetryAggregate:
                await UpsertTelemetryAggregateAsync(
                        context,
                        telemetryAggregate.Aggregate,
                        cancellationToken)
                    .ConfigureAwait(false);
                break;
            case AlarmWriteRequest alarm:
                await UpsertAlarmAsync(context, alarm.Alarm, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private static async Task UpsertProductionRunAsync(
        HistoryDbContext context,
        ProductionRun run,
        CancellationToken cancellationToken)
    {
        ProductionRunRecord? record = await context.ProductionRuns
            .FindAsync([run.Id], cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            record = new ProductionRunRecord { Id = run.Id };
            context.ProductionRuns.Add(record);
        }

        record.RecipeName = run.Recipe.Name;
        record.PartReference = run.Recipe.PartReference;
        record.StartedAtUtc = ToUtcDateTime(run.StartedAt);
        record.EndedAtUtc = run.EndedAt is null ? null : ToUtcDateTime(run.EndedAt.Value);
        record.FinalStatus = run.Status;
        record.CompletionPercentage = run.CompletionPercentage;
        record.AlarmCount = run.AlarmCount;
        record.AverageTemperatureCelsius = run.AverageTemperatureCelsius;
        record.AveragePressureBar = run.AveragePressureBar;
        record.AverageCompactionForceNewtons = run.AverageCompactionForceNewtons;
        record.AverageFeedRateMillimetersPerSecond = run.AverageFeedRateMillimetersPerSecond;
        record.MinimumProcessHealthPercentage = run.MinimumProcessHealthPercentage;
    }

    private static async Task UpsertTelemetryAggregateAsync(
        HistoryDbContext context,
        TelemetryAggregate aggregate,
        CancellationToken cancellationToken)
    {
        DateTime bucketStartedAt = ToUtcDateTime(aggregate.WindowStartedAt);
        TelemetryAggregateRecord? record = await context.TelemetryAggregates
            .FindAsync([aggregate.ProductionRunId, bucketStartedAt], cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            record = new TelemetryAggregateRecord
            {
                ProductionRunId = aggregate.ProductionRunId,
                BucketStartedAtUtc = bucketStartedAt,
            };
            context.TelemetryAggregates.Add(record);
        }

        record.SampleCount = aggregate.SampleCount;
        record.AverageFeedRateMillimetersPerSecond = aggregate.AverageFeedRateMillimetersPerSecond;
        record.AverageCompactionForceNewtons = aggregate.AverageCompactionForceNewtons;
        record.AverageHeaterTemperatureCelsius = aggregate.AverageHeaterTemperatureCelsius;
        record.MinimumHeaterTemperatureCelsius = aggregate.MinimumHeaterTemperatureCelsius;
        record.MaximumHeaterTemperatureCelsius = aggregate.MaximumHeaterTemperatureCelsius;
        record.AverageMaterialPressureBar = aggregate.AverageMaterialPressureBar;
        record.MinimumMaterialPressureBar = aggregate.MinimumMaterialPressureBar;
        record.MaximumMaterialPressureBar = aggregate.MaximumMaterialPressureBar;
        record.MinimumCompactionForceNewtons = aggregate.MinimumCompactionForceNewtons;
        record.MaximumCompactionForceNewtons = aggregate.MaximumCompactionForceNewtons;
        record.AverageProcessHealthPercentage = aggregate.AverageProcessHealthPercentage;
        record.MinimumProcessHealthPercentage = aggregate.MinimumProcessHealthPercentage;
        record.EndOfBucketCycleProgressPercentage = aggregate.EndOfBucketCycleProgressPercentage;
    }

    private static async Task UpsertAlarmAsync(
        HistoryDbContext context,
        AlarmEvent alarm,
        CancellationToken cancellationToken)
    {
        AlarmRecord? record = await context.Alarms
            .FindAsync([alarm.Id], cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            record = new AlarmRecord { Id = alarm.Id };
            context.Alarms.Add(record);
        }

        record.ProductionRunId = alarm.ProductionRunId;
        record.Code = alarm.Code;
        record.Severity = alarm.Severity;
        record.Source = alarm.Source;
        record.Message = alarm.Message;
        record.RaisedAtUtc = ToUtcDateTime(alarm.RaisedAt);
        record.AcknowledgedAtUtc = alarm.AcknowledgedAt is null
            ? null
            : ToUtcDateTime(alarm.AcknowledgedAt.Value);
        record.ClearedAtUtc = alarm.ClearedAt is null
            ? null
            : ToUtcDateTime(alarm.ClearedAt.Value);
    }

    private async Task RecoverInterruptedRunsAsync(
        HistoryDbContext context,
        CancellationToken cancellationToken)
    {
        ProductionRunRecord[] interruptedRuns = await context.ProductionRuns
            .Where(static run => run.FinalStatus == ProductionRunStatus.Running)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        if (interruptedRuns.Length == 0)
        {
            return;
        }

        DateTime recoveredAt = ToUtcDateTime(_timeProvider.GetUtcNow());
        foreach (ProductionRunRecord run in interruptedRuns)
        {
            run.FinalStatus = ProductionRunStatus.Aborted;
            run.EndedAtUtc = recoveredAt < run.StartedAtUtc ? run.StartedAtUtc : recoveredAt;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private void ReportFailure(string context, Exception exception)
    {
        PersistenceFailureLog(_logger, exception.Message, exception);
        PublishDiagnostic($"{context} {exception.Message}");
    }

    private void PublishDiagnostic(string message)
    {
        lock (_diagnosticLock)
        {
            _lastDiagnosticMessage = message;
        }

        DiagnosticOccurred?.Invoke(this, new HistoryPersistenceDiagnosticEventArgs(message));
    }

    private static DateTime ToUtcDateTime(DateTimeOffset timestamp) => timestamp.UtcDateTime;

    private abstract record HistoryWriteRequest;

    private sealed record ProductionRunWriteRequest(ProductionRun Run) : HistoryWriteRequest;

    private sealed record TelemetryAggregateWriteRequest(TelemetryAggregate Aggregate) : HistoryWriteRequest;

    private sealed record AlarmWriteRequest(AlarmEvent Alarm) : HistoryWriteRequest;
}

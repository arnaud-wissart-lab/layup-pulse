using LayupPulse.Domain;

namespace LayupPulse.Application;

/// <summary>
/// Compose les repositories de lecture en modèles applicatifs consommables par l’interface.
/// </summary>
public sealed class ProductionHistoryService : IProductionHistoryService
{
    private readonly IProductionRunRepository _productionRuns;
    private readonly IAlarmRepository _alarms;
    private readonly ITelemetryAggregateRepository _telemetryAggregates;
    private readonly ILocalHistoryStore _localHistoryStore;
    private readonly IPersistenceDiagnosticReporter _diagnostics;

    public ProductionHistoryService(
        IProductionRunRepository productionRuns,
        IAlarmRepository alarms,
        ITelemetryAggregateRepository telemetryAggregates,
        ILocalHistoryStore localHistoryStore,
        IPersistenceDiagnosticReporter diagnostics)
    {
        _productionRuns = productionRuns;
        _alarms = alarms;
        _telemetryAggregates = telemetryAggregates;
        _localHistoryStore = localHistoryStore;
        _diagnostics = diagnostics;
    }

    public async Task<IReadOnlyList<ProductionRun>> GetRunsAsync(
        ProductionHistoryFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        try
        {
            ProductionRunQuery query = new(filter.MaximumCount, filter.Status, filter.StartedAfter);
            List<ProductionRun> runs = [];
            await foreach (ProductionRun run in _productionRuns
                .GetRecentAsync(query, cancellationToken)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                runs.Add(run);
            }

            return runs;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _diagnostics.ReportFailure("lecture de l’historique de production", exception);
            throw;
        }
    }

    public async Task<ProductionRunDetails?> GetDetailsAsync(
        Guid productionRunId,
        CancellationToken cancellationToken)
    {
        if (productionRunId == Guid.Empty)
        {
            throw new ArgumentException("L’identifiant du run ne peut pas être vide.", nameof(productionRunId));
        }

        try
        {
            ProductionRun? run = await _productionRuns
                .GetByIdAsync(productionRunId, cancellationToken)
                .ConfigureAwait(false);
            if (run is null)
            {
                return null;
            }

            List<AlarmEvent> alarms = [];
            await foreach (AlarmEvent alarm in _alarms
                .GetByProductionRunIdAsync(productionRunId, cancellationToken)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                alarms.Add(alarm);
            }

            List<TelemetryAggregate> aggregates = [];
            await foreach (TelemetryAggregate aggregate in _telemetryAggregates
                .GetByProductionRunIdAsync(productionRunId, cancellationToken)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                aggregates.Add(aggregate);
            }

            return new ProductionRunDetails(run, alarms, aggregates);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _diagnostics.ReportFailure("lecture du détail d’un run", exception);
            throw;
        }
    }

    public async Task ClearLocalDataAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _localHistoryStore.ClearAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _diagnostics.ReportFailure("effacement des données locales", exception);
            throw;
        }
    }
}

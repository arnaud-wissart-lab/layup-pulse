namespace LayupPulse.Application;

/// <summary>
/// Persiste les agrégats télémétriques déjà produits hors de la cadence UI.
/// </summary>
public interface ITelemetryAggregateRepository
{
    public Task SaveAsync(TelemetryAggregate aggregate, CancellationToken cancellationToken);
}

using LayupPulse.Domain;

namespace LayupPulse.Application;

/// <summary>
/// Persiste les échantillons télémétriques déjà sélectionnés pour l’historique agrégé.
/// </summary>
public interface ITelemetryAggregateRepository
{
    public Task SaveAsync(TelemetrySample sample, CancellationToken cancellationToken);
}

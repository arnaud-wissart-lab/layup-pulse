namespace LayupPulse.Application;

/// <summary>
/// Efface de manière atomique les données de démonstration persistées localement.
/// </summary>
public interface ILocalHistoryStore
{
    public Task ClearAsync(CancellationToken cancellationToken);
}

namespace LayupPulse.Infrastructure.Persistence;

/// <summary>
/// Résout l’emplacement par utilisateur de la base locale.
/// </summary>
public static class HistoryDatabasePath
{
    public static string GetDefault()
    {
        string localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localApplicationData, "LayupPulse", "layuppulse.db");
    }
}

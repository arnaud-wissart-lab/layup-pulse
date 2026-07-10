namespace LayupPulse.Domain;

/// <summary>
/// Décrit l’avancement ou l’issue d’une exécution de production simulée.
/// </summary>
public enum ProductionRunStatus
{
    Running,
    Paused,
    Completed,
    Aborted,
    Failed,
}

namespace LayupPulse.Domain;

/// <summary>
/// Décrit la visibilité et l’activité d’une alarme au cours de son cycle de vie.
/// </summary>
public enum AlarmLifecycleState
{
    Raised,
    Acknowledged,
    Cleared,
}

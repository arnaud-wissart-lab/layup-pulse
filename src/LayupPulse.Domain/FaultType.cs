namespace LayupPulse.Domain;

/// <summary>
/// Identifie les conditions de défaut injectables dans la cellule simulée.
/// </summary>
public enum FaultType
{
    HighTemperature,
    LowMaterialPressure,
    UnstableCompactionForce,
    CommunicationTimeout,
    HeadPositionError,
}

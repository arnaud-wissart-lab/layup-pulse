namespace LayupPulse.Domain;

/// <summary>
/// Identifie de manière stable une alarme du catalogue initial.
/// </summary>
public enum AlarmCode
{
    HighTemperature,
    LowMaterialPressure,
    UnstableCompactionForce,
    CommunicationTimeout,
    HeadPositionError,
}

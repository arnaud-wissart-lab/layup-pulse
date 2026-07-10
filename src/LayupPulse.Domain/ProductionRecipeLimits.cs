namespace LayupPulse.Domain;

/// <summary>
/// Définit les limites fictives du démonstrateur, sans valeur de sécurité industrielle.
/// </summary>
public static class ProductionRecipeLimits
{
    /// <summary>Température cible simulée minimale, en degrés Celsius.</summary>
    public const double MinimumTargetTemperatureCelsius = 20;

    /// <summary>Température cible simulée maximale, en degrés Celsius.</summary>
    public const double MaximumTargetTemperatureCelsius = 250;

    /// <summary>Pression cible simulée minimale, en bars.</summary>
    public const double MinimumTargetPressureBar = 1;

    /// <summary>Pression cible simulée maximale, en bars.</summary>
    public const double MaximumTargetPressureBar = 12;
}

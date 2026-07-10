namespace LayupPulse.Domain;

/// <summary>
/// Décrit les paramètres immuables d’un cycle de dépose simulé.
/// </summary>
public sealed record ProductionRecipe(
    Guid Id,
    string Name,
    string PartReference,
    double TargetTemperatureCelsius,
    double TargetPressureBar,
    double FeedRateMillimetersPerSecond,
    int PassCount,
    TimeSpan EstimatedDuration)
{
    /// <summary>
    /// Valide la recette sans lever d’exception pour les erreurs de saisie attendues.
    /// </summary>
    public RecipeValidationResult Validate() => ProductionRecipeValidator.Validate(this);
}

namespace LayupPulse.Domain;

/// <summary>
/// Applique les règles de validité des recettes simulées.
/// </summary>
public static class ProductionRecipeValidator
{
    public static RecipeValidationResult Validate(ProductionRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);

        List<RecipeValidationError> errors = [];

        if (string.IsNullOrWhiteSpace(recipe.Name))
        {
            errors.Add(new(
                RecipeValidationErrorCode.NameRequired,
                nameof(ProductionRecipe.Name),
                "Le nom de la recette est obligatoire."));
        }

        if (string.IsNullOrWhiteSpace(recipe.PartReference))
        {
            errors.Add(new(
                RecipeValidationErrorCode.PartReferenceRequired,
                nameof(ProductionRecipe.PartReference),
                "La référence de pièce est obligatoire."));
        }

        if (!IsWithinInclusiveRange(
                recipe.TargetTemperatureCelsius,
                ProductionRecipeLimits.MinimumTargetTemperatureCelsius,
                ProductionRecipeLimits.MaximumTargetTemperatureCelsius))
        {
            errors.Add(new(
                RecipeValidationErrorCode.TargetTemperatureOutOfRange,
                nameof(ProductionRecipe.TargetTemperatureCelsius),
                $"La température cible doit être comprise entre {ProductionRecipeLimits.MinimumTargetTemperatureCelsius} et {ProductionRecipeLimits.MaximumTargetTemperatureCelsius} °C."));
        }

        if (!IsWithinInclusiveRange(
                recipe.TargetPressureBar,
                ProductionRecipeLimits.MinimumTargetPressureBar,
                ProductionRecipeLimits.MaximumTargetPressureBar))
        {
            errors.Add(new(
                RecipeValidationErrorCode.TargetPressureOutOfRange,
                nameof(ProductionRecipe.TargetPressureBar),
                $"La pression cible doit être comprise entre {ProductionRecipeLimits.MinimumTargetPressureBar} et {ProductionRecipeLimits.MaximumTargetPressureBar} bar."));
        }

        if (!double.IsFinite(recipe.FeedRateMillimetersPerSecond) || recipe.FeedRateMillimetersPerSecond <= 0)
        {
            errors.Add(new(
                RecipeValidationErrorCode.FeedRateMustBePositive,
                nameof(ProductionRecipe.FeedRateMillimetersPerSecond),
                "La vitesse d’avance doit être strictement positive."));
        }

        if (recipe.PassCount <= 0)
        {
            errors.Add(new(
                RecipeValidationErrorCode.PassCountMustBePositive,
                nameof(ProductionRecipe.PassCount),
                "Le nombre de passes doit être strictement positif."));
        }

        if (recipe.EstimatedDuration <= TimeSpan.Zero)
        {
            errors.Add(new(
                RecipeValidationErrorCode.EstimatedDurationMustBePositive,
                nameof(ProductionRecipe.EstimatedDuration),
                "La durée estimée doit être strictement positive."));
        }

        return new(errors);
    }

    private static bool IsWithinInclusiveRange(double value, double minimum, double maximum) =>
        double.IsFinite(value) && value >= minimum && value <= maximum;
}

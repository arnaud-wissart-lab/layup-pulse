namespace LayupPulse.Domain;

/// <summary>
/// Fournit une erreur de recette exploitable par une future couche de présentation.
/// </summary>
public sealed record RecipeValidationError(
    RecipeValidationErrorCode Code,
    string PropertyName,
    string Message);

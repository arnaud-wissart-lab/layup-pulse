namespace LayupPulse.Domain;

/// <summary>
/// Identifie de manière stable une erreur de validation de recette.
/// </summary>
public enum RecipeValidationErrorCode
{
    NameRequired,
    PartReferenceRequired,
    TargetTemperatureOutOfRange,
    TargetPressureOutOfRange,
    FeedRateMustBePositive,
    PassCountMustBePositive,
    EstimatedDurationMustBePositive,
}

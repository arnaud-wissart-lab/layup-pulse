namespace LayupPulse.Domain;

/// <summary>
/// Identifie la cause stable d’un rejet de transition.
/// </summary>
public enum StateTransitionRejectionCode
{
    InvalidState,
    RecipeRequired,
    InvalidRecipe,
    ProductionRunMissing,
    FaultRequired,
    FaultNotActive,
    ActiveFaultsRemain,
    UnsupportedCommand,
}

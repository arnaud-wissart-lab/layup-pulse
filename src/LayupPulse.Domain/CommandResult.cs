namespace LayupPulse.Domain;

/// <summary>
/// Associe le résultat métier d’une commande à son identifiant de corrélation.
/// </summary>
public sealed record CommandResult(Guid CorrelationId, StateTransitionResult Transition)
{
    public bool IsAccepted => Transition.IsAccepted;
}

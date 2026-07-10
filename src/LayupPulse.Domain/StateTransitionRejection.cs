using System.Collections.ObjectModel;

namespace LayupPulse.Domain;

/// <summary>
/// Fournit une cause de rejet stable et les éventuelles erreurs de recette associées.
/// </summary>
public sealed class StateTransitionRejection
{
    public StateTransitionRejection(
        StateTransitionRejectionCode code,
        string message,
        IEnumerable<RecipeValidationError>? validationErrors = null)
    {
        Code = code;
        Message = message;
        ValidationErrors = new ReadOnlyCollection<RecipeValidationError>(
            validationErrors?.ToArray() ?? []);
    }

    public StateTransitionRejectionCode Code { get; }

    public string Message { get; }

    public IReadOnlyList<RecipeValidationError> ValidationErrors { get; }
}

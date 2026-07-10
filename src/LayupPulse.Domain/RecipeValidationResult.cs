using System.Collections.ObjectModel;

namespace LayupPulse.Domain;

/// <summary>
/// Regroupe le résultat et les erreurs structurées d’une validation de recette.
/// </summary>
public sealed class RecipeValidationResult
{
    internal RecipeValidationResult(IEnumerable<RecipeValidationError> errors)
    {
        Errors = new ReadOnlyCollection<RecipeValidationError>(errors.ToArray());
    }

    public bool IsValid => Errors.Count == 0;

    public IReadOnlyList<RecipeValidationError> Errors { get; }
}

namespace LayupPulse.Domain;

/// <summary>
/// Expose les recettes fictives fournies avec le démonstrateur.
/// </summary>
public static class BuiltInRecipes
{
    public static ProductionRecipe WingPanelDemo => new(
        new Guid("b6fe6261-97c1-4b07-b919-7109c61c3905"),
        "Wing Panel Demo",
        "WP-DEMO-001",
        145,
        6,
        120,
        8,
        TimeSpan.FromMinutes(5));
}

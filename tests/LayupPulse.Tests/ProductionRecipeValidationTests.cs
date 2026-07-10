using LayupPulse.Domain;
using Xunit;

namespace LayupPulse.Tests;

public sealed class ProductionRecipeValidationTests
{
    [Fact]
    public void BuiltInWingPanelDemoRecipeIsValid()
    {
        // Préparation
        ProductionRecipe recipe = BuiltInRecipes.WingPanelDemo;

        // Action
        RecipeValidationResult result = recipe.Validate();

        // Vérification
        Assert.Equal("Wing Panel Demo", recipe.Name);
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(
        ProductionRecipeLimits.MinimumTargetTemperatureCelsius,
        ProductionRecipeLimits.MinimumTargetPressureBar)]
    [InlineData(
        ProductionRecipeLimits.MaximumTargetTemperatureCelsius,
        ProductionRecipeLimits.MaximumTargetPressureBar)]
    public void DocumentedTemperatureAndPressureBoundariesAreValid(
        double targetTemperatureCelsius,
        double targetPressureBar)
    {
        // Préparation
        ProductionRecipe recipe = BuiltInRecipes.WingPanelDemo with
        {
            TargetTemperatureCelsius = targetTemperatureCelsius,
            TargetPressureBar = targetPressureBar,
        };

        // Action
        RecipeValidationResult result = recipe.Validate();

        // Vérification
        Assert.True(result.IsValid);
    }

    [Fact]
    public void InvalidRecipeReturnsOneStructuredErrorForEveryRule()
    {
        // Préparation
        ProductionRecipe recipe = new(
            Guid.NewGuid(),
            " ",
            string.Empty,
            double.NaN,
            ProductionRecipeLimits.MaximumTargetPressureBar + 1,
            0,
            0,
            TimeSpan.Zero);

        // Action
        RecipeValidationResult result = recipe.Validate();

        // Vérification
        Assert.False(result.IsValid);
        Assert.Equal(7, result.Errors.Count);
        Assert.Equal(
            Enum.GetValues<RecipeValidationErrorCode>().Order(),
            result.Errors.Select(error => error.Code).Order());
        Assert.All(result.Errors, error =>
        {
            Assert.False(string.IsNullOrWhiteSpace(error.PropertyName));
            Assert.False(string.IsNullOrWhiteSpace(error.Message));
        });
    }
}

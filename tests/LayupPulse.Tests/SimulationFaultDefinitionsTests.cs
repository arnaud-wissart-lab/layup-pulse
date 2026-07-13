using LayupPulse.Desktop;
using LayupPulse.Domain;
using Xunit;

namespace LayupPulse.Tests;

public sealed class SimulationFaultDefinitionsTests
{
    [Fact]
    public void DefinitionsExposeFrenchDisplayNamesTechnicalNamesAndHelp()
    {
        var expected = new[]
        {
            (
                FaultType.HighTemperature,
                "Surchauffe",
                "OverTemperature",
                "température de chauffe supérieure au seuil autorisé."),
            (
                FaultType.LowMaterialPressure,
                "Pression matière insuffisante",
                "LowMaterialPressure",
                "pression trop faible pour assurer une alimentation régulière du matériau."),
            (
                FaultType.UnstableCompactionForce,
                "Force de compactage instable",
                "UnstableCompactionForce",
                "variations anormales de la force appliquée par le rouleau sur les fibres."),
            (
                FaultType.HeadPositionError,
                "Erreur de position de la tête",
                "HeadPositionError",
                "écart simulé entre position demandée et position réelle de la tête."),
            (
                FaultType.CommunicationTimeout,
                "Perte de communication",
                "CommunicationDrop",
                "interruption simulée du flux entre Simulator et Desktop."),
        };

        Assert.Equal(expected.Length, SimulationFaultDefinitions.All.Count);
        foreach ((FaultType fault, string displayName, string technicalName, string description) in expected)
        {
            SimulationFaultDefinition definition = SimulationFaultDefinitions.Get(fault);
            Assert.Equal(displayName, definition.DisplayName);
            Assert.Equal(technicalName, definition.TechnicalName);
            Assert.Equal(description, definition.Description);
            Assert.Equal($"{displayName} ({technicalName})", definition.DisplayLabel);
            Assert.Contains(description, definition.HelpText, StringComparison.Ordinal);
        }
    }
}

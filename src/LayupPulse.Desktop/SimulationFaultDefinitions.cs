using LayupPulse.Domain;

namespace LayupPulse.Desktop;

public sealed record SimulationFaultDefinition(
    FaultType Fault,
    string TechnicalName,
    string DisplayName,
    string Description)
{
    public string DisplayLabel => $"{DisplayName} ({TechnicalName})";

    public string HelpText => $"{DisplayLabel} : {Description}";
}

/// <summary>
/// Centralise les libellés opérateur des défauts exposés par le contrat de simulation.
/// </summary>
public static class SimulationFaultDefinitions
{
    public static IReadOnlyList<SimulationFaultDefinition> All { get; } =
    [
        new(
            FaultType.HighTemperature,
            "OverTemperature",
            "Surchauffe",
            "température de chauffe supérieure au seuil autorisé."),
        new(
            FaultType.LowMaterialPressure,
            "LowMaterialPressure",
            "Pression matière insuffisante",
            "pression trop faible pour assurer une alimentation régulière du matériau."),
        new(
            FaultType.UnstableCompactionForce,
            "UnstableCompactionForce",
            "Force de compactage instable",
            "variations anormales de la force appliquée par le rouleau sur les fibres."),
        new(
            FaultType.HeadPositionError,
            "HeadPositionError",
            "Erreur de position de la tête",
            "écart simulé entre position demandée et position réelle de la tête."),
        new(
            FaultType.CommunicationTimeout,
            "CommunicationDrop",
            "Perte de communication",
            "interruption simulée du flux entre Simulator et Desktop."),
    ];

    public static SimulationFaultDefinition Get(FaultType fault) =>
        All.Single(definition => definition.Fault == fault);
}

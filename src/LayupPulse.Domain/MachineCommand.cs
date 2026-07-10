namespace LayupPulse.Domain;

/// <summary>
/// Porte une intention corrélée et les données optionnelles nécessaires à son traitement déterministe.
/// </summary>
public sealed record MachineCommand
{
    public MachineCommand(
        Guid correlationId,
        MachineCommandType type,
        DateTimeOffset timestamp,
        ProductionRecipe? recipe = null,
        Guid? productionRunId = null,
        FaultType? fault = null)
    {
        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("L’identifiant de corrélation ne peut pas être vide.", nameof(correlationId));
        }

        CorrelationId = correlationId;
        Type = type;
        Timestamp = timestamp.ToUniversalTime();
        Recipe = recipe;
        ProductionRunId = productionRunId;
        Fault = fault;
    }

    public Guid CorrelationId { get; init; }

    public MachineCommandType Type { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    public ProductionRecipe? Recipe { get; init; }

    /// <summary>
    /// Identifiant du run à créer ; l’identifiant de corrélation est utilisé lorsqu’il est omis.
    /// </summary>
    public Guid? ProductionRunId { get; init; }

    public FaultType? Fault { get; init; }
}

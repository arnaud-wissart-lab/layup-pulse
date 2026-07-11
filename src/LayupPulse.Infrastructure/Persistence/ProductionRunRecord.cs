using LayupPulse.Domain;

namespace LayupPulse.Infrastructure.Persistence;

/// <summary>
/// Représentation EF Core d’une exécution de production simulée.
/// </summary>
public sealed class ProductionRunRecord
{
    public Guid Id { get; set; }

    public string RecipeName { get; set; } = string.Empty;

    public string PartReference { get; set; } = string.Empty;

    public DateTime StartedAtUtc { get; set; }

    public DateTime? EndedAtUtc { get; set; }

    public ProductionRunStatus FinalStatus { get; set; }

    public double CompletionPercentage { get; set; }

    public int AlarmCount { get; set; }

    public double AverageTemperatureCelsius { get; set; }

    public double AveragePressureBar { get; set; }

    public double AverageCompactionForceNewtons { get; set; }

    public double AverageFeedRateMillimetersPerSecond { get; set; }

    public double MinimumProcessHealthPercentage { get; set; }

    public ICollection<TelemetryAggregateRecord> TelemetryAggregates { get; } = [];

    public ICollection<AlarmRecord> Alarms { get; } = [];
}

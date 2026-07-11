using LayupPulse.Domain;

namespace LayupPulse.Infrastructure.Persistence;

/// <summary>
/// Représentation EF Core d’une occurrence d’alarme simulée.
/// </summary>
public sealed class AlarmRecord
{
    public Guid Id { get; set; }

    public Guid? ProductionRunId { get; set; }

    public AlarmCode Code { get; set; }

    public AlarmSeverity Severity { get; set; }

    public string Source { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public DateTime RaisedAtUtc { get; set; }

    public DateTime? AcknowledgedAtUtc { get; set; }

    public DateTime? ClearedAtUtc { get; set; }

    public ProductionRunRecord? ProductionRun { get; set; }
}

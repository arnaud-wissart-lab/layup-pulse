using LayupPulse.Domain;

namespace LayupPulse.Application;

/// <summary>
/// Modèle de lecture d’une occurrence d’alarme persistée.
/// </summary>
public sealed record AlarmHistoryItem(
    Guid Id,
    AlarmCode Code,
    AlarmSeverity Severity,
    string Source,
    string Message,
    DateTimeOffset RaisedAt,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? ClearedAt);

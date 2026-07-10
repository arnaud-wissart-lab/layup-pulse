namespace LayupPulse.Domain;

/// <summary>
/// Représente l’état immuable d’une occurrence d’alarme simulée.
/// </summary>
public sealed record AlarmEvent
{
    public AlarmEvent(
        Guid id,
        AlarmCode code,
        AlarmSeverity severity,
        string source,
        string message,
        DateTimeOffset raisedAt,
        AlarmLifecycleState lifecycleState = AlarmLifecycleState.Raised,
        DateTimeOffset? acknowledgedAt = null,
        DateTimeOffset? clearedAt = null,
        Guid? productionRunId = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("L’identifiant d’alarme ne peut pas être vide.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Le message d’alarme est obligatoire.", nameof(message));
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("La source de l’alarme est obligatoire.", nameof(source));
        }

        Id = id;
        Code = code;
        Severity = severity;
        Source = source;
        Message = message;
        RaisedAt = raisedAt.ToUniversalTime();
        LifecycleState = lifecycleState;
        AcknowledgedAt = acknowledgedAt?.ToUniversalTime();
        ClearedAt = clearedAt?.ToUniversalTime();
        ProductionRunId = productionRunId;
    }

    public Guid Id { get; init; }

    public AlarmCode Code { get; init; }

    public AlarmSeverity Severity { get; init; }

    public string Source { get; init; }

    public string Message { get; init; }

    public DateTimeOffset RaisedAt { get; init; }

    public AlarmLifecycleState LifecycleState { get; init; }

    public DateTimeOffset? AcknowledgedAt { get; init; }

    public DateTimeOffset? ClearedAt { get; init; }

    public Guid? ProductionRunId { get; init; }

    public AlarmEvent Acknowledge(DateTimeOffset timestamp)
    {
        if (LifecycleState == AlarmLifecycleState.Cleared || AcknowledgedAt is not null)
        {
            return this;
        }

        return this with
        {
            LifecycleState = AlarmLifecycleState.Acknowledged,
            AcknowledgedAt = timestamp.ToUniversalTime(),
        };
    }

    public AlarmEvent Clear(DateTimeOffset timestamp)
    {
        if (LifecycleState == AlarmLifecycleState.Cleared)
        {
            return this;
        }

        return this with
        {
            LifecycleState = AlarmLifecycleState.Cleared,
            ClearedAt = timestamp.ToUniversalTime(),
        };
    }
}

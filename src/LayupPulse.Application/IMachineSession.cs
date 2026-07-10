namespace LayupPulse.Application;

/// <summary>
/// Identifie une session de communication dont la séquence télémétrique possède sa propre portée.
/// </summary>
public interface IMachineSession
{
    public Guid SessionId { get; }

    public DateTimeOffset ConnectedAt { get; }
}

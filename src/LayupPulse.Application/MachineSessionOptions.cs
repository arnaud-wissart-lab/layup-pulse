namespace LayupPulse.Application;

/// <summary>
/// Configure la détection de fraîcheur et les limites mémoire de la session.
/// </summary>
public sealed class MachineSessionOptions
{
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan StaleCheckInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan NotificationInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    public int DiagnosticCapacity { get; set; } = 100;

    internal void Validate()
    {
        if (StaleAfter <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(StaleAfter));
        }

        if (StaleCheckInterval <= TimeSpan.Zero || StaleCheckInterval > StaleAfter)
        {
            throw new ArgumentOutOfRangeException(nameof(StaleCheckInterval));
        }

        if (NotificationInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(NotificationInterval));
        }

        if (DiagnosticCapacity is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(DiagnosticCapacity));
        }
    }
}

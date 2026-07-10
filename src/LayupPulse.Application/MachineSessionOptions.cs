using LayupPulse.Domain;

namespace LayupPulse.Application;

/// <summary>
/// Configure la détection de fraîcheur et les limites mémoire de la session.
/// </summary>
public sealed class MachineSessionOptions
{
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan StaleCheckInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan ReconnectInitialDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan ReconnectMaximumDelay { get; set; } = TimeSpan.FromSeconds(2);

    public double ReconnectBackoffMultiplier { get; set; } = 2;

    public int DiagnosticCapacity { get; set; } = 100;

    public TelemetryPipelineOptions Telemetry { get; set; } = new();

    public AlarmEngineOptions Alarms { get; set; } = new();

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

        if (StaleAfter >= Alarms.CommunicationTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(StaleAfter),
                "Le seuil de péremption doit précéder le délai de communication.");
        }

        if (ReconnectInitialDelay <= TimeSpan.Zero
            || ReconnectMaximumDelay < ReconnectInitialDelay
            || ReconnectMaximumDelay > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(nameof(ReconnectInitialDelay));
        }

        if (!double.IsFinite(ReconnectBackoffMultiplier) || ReconnectBackoffMultiplier < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ReconnectBackoffMultiplier));
        }

        if (DiagnosticCapacity is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(DiagnosticCapacity));
        }

        Telemetry.Validate();
        Alarms.Validate();
    }
}

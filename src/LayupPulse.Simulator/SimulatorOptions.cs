namespace LayupPulse.Simulator;

/// <summary>
/// Regroupe les paramètres publics du processus de simulation locale.
/// </summary>
public sealed class SimulatorOptions
{
    public const string SectionName = "Simulator";
    public const int MinimumTelemetryRateHz = 1;
    public const int MaximumTelemetryRateHz = 50;

    public string Endpoint { get; set; } = "http://127.0.0.1:5057";

    public int Seed { get; set; } = 24117;

    public int TelemetryRateHz { get; set; } = 20;

    public static bool IsValidLocalEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        return uri.IsLoopback &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}

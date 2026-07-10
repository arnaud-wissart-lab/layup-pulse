namespace LayupPulse.Infrastructure;

/// <summary>
/// Configure le point d’accès local et les délais des appels gRPC unitaires.
/// </summary>
public sealed class GrpcMachineGatewayOptions
{
    public string Endpoint { get; set; } = "http://127.0.0.1:5057";

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(3);

    public Uri GetValidatedEndpoint()
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out Uri? endpoint)
            || endpoint.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(endpoint.UserInfo))
        {
            throw new InvalidOperationException(
                "Le point d’accès gRPC doit être une URI HTTP(S) absolue sans informations d’identification.");
        }

        if (RequestTimeout <= TimeSpan.Zero || RequestTimeout > TimeSpan.FromMinutes(1))
        {
            throw new InvalidOperationException("Le délai des appels gRPC doit être compris entre 0 et 60 secondes.");
        }

        return endpoint;
    }
}

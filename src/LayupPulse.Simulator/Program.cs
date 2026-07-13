using LayupPulse.Simulator;
using Microsoft.Extensions.Options;

string endpoint = "http://127.0.0.1:5057";
try
{
    await using WebApplication application = SimulatorHost.BuildApplication(args);
    SimulatorOptions options = application.Services
        .GetRequiredService<IOptions<SimulatorOptions>>()
        .Value;
    endpoint = options.Endpoint;
    ILogger logger = application.Services
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("SimulatorStartup");

    application.Lifetime.ApplicationStarted.Register(() =>
        SimulatorLog.Started(
            logger,
            options.Endpoint,
            options.Seed,
            options.TelemetryRateHz));

    await application.RunAsync();
    return 0;
}
catch (Exception exception) when (SimulatorStartupFailure.IsAddressInUse(exception))
{
    Console.Error.WriteLine(
        $"Le simulateur LayupPulse ne peut pas démarrer : le point d’écoute {endpoint} est déjà utilisé.");
    return 2;
}

public partial class Program;

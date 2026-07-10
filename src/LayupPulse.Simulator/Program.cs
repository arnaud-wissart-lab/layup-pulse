using LayupPulse.Simulator;
using Microsoft.Extensions.Options;

WebApplication application = SimulatorHost.BuildApplication(args);
SimulatorOptions options = application.Services.GetRequiredService<IOptions<SimulatorOptions>>().Value;
ILogger logger = application.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SimulatorStartup");

application.Lifetime.ApplicationStarted.Register(() =>
    SimulatorLog.Started(
        logger,
        options.Endpoint,
        options.Seed,
        options.TelemetryRateHz));

await application.RunAsync();

public partial class Program;

using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

namespace LayupPulse.Simulator;

/// <summary>
/// Configure la racine de composition du processus gRPC autonome.
/// </summary>
public static class SimulatorHost
{
    public static WebApplication BuildApplication(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });
        SimulatorOptions configuredOptions = builder.Configuration
            .GetSection(SimulatorOptions.SectionName)
            .Get<SimulatorOptions>() ?? new SimulatorOptions();

        builder.WebHost.UseUrls(configuredOptions.Endpoint);
        builder.WebHost.ConfigureKestrel(options =>
            options.ConfigureEndpointDefaults(endpoint => endpoint.Protocols = HttpProtocols.Http2));

        builder.Services.AddGrpc();
        builder.Services
            .AddOptions<SimulatorOptions>()
            .Bind(builder.Configuration.GetSection(SimulatorOptions.SectionName))
            .Validate(
                options => SimulatorOptions.IsValidLocalEndpoint(options.Endpoint),
                "Simulator:Endpoint doit être une adresse HTTP(S) de bouclage absolue.")
            .Validate(
                options => options.TelemetryRateHz is >= SimulatorOptions.MinimumTelemetryRateHz and
                    <= SimulatorOptions.MaximumTelemetryRateHz,
                $"Simulator:TelemetryRateHz doit être compris entre " +
                $"{SimulatorOptions.MinimumTelemetryRateHz} et {SimulatorOptions.MaximumTelemetryRateHz}.")
            .ValidateOnStart();

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<TelemetryStreamHub>();
        builder.Services.AddSingleton(serviceProvider =>
        {
            SimulatorOptions options = serviceProvider
                .GetRequiredService<IOptions<SimulatorOptions>>()
                .Value;
            TimeProvider timeProvider = serviceProvider.GetRequiredService<TimeProvider>();
            return new DeterministicMachineSimulator(
                options.Seed,
                options.TelemetryRateHz,
                timeProvider.GetUtcNow());
        });
        builder.Services.AddHostedService<TelemetryPublisher>();

        WebApplication application = builder.Build();
        application.MapGrpcService<MachineSimulatorGrpcService>();
        return application;
    }
}

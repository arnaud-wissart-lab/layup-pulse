using Microsoft.Extensions.Options;

namespace LayupPulse.Simulator;

/// <summary>
/// Cadence la simulation indépendamment du nombre de clients gRPC connectés.
/// </summary>
public sealed class TelemetryPublisher : BackgroundService
{
    private readonly DeterministicMachineSimulator _simulator;
    private readonly TelemetryStreamHub _streamHub;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _period;
    private readonly ILogger<TelemetryPublisher> _logger;

    public TelemetryPublisher(
        DeterministicMachineSimulator simulator,
        TelemetryStreamHub streamHub,
        TimeProvider timeProvider,
        IOptions<SimulatorOptions> options,
        ILogger<TelemetryPublisher> logger)
    {
        _simulator = simulator;
        _streamHub = streamHub;
        _timeProvider = timeProvider;
        _period = TimeSpan.FromSeconds(1d / options.Value.TelemetryRateHz);
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(_period, _timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (_simulator.TryAdvance(_timeProvider.GetUtcNow(), out SimulationSnapshot snapshot))
                {
                    _streamHub.Publish(snapshot);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            SimulatorLog.TelemetryPublisherStopped(_logger);
        }
        finally
        {
            _streamHub.CompleteForShutdown();
        }
    }
}

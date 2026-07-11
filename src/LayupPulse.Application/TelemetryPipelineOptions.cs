namespace LayupPulse.Application;

/// <summary>
/// Configure les bornes mémoire et les cadences indépendantes du pipeline télémétrique.
/// </summary>
public sealed class TelemetryPipelineOptions
{
    public TimeSpan UiPublicationInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    public TimeSpan AggregateInterval { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan HistoryDuration { get; set; } = TimeSpan.FromSeconds(60);

    public int HistoryCapacity { get; set; } = 3_000;

    public int AggregateCapacity { get; set; } = 60;

    public TimeSpan RateWindow { get; set; } = TimeSpan.FromSeconds(5);

    public void Validate()
    {
        if (UiPublicationInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(UiPublicationInterval));
        }

        if (AggregateInterval != TimeSpan.FromSeconds(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(AggregateInterval),
                "L’agrégation durable doit utiliser des buckets d’une seconde.");
        }

        if (HistoryDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(HistoryDuration));
        }

        if (HistoryCapacity is < 1 or > 100_000)
        {
            throw new ArgumentOutOfRangeException(nameof(HistoryCapacity));
        }

        if (AggregateCapacity is < 1 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(AggregateCapacity));
        }

        if (RateWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(RateWindow));
        }
    }
}

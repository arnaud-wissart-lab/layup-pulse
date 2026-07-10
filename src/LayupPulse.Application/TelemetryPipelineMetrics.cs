namespace LayupPulse.Application;

/// <summary>
/// Expose les compteurs bornés et les cadences observées du pipeline client.
/// </summary>
public sealed record TelemetryPipelineMetrics(
    long ReceivedSamples,
    long DroppedSamples,
    long CoalescedSamples,
    long LatestSequenceNumber,
    double AcquisitionRateHertz,
    double UiPublicationRateHertz,
    long AggregateCount,
    long ReconnectCount,
    int HistoryCapacity,
    int HistoryCount)
{
    public long DroppedOrCoalescedSamples => DroppedSamples + CoalescedSamples;

    public static TelemetryPipelineMetrics Empty(int historyCapacity) => new(
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        historyCapacity,
        0);
}

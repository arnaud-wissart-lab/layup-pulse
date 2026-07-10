namespace LayupPulse.Domain;

/// <summary>
/// Configure les seuils fictifs et les temporisations déterministes du moteur d’alarmes.
/// </summary>
public sealed class AlarmEngineOptions
{
    public double HighTemperatureThresholdCelsius { get; set; } = 165;

    public double HighTemperatureClearThresholdCelsius { get; set; } = 155;

    public TimeSpan HighTemperatureDebounce { get; set; } = TimeSpan.FromSeconds(1);

    public double LowPressureThresholdBar { get; set; } = 4;

    public double LowPressureClearThresholdBar { get; set; } = 5;

    public TimeSpan LowPressureDebounce { get; set; } = TimeSpan.FromMilliseconds(750);

    public TimeSpan ForceVariationWindow { get; set; } = TimeSpan.FromSeconds(1);

    public int ForceMinimumSampleCount { get; set; } = 6;

    public double ForceVariationThresholdNewtons { get; set; } = 100;

    public double ForceVariationClearThresholdNewtons { get; set; } = 50;

    public TimeSpan CommunicationTimeout { get; set; } = TimeSpan.FromSeconds(2);

    public int CommunicationRecoverySampleCount { get; set; } = 3;

    public int HistoryCapacity { get; set; } = 500;

    public void Validate()
    {
        if (!double.IsFinite(HighTemperatureThresholdCelsius)
            || !double.IsFinite(HighTemperatureClearThresholdCelsius)
            || HighTemperatureClearThresholdCelsius >= HighTemperatureThresholdCelsius)
        {
            throw new ArgumentOutOfRangeException(nameof(HighTemperatureClearThresholdCelsius));
        }

        if (HighTemperatureDebounce <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(HighTemperatureDebounce));
        }

        if (!double.IsFinite(LowPressureThresholdBar)
            || !double.IsFinite(LowPressureClearThresholdBar)
            || LowPressureClearThresholdBar <= LowPressureThresholdBar)
        {
            throw new ArgumentOutOfRangeException(nameof(LowPressureClearThresholdBar));
        }

        if (LowPressureDebounce <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(LowPressureDebounce));
        }

        if (ForceVariationWindow <= TimeSpan.Zero
            || ForceMinimumSampleCount < 2
            || ForceVariationClearThresholdNewtons < 0
            || ForceVariationThresholdNewtons <= ForceVariationClearThresholdNewtons)
        {
            throw new ArgumentOutOfRangeException(nameof(ForceVariationThresholdNewtons));
        }

        if (CommunicationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(CommunicationTimeout));
        }

        if (CommunicationRecoverySampleCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(CommunicationRecoverySampleCount));
        }

        if (HistoryCapacity is < 1 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(HistoryCapacity));
        }
    }
}

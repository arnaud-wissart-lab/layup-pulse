using System.Globalization;
using LayupPulse.Application;
using LayupPulse.Domain;

namespace LayupPulse.Desktop;

public sealed class HistoryRunRowViewModel(ProductionRunHistoryItem run)
{
    public Guid Id => run.Id;

    public string RecipeName => run.RecipeName;

    public string PartReference => run.PartReference;

    public string Started => FormatTimestamp(run.StartedAt);

    public string Ended => run.EndedAt is null ? "—" : FormatTimestamp(run.EndedAt.Value);

    public string Duration => run.EndedAt is null
        ? "En cours"
        : (run.EndedAt.Value - run.StartedAt).ToString(@"hh\:mm\:ss", CultureInfo.CurrentCulture);

    public string Status => run.Status switch
    {
        ProductionRunStatus.Completed => "Terminé",
        ProductionRunStatus.Aborted => "Interrompu",
        ProductionRunStatus.Faulted => "En défaut",
        _ => "En cours",
    };

    public string Completion => $"{run.CompletionPercentage:F1} %";

    public int AlarmCount => run.AlarmCount;

    public string AverageTemperature => $"{run.AverageTemperatureCelsius:F1} °C";

    public string AveragePressure => $"{run.AveragePressureBar:F2} bar";

    public string AverageForce => $"{run.AverageCompactionForceNewtons:F0} N";

    public string AverageFeedRate => $"{run.AverageFeedRateMillimetersPerSecond:F1} mm/s";

    public string MinimumHealth => $"{run.MinimumProcessHealthPercentage:F1} %";

    private static string FormatTimestamp(DateTimeOffset timestamp) => timestamp
        .ToLocalTime()
        .ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.CurrentCulture);
}

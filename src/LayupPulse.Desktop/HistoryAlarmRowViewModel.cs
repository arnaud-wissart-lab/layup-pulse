using System.Globalization;
using LayupPulse.Application;
using LayupPulse.Domain;

namespace LayupPulse.Desktop;

public sealed class HistoryAlarmRowViewModel(AlarmHistoryItem alarm)
{
    public string Code => alarm.Code switch
    {
        AlarmCode.HighTemperature => "TEMP_HIGH",
        AlarmCode.LowMaterialPressure => "PRESSURE_LOW",
        AlarmCode.UnstableCompactionForce => "FORCE_UNSTABLE",
        AlarmCode.CommunicationTimeout => "COMM_TIMEOUT",
        AlarmCode.HeadPositionError => "HEAD_POSITION_ERROR",
        _ => alarm.Code.ToString(),
    };

    public string Severity => alarm.Severity switch
    {
        AlarmSeverity.Critical => "Critique",
        AlarmSeverity.Warning => "Avertissement",
        _ => "Information",
    };

    public string Source => alarm.Source;

    public string Message => alarm.Message;

    public string Raised => FormatTimestamp(alarm.RaisedAt);

    public string Acknowledged => alarm.AcknowledgedAt is null
        ? "—"
        : FormatTimestamp(alarm.AcknowledgedAt.Value);

    public string Cleared => alarm.ClearedAt is null
        ? "—"
        : FormatTimestamp(alarm.ClearedAt.Value);

    private static string FormatTimestamp(DateTimeOffset timestamp) => timestamp
        .ToLocalTime()
        .ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.CurrentCulture);
}

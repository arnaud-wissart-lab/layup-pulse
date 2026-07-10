using System.Globalization;
using LayupPulse.Domain;

namespace LayupPulse.Desktop;

public sealed class AlarmRowViewModel
{
    public AlarmRowViewModel(AlarmEvent alarm)
    {
        Id = alarm.Id;
        Severity = alarm.Severity switch
        {
            AlarmSeverity.Critical => "Critique",
            AlarmSeverity.Warning => "Avertissement",
            _ => "Information",
        };
        Code = alarm.Code switch
        {
            AlarmCode.HighTemperature => "TEMP_HIGH",
            AlarmCode.LowMaterialPressure => "PRESSURE_LOW",
            AlarmCode.UnstableCompactionForce => "FORCE_UNSTABLE",
            AlarmCode.CommunicationTimeout => "COMM_TIMEOUT",
            AlarmCode.HeadPositionError => "HEAD_POSITION_ERROR",
            _ => alarm.Code.ToString(),
        };
        Source = alarm.Source;
        Message = alarm.Message;
        Raised = FormatTimestamp(alarm.RaisedAt);
        Acknowledged = alarm.AcknowledgedAt is null ? "—" : FormatTimestamp(alarm.AcknowledgedAt.Value);
        Cleared = alarm.ClearedAt is null ? "—" : FormatTimestamp(alarm.ClearedAt.Value);
        IsAcknowledged = alarm.AcknowledgedAt is not null;
        IsActive = alarm.ClearedAt is null;
        AcknowledgementState = IsAcknowledged ? "Acquittée" : "À acquitter";
    }

    public Guid Id { get; }

    public string Severity { get; }

    public string Code { get; }

    public string Source { get; }

    public string Message { get; }

    public string Raised { get; }

    public string Acknowledged { get; }

    public string Cleared { get; }

    public bool IsAcknowledged { get; }

    public bool IsActive { get; }

    public string AcknowledgementState { get; }

    private static string FormatTimestamp(DateTimeOffset timestamp) => timestamp
        .ToLocalTime()
        .ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.CurrentCulture);
}

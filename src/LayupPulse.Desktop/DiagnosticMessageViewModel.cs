using System.Globalization;
using LayupPulse.Application;

namespace LayupPulse.Desktop;

public sealed class DiagnosticMessageViewModel
{
    public DiagnosticMessageViewModel(MachineDiagnosticMessage message)
    {
        Timestamp = message.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.CurrentCulture);
        Level = message.Level switch
        {
            MachineDiagnosticLevel.Information => "Information",
            MachineDiagnosticLevel.Warning => "Avertissement",
            MachineDiagnosticLevel.Error => "Erreur",
            _ => "Inconnu",
        };
        Glyph = message.Level switch
        {
            MachineDiagnosticLevel.Information => "i",
            MachineDiagnosticLevel.Warning => "!",
            MachineDiagnosticLevel.Error => "×",
            _ => "·",
        };
        Message = message.Message;
    }

    public string Timestamp { get; }

    public string Level { get; }

    public string Glyph { get; }

    public string Message { get; }
}

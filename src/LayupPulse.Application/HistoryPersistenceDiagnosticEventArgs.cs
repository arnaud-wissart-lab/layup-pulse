namespace LayupPulse.Application;

/// <summary>
/// Décrit une défaillance non fatale du stockage local.
/// </summary>
public sealed class HistoryPersistenceDiagnosticEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}

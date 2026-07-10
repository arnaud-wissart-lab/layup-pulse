namespace LayupPulse.Application;

/// <summary>
/// Décrit un échec de persistance non fatal destiné aux journaux et diagnostics.
/// </summary>
public sealed class PersistenceDiagnosticEventArgs : EventArgs
{
    public PersistenceDiagnosticEventArgs(
        DateTimeOffset timestamp,
        string operation,
        string message)
    {
        Timestamp = timestamp.ToUniversalTime();
        Operation = operation;
        Message = message;
    }

    public DateTimeOffset Timestamp { get; }

    public string Operation { get; }

    public string Message { get; }
}

namespace LayupPulse.Application;

/// <summary>
/// Journalise et publie les échecs de persistance sans coupler Application à un framework de log.
/// </summary>
public interface IPersistenceDiagnosticReporter
{
    public event EventHandler<PersistenceDiagnosticEventArgs>? DiagnosticRaised;

    public void ReportFailure(string operation, Exception exception);
}

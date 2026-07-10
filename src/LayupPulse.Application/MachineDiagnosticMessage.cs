namespace LayupPulse.Application;

/// <summary>
/// Représente un événement applicatif récent destiné au diagnostic opérateur.
/// </summary>
public sealed record MachineDiagnosticMessage(
    DateTimeOffset Timestamp,
    MachineDiagnosticLevel Level,
    string Message);

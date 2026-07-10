using LayupPulse.Domain;

namespace LayupPulse.Application;

/// <summary>
/// Capture immuable de la session courante, publiable sans dépendance à une technologie d’interface.
/// </summary>
public sealed record MachineSessionState(
    MachineConnectionStatus ConnectionStatus,
    MachineSnapshot LatestSnapshot,
    TelemetrySample? LatestTelemetry,
    Guid? SessionId,
    DateTimeOffset? ConnectedAt,
    DateTimeOffset? LastSuccessfulCommunication,
    long ReceivedSampleCount,
    MachineGatewayFailureKind? LastFailureKind,
    string? LastCommunicationError,
    IReadOnlyList<MachineDiagnosticMessage> RecentDiagnostics);

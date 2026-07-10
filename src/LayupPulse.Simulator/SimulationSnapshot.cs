using LayupPulse.Domain;

namespace LayupPulse.Simulator;

/// <summary>
/// Réunit atomiquement le contexte métier et le dernier échantillon synthétique.
/// </summary>
public sealed record SimulationSnapshot(
    MachineSnapshot Machine,
    TelemetrySample Telemetry,
    FaultType? ActiveFault);

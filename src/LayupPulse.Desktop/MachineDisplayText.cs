using LayupPulse.Application;
using LayupPulse.Domain;

namespace LayupPulse.Desktop;

internal static class MachineDisplayText
{
    public static string ConnectionStatus(MachineConnectionStatus status) => status switch
    {
        MachineConnectionStatus.Disconnected => "Déconnecté",
        MachineConnectionStatus.Connecting => "Connexion…",
        MachineConnectionStatus.Connected => "Connecté",
        MachineConnectionStatus.Stale => "Télémétrie périmée",
        MachineConnectionStatus.Disconnecting => "Déconnexion…",
        _ => "Inconnu",
    };

    public static string MachineState(MachineState state) => state switch
    {
        LayupPulse.Domain.MachineState.Disconnected => "Déconnectée",
        LayupPulse.Domain.MachineState.Connecting => "Connexion",
        LayupPulse.Domain.MachineState.Ready => "Prête",
        LayupPulse.Domain.MachineState.Running => "En cycle",
        LayupPulse.Domain.MachineState.Paused => "En pause",
        LayupPulse.Domain.MachineState.Faulted => "En défaut",
        LayupPulse.Domain.MachineState.Completed => "Terminée",
        _ => "Inconnue",
    };

    public static string ConnectionTone(MachineConnectionStatus status) => status switch
    {
        MachineConnectionStatus.Connected => "Healthy",
        MachineConnectionStatus.Stale => "Warning",
        MachineConnectionStatus.Connecting or MachineConnectionStatus.Disconnecting => "Info",
        _ => "Neutral",
    };

    public static string MachineTone(MachineState state) => state switch
    {
        LayupPulse.Domain.MachineState.Running
            or LayupPulse.Domain.MachineState.Ready
            or LayupPulse.Domain.MachineState.Completed => "Healthy",
        LayupPulse.Domain.MachineState.Paused => "Warning",
        LayupPulse.Domain.MachineState.Faulted => "Danger",
        LayupPulse.Domain.MachineState.Connecting => "Info",
        _ => "Neutral",
    };

    public static string ConnectionGlyph(MachineConnectionStatus status) => status switch
    {
        MachineConnectionStatus.Connected => "●",
        MachineConnectionStatus.Stale => "!",
        MachineConnectionStatus.Connecting or MachineConnectionStatus.Disconnecting => "◌",
        _ => "○",
    };

    public static string MachineGlyph(MachineState state) => state switch
    {
        LayupPulse.Domain.MachineState.Running => "▶",
        LayupPulse.Domain.MachineState.Paused => "Ⅱ",
        LayupPulse.Domain.MachineState.Faulted => "!",
        LayupPulse.Domain.MachineState.Completed => "✓",
        LayupPulse.Domain.MachineState.Ready => "●",
        _ => "○",
    };
}

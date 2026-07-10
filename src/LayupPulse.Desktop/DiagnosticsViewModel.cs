using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LayupPulse.Application;
using LayupPulse.Infrastructure;

namespace LayupPulse.Desktop;

public sealed class DiagnosticsViewModel : ObservableObject
{
    private readonly TimeProvider _timeProvider;
    private string? _latestDiagnosticIdentity;
    private string _connectionStatus = string.Empty;
    private string _connectionTone = "Neutral";
    private string _lastSuccessfulCommunication = "Jamais";
    private string _latestSequenceNumber = "—";
    private string _receivedSampleCount = "0";
    private string _sessionId = "Aucune session";
    private string _connectionDuration = "—";
    private string _dataAge = "—";
    private string _latestCommunicationError = "Aucune";

    public DiagnosticsViewModel(
        GrpcMachineGatewayOptions gatewayOptions,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(gatewayOptions);
        _timeProvider = timeProvider;
        Endpoint = gatewayOptions.GetValidatedEndpoint().ToString();
        Version? version = typeof(App).Assembly.GetName().Version;
        ApplicationVersion = version is null ? "Indisponible" : version.ToString(3);
    }

    public string Endpoint { get; }

    public string ApplicationVersion { get; }

    public string SimulatorVersion { get; } = "Non exposée par le contrat layuppulse.v1";

    public ObservableCollection<DiagnosticMessageViewModel> RecentMessages { get; } = new();

    public string ConnectionStatus
    {
        get => _connectionStatus;
        private set => SetProperty(ref _connectionStatus, value);
    }

    public string ConnectionTone
    {
        get => _connectionTone;
        private set => SetProperty(ref _connectionTone, value);
    }

    public string LastSuccessfulCommunication
    {
        get => _lastSuccessfulCommunication;
        private set => SetProperty(ref _lastSuccessfulCommunication, value);
    }

    public string LatestSequenceNumber
    {
        get => _latestSequenceNumber;
        private set => SetProperty(ref _latestSequenceNumber, value);
    }

    public string ReceivedSampleCount
    {
        get => _receivedSampleCount;
        private set => SetProperty(ref _receivedSampleCount, value);
    }

    public string SessionId
    {
        get => _sessionId;
        private set => SetProperty(ref _sessionId, value);
    }

    public string ConnectionDuration
    {
        get => _connectionDuration;
        private set => SetProperty(ref _connectionDuration, value);
    }

    public string DataAge
    {
        get => _dataAge;
        private set => SetProperty(ref _dataAge, value);
    }

    public string LatestCommunicationError
    {
        get => _latestCommunicationError;
        private set => SetProperty(ref _latestCommunicationError, value);
    }

    public void ApplyState(MachineSessionState state)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        ConnectionStatus = MachineDisplayText.ConnectionStatus(state.ConnectionStatus);
        ConnectionTone = MachineDisplayText.ConnectionTone(state.ConnectionStatus);
        LastSuccessfulCommunication = state.LastSuccessfulCommunication?.ToLocalTime()
            .ToString("dd/MM/yyyy HH:mm:ss.fff", CultureInfo.CurrentCulture) ?? "Jamais";
        LatestSequenceNumber = state.LatestTelemetry?.SequenceNumber.ToString(CultureInfo.CurrentCulture) ?? "—";
        ReceivedSampleCount = state.ReceivedSampleCount.ToString(CultureInfo.CurrentCulture);
        SessionId = state.SessionId?.ToString("D") ?? "Aucune session";
        ConnectionDuration = state.ConnectedAt is null
            ? "—"
            : FormatDuration(now - state.ConnectedAt.Value);
        DataAge = state.LastSuccessfulCommunication is null
            ? "—"
            : $"{Math.Max(0, (now - state.LastSuccessfulCommunication.Value).TotalSeconds):F1} s";
        LatestCommunicationError = state.LastCommunicationError ?? "Aucune";

        MachineDiagnosticMessage? latest = state.RecentDiagnostics.Count == 0
            ? null
            : state.RecentDiagnostics[0];
        string? identity = latest is null
            ? null
            : $"{latest.Timestamp.UtcTicks}:{latest.Level}:{latest.Message}";
        if (identity == _latestDiagnosticIdentity)
        {
            return;
        }

        _latestDiagnosticIdentity = identity;
        RecentMessages.Clear();
        foreach (MachineDiagnosticMessage message in state.RecentDiagnostics)
        {
            RecentMessages.Add(new DiagnosticMessageViewModel(message));
        }
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}"
            : $"{duration.Minutes:D2}:{duration.Seconds:D2}";
}

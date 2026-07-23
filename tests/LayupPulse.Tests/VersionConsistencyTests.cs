using System.IO;
using System.Xml.Linq;
using LayupPulse.Application;
using LayupPulse.Desktop;
using LayupPulse.Domain;
using LayupPulse.Simulator;
using Xunit;

namespace LayupPulse.Tests;

public sealed class VersionConsistencyTests
{
    [Fact]
    public void ReleaseVersionHasOneBuildSourceAndMatchesAssembliesAndDiagnostics()
    {
        string repositoryRoot = FindRepositoryRoot();
        XDocument buildProperties = XDocument.Load(
            Path.Combine(repositoryRoot, "Directory.Build.props"));
        string version = Assert.IsType<string>(
            buildProperties.Root?.Element("PropertyGroup")?.Element("Version")?.Value);
        DiagnosticsViewModel diagnostics = new(
            new Uri("http://127.0.0.1:5055"),
            TimeProvider.System,
            new UnusedSessionService(),
            new DemoModeOptions());

        Assert.Equal("0.3.0", version);
        Assert.Equal(version, GetThreePartVersion(typeof(DiagnosticsViewModel).Assembly));
        Assert.Equal(version, GetThreePartVersion(typeof(SimulatorHost).Assembly));
        Assert.Equal(version, diagnostics.ApplicationVersion);
    }

    [Fact]
    public void PackagingCiAndReleaseDocumentsConsumeTheBuildVersion()
    {
        string repositoryRoot = FindRepositoryRoot();
        string packaging = File.ReadAllText(
            Path.Combine(repositoryRoot, "scripts", "package-demo.ps1"));
        string workflow = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "workflows", "ci.yml"));
        string changelog = File.ReadAllText(Path.Combine(repositoryRoot, "CHANGELOG.md"));
        string readiness = File.ReadAllText(
            Path.Combine(repositoryRoot, "docs", "release-readiness.md"));

        Assert.Contains("Directory.Build.props", packaging, StringComparison.Ordinal);
        Assert.Contains("$resolvedVersion = $sourceVersion", packaging, StringComparison.Ordinal);
        Assert.DoesNotContain("[string]$Version =", packaging, StringComparison.Ordinal);
        Assert.Contains("run: ./scripts/package-demo.ps1", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("package-demo.ps1 -Version", workflow, StringComparison.Ordinal);
        Assert.Contains("Version candidate prévue : `0.3.0`", changelog, StringComparison.Ordinal);
        Assert.Contains("Version candidate : `0.3.0`", readiness, StringComparison.Ordinal);
    }

    private static string GetThreePartVersion(System.Reflection.Assembly assembly) =>
        assembly.GetName().Version?.ToString(3) ?? "Indisponible";

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LayupPulse.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("La racine du dépôt LayupPulse est introuvable.");
    }

    private sealed class UnusedSessionService : IMachineSessionService
    {
        public event EventHandler<MachineSessionStateChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

        public MachineSessionState State => throw new NotSupportedException();

        public IReadOnlyList<TelemetrySample> GetTelemetryHistorySnapshot() => [];

        public Task<MachineSessionOperationResult> ConnectAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MachineSessionOperationResult> DisconnectAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MachineCommandExecutionResult> ExecuteCommandAsync(
            MachineCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<MachineSessionOperationResult> SetDemoFaultAsync(
            FaultType fault,
            bool active,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public bool AcknowledgeAlarm(Guid alarmId) => false;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

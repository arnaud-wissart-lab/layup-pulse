using System.IO;
using Xunit;

namespace LayupPulse.Tests;

public sealed class PackagedLauncherRegressionTests
{
    [Fact]
    public void LauncherCoordinatesOwnershipAndVerifiesListenerProcess()
    {
        string launcherPath = Path.Combine(
            FindRepositoryRoot(),
            "scripts",
            "package-assets",
            "Run-LayupPulse.ps1");
        string launcher = File.ReadAllText(launcherPath);

        Assert.Contains("Local\\LayupPulse.PackageLauncher.v1", launcher, StringComparison.Ordinal);
        Assert.Contains("Local\\LayupPulse.Desktop.SingleInstance.v1", launcher, StringComparison.Ordinal);
        Assert.Contains("OwningProcess -eq $Process.Id", launcher, StringComparison.Ordinal);
        Assert.Contains("Stop-OwnedProcess -Process $simulatorProcess", launcher, StringComparison.Ordinal);

        int preflight = launcher.IndexOf("$existingListener = Get-EndpointListeners", StringComparison.Ordinal);
        int simulatorStart = launcher.IndexOf(
            "$simulatorProcess = Start-Process",
            StringComparison.Ordinal);
        int readiness = launcher.IndexOf("Wait-EndpointReady", simulatorStart, StringComparison.Ordinal);
        int desktopStart = launcher.IndexOf(
            "$desktopProcess = Start-Process",
            StringComparison.Ordinal);
        Assert.True(preflight >= 0 && preflight < simulatorStart);
        Assert.True(simulatorStart < readiness && readiness < desktopStart);
    }

    [Fact]
    public void LaunchFilesUseWindowsPowerShellCompatibleUtf8Configuration()
    {
        string assetsDirectory = Path.Combine(
            FindRepositoryRoot(),
            "scripts",
            "package-assets");
        byte[] scriptBytes = File.ReadAllBytes(Path.Combine(assetsDirectory, "Run-LayupPulse.ps1"));
        string command = File.ReadAllText(Path.Combine(assetsDirectory, "Run-LayupPulse.cmd"));

        Assert.True(scriptBytes.Length >= 3);
        Assert.Equal(0xEF, scriptBytes[0]);
        Assert.Equal(0xBB, scriptBytes[1]);
        Assert.Equal(0xBF, scriptBytes[2]);
        Assert.Contains("chcp 65001", command, StringComparison.Ordinal);
        Assert.Contains("[Console]::OutputEncoding", File.ReadAllText(
            Path.Combine(assetsDirectory, "Run-LayupPulse.ps1")), StringComparison.Ordinal);
    }

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
}

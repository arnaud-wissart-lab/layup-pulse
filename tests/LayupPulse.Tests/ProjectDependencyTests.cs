using System.IO;
using System.Xml.Linq;
using Xunit;

namespace LayupPulse.Tests;

public sealed class ProjectDependencyTests
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedProjectReferences =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["LayupPulse.Domain"] = [],
            ["LayupPulse.Application"] = ["LayupPulse.Domain"],
            ["LayupPulse.Contracts"] = [],
            ["LayupPulse.Infrastructure"] =
                ["LayupPulse.Application", "LayupPulse.Contracts", "LayupPulse.Domain"],
            ["LayupPulse.Simulator"] =
                ["LayupPulse.Application", "LayupPulse.Contracts", "LayupPulse.Domain"],
            ["LayupPulse.Desktop"] =
                ["LayupPulse.Application", "LayupPulse.Domain", "LayupPulse.Infrastructure"],
            ["LayupPulse.Tests"] =
                [
                    "LayupPulse.Application",
                    "LayupPulse.Contracts",
                    "LayupPulse.Desktop",
                    "LayupPulse.Domain",
                    "LayupPulse.Infrastructure",
                    "LayupPulse.Simulator",
                ],
        };

    [Fact]
    public void ProjectReferencesFollowArchitectureBoundaries()
    {
        string repositoryRoot = FindRepositoryRoot();

        foreach ((string projectName, string[] allowedReferences) in AllowedProjectReferences)
        {
            string projectFile = FindProjectFile(repositoryRoot, projectName);
            XDocument document = XDocument.Load(projectFile);
            string[] actualReferences = document
                .Descendants("ProjectReference")
                .Select(reference => Path.GetFileNameWithoutExtension((string?)reference.Attribute("Include")))
                .Where(static name => name is not null)
                .Select(static name => name!)
                .Order(StringComparer.Ordinal)
                .ToArray();

            string[] expectedReferences = allowedReferences
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expectedReferences, actualReferences);
        }
    }

    [Fact]
    public void CodeFrameworkPackagesRemainPinnedAndConfinedToDesktopDocuments()
    {
        string repositoryRoot = FindRepositoryRoot();
        XDocument packageVersions = XDocument.Load(
            Path.Combine(repositoryRoot, "Directory.Packages.props"));
        Dictionary<string, string?> codeFrameworkVersions = packageVersions
            .Descendants("PackageVersion")
            .Where(element => ((string?)element.Attribute("Include"))?
                .StartsWith("CODE.Framework.", StringComparison.Ordinal) == true)
            .ToDictionary(
                element => (string)element.Attribute("Include")!,
                element => (string?)element.Attribute("Version"),
                StringComparer.Ordinal);

        Assert.Equal(2, codeFrameworkVersions.Count);
        Assert.Equal("6.0.0", codeFrameworkVersions["CODE.Framework.Wpf"]);
        Assert.Equal("6.0.0", codeFrameworkVersions["CODE.Framework.Wpf.Documents"]);

        foreach (string projectName in AllowedProjectReferences.Keys)
        {
            string projectFile = FindProjectFile(repositoryRoot, projectName);
            XDocument project = XDocument.Load(projectFile);
            string[] directReferences = project
                .Descendants("PackageReference")
                .Select(element => (string?)element.Attribute("Include"))
                .Where(package => package?.StartsWith(
                    "CODE.Framework.",
                    StringComparison.Ordinal) == true)
                .Select(package => package!)
                .ToArray();

            if (projectName == "LayupPulse.Desktop")
            {
                Assert.Equal(["CODE.Framework.Wpf.Documents"], directReferences);
            }
            else
            {
                Assert.Empty(directReferences);
            }
        }
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

        throw new DirectoryNotFoundException("Could not locate the LayupPulse repository root.");
    }

    private static string FindProjectFile(string repositoryRoot, string projectName)
    {
        string rootDirectory = projectName == "LayupPulse.Tests" ? "tests" : "src";
        return Path.Combine(repositoryRoot, rootDirectory, projectName, $"{projectName}.csproj");
    }
}

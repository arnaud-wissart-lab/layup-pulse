using System.IO;
using System.Xml.Linq;
using Xunit;

namespace LayupPulse.Tests;

public sealed class WpfMarkupRegressionTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Automation =
        "clr-namespace:System.Windows.Automation;assembly=PresentationCore";

    [Fact]
    public void EveryDataGridWithExplicitColumnsDisablesAutoGeneration()
    {
        string viewsDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "LayupPulse.Desktop",
            "Views");
        int explicitGridCount = 0;

        foreach (string viewPath in Directory.GetFiles(viewsDirectory, "*.xaml"))
        {
            XDocument view = XDocument.Load(viewPath);
            foreach (XElement grid in view.Descendants(Presentation + "DataGrid"))
            {
                if (grid.Element(Presentation + "DataGrid.Columns") is null)
                {
                    continue;
                }

                explicitGridCount++;
                Assert.Equal("False", (string?)grid.Attribute("AutoGenerateColumns"));
            }
        }

        Assert.Equal(5, explicitGridCount);
    }

    [Fact]
    public void DiagnosticsMessagesGridContainsOnlyExpectedColumns()
    {
        string diagnosticsPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "LayupPulse.Desktop",
            "Views",
            "DiagnosticsView.xaml");
        XDocument diagnostics = XDocument.Load(diagnosticsPath);
        XElement grid = Assert.Single(
            diagnostics.Descendants(Presentation + "DataGrid"),
            element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName.EndsWith(".AutomationId", StringComparison.Ordinal) &&
                attribute.Value == "DiagnosticsMessagesDataGrid"));
        XElement columnsContainer = Assert.IsType<XElement>(
            grid.Element(Presentation + "DataGrid.Columns"));
        XElement[] columns = columnsContainer.Elements().ToArray();

        Assert.Equal(4, columns.Length);
        Assert.Equal(
            ["Heure", string.Empty, "Niveau", "Message"],
            columns.Select(column => (string?)column.Attribute("Header") ?? string.Empty));
        Assert.Contains("Glyph", (string?)columns[1].Attribute("Binding"), StringComparison.Ordinal);
    }

    [Fact]
    public void MachineSceneHelpExplainsEverySchematicElement()
    {
        string scenePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "LayupPulse.Desktop",
            "Controls",
            "MachineSceneControl.xaml");
        XDocument scene = XDocument.Load(scenePath);
        XElement helpButton = Assert.Single(
            scene.Descendants(Presentation + "Button"),
            element => (string?)element.Attribute(Automation + "AutomationProperties.AutomationId") ==
                "MachineSceneHelpButton");
        XElement tooltip = Assert.Single(helpButton.Descendants(Presentation + "ToolTip"));
        string content = Assert.IsType<string>((object?)tooltip.Attribute("Content")?.Value);

        Assert.Equal(
            "Afficher l’aide de la visualisation 3D",
            (string?)helpButton.Attribute(Automation + "AutomationProperties.Name"));
        Assert.False(string.IsNullOrWhiteSpace(
            (string?)helpButton.Attribute(Automation + "AutomationProperties.HelpText")));
        Assert.NotEqual("False", (string?)helpButton.Attribute("IsTabStop"));
        Assert.Contains("Cube bleu : tête de dépose simulée portée par le robot.", content, StringComparison.Ordinal);
        Assert.Contains("Cylindre gris : moule ou mandrin sur lequel la fibre est déposée.", content, StringComparison.Ordinal);
        Assert.Contains("Bandes vertes : passages de fibre considérés comme déjà déposés.", content, StringComparison.Ordinal);
        Assert.Contains("Lignes grises : trajectoires restant à parcourir.", content, StringComparison.Ordinal);
        Assert.Contains(
            "La scène est schématique et ne simule pas la physique réelle du matériau.",
            content,
            StringComparison.Ordinal);
    }

    [Fact]
    public void HistoryReportButtonIsKeyboardAccessibleAndBoundToTheReportCommand()
    {
        string historyPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "LayupPulse.Desktop",
            "Views",
            "HistoryView.xaml");
        XDocument history = XDocument.Load(historyPath);
        XElement reportButton = Assert.Single(
            history.Descendants(Presentation + "Button"),
            element => (string?)element.Attribute(Automation + "AutomationProperties.AutomationId") ==
                "HistoryShowReportButton");

        Assert.Equal("{Binding ShowReportCommand}", (string?)reportButton.Attribute("Command"));
        Assert.Contains("_", (string?)reportButton.Attribute("Content"), StringComparison.Ordinal);
        Assert.Equal(
            "Afficher le rapport du cycle sélectionné",
            (string?)reportButton.Attribute(Automation + "AutomationProperties.Name"));
        Assert.False(string.IsNullOrWhiteSpace(
            (string?)reportButton.Attribute(Automation + "AutomationProperties.HelpText")));
        Assert.False(string.IsNullOrWhiteSpace((string?)reportButton.Attribute("ToolTip")));
        Assert.Contains(
            reportButton.Parent!.Elements(Presentation + "Button"),
            button => (string?)button.Attribute("Content") == "Actualiser");
    }

    [Fact]
    public void ReportPreviewUsesAFlowDocumentReaderAndAccessibleCommands()
    {
        string previewPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "LayupPulse.Desktop",
            "Reporting",
            "ProductionRunReportWindow.xaml");
        XDocument preview = XDocument.Load(previewPath);

        Assert.Empty(preview.Descendants(Presentation + "DataGrid"));
        XElement reader = Assert.Single(preview.Descendants(Presentation + "FlowDocumentReader"));
        Assert.Equal(
            "Contenu du rapport de cycle",
            (string?)reader.Attribute(Automation + "AutomationProperties.Name"));
        Assert.False(string.IsNullOrWhiteSpace(
            (string?)reader.Attribute(Automation + "AutomationProperties.HelpText")));

        XElement[] buttons = preview.Descendants(Presentation + "Button").ToArray();
        Assert.Equal(3, buttons.Length);
        Assert.Collection(
            buttons,
            button => AssertAccessibleReportButton(
                button,
                "ProductionRunReportPrintButton",
                "Imprimer le rapport de cycle"),
            button => AssertAccessibleReportButton(
                button,
                "ProductionRunReportSaveXpsButton",
                "Enregistrer le rapport de cycle en XPS"),
            button => AssertAccessibleReportButton(
                button,
                "ProductionRunReportCloseButton",
                "Fermer l’aperçu du rapport"));
        Assert.DoesNotContain(
            buttons,
            button => string.Equals(
                (string?)button.Attribute("Content"),
                "Export PDF",
                StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertAccessibleReportButton(
        XElement button,
        string automationId,
        string automationName)
    {
        Assert.Equal(
            automationId,
            (string?)button.Attribute(Automation + "AutomationProperties.AutomationId"));
        Assert.Equal(
            automationName,
            (string?)button.Attribute(Automation + "AutomationProperties.Name"));
        Assert.Contains("_", (string?)button.Attribute("Content"), StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(
            (string?)button.Attribute(Automation + "AutomationProperties.HelpText")));
        Assert.False(string.IsNullOrWhiteSpace((string?)button.Attribute("ToolTip")));
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

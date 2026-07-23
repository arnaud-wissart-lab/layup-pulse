using CODE.Framework.Wpf.Documents;
using Microsoft.Win32;

namespace LayupPulse.Desktop.Reporting;

/// <summary>
/// Isole les dialogues Windows afin que leur annulation reste testable sans interaction.
/// </summary>
public interface IProductionRunReportDialogs
{
    public void ShowPrintDialog(FlowDocumentEx document);

    public string? ShowXpsSaveDialog(string suggestedFileName);
}

/// <summary>
/// Ouvre les dialogues Windows utilisés par le rapport.
/// </summary>
public sealed class WindowsProductionRunReportDialogs : IProductionRunReportDialogs
{
    public void ShowPrintDialog(FlowDocumentEx document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Print();
    }

    public string? ShowXpsSaveDialog(string suggestedFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);

        SaveFileDialog dialog = new()
        {
            AddExtension = true,
            CheckPathExists = true,
            DefaultExt = ".xps",
            FileName = suggestedFileName,
            Filter = "Document XPS (*.xps)|*.xps",
            OverwritePrompt = true,
            Title = "Enregistrer le rapport de cycle en XPS",
        };
        System.Windows.Window? owner = System.Windows.Application.Current?.Windows
            .OfType<System.Windows.Window>()
            .FirstOrDefault(window => window.IsActive)
            ?? System.Windows.Application.Current?.MainWindow;
        bool? result = owner is null
            ? dialog.ShowDialog()
            : dialog.ShowDialog(owner);
        return result == true ? dialog.FileName : null;
    }
}

/// <summary>
/// Orchestre l’impression et l’export XPS d’un rapport affiché.
/// </summary>
public sealed class ProductionRunReportOutputService
{
    private readonly IProductionRunReportDialogs _dialogs;

    public ProductionRunReportOutputService(IProductionRunReportDialogs dialogs)
    {
        _dialogs = dialogs;
    }

    public void Print(FlowDocumentEx document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _dialogs.ShowPrintDialog(document);
    }

    public void SaveAsXps(FlowDocumentEx document, string suggestedFileName)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);

        string? fileName = _dialogs.ShowXpsSaveDialog(suggestedFileName);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        PrintHelper.SaveAsXps(document, fileName, document.PageWidth, document.PageHeight);
    }
}

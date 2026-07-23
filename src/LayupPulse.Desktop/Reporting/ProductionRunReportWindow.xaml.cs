using System.Windows;
using CODE.Framework.Wpf.Documents;

namespace LayupPulse.Desktop.Reporting;

public partial class ProductionRunReportWindow : Window
{
    private readonly FlowDocumentEx _document;
    private readonly ProductionRunReportOutputService _outputService;
    private readonly string _suggestedFileName;

    public ProductionRunReportWindow(
        FlowDocumentEx document,
        ProductionRunReportOutputService outputService,
        string suggestedFileName)
    {
        _document = document;
        _outputService = outputService;
        _suggestedFileName = suggestedFileName;
        InitializeComponent();
        ReportReader.Document = document;
    }

    private void OnPrintClicked(object sender, RoutedEventArgs eventArgs) =>
        RunOutputOperation(
            () => _outputService.Print(_document),
            "L’impression du rapport a échoué.");

    private void OnSaveAsXpsClicked(object sender, RoutedEventArgs eventArgs) =>
        RunOutputOperation(
            () => _outputService.SaveAsXps(_document, _suggestedFileName),
            "L’enregistrement du rapport XPS a échoué.");

    private void RunOutputOperation(Action operation, string failureMessage)
    {
        try
        {
            operation();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"{failureMessage}\n\n{exception.Message}",
                "Rapport de cycle",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}

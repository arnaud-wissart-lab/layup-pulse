using LayupPulse.Application;

namespace LayupPulse.Desktop.Reporting;

/// <summary>
/// Construit puis ouvre l’aperçu WPF du rapport de cycle.
/// </summary>
public sealed class ProductionRunReportPresenter : IProductionRunReportPresenter
{
    private static readonly string ApplicationVersion =
        typeof(ProductionRunReportPresenter).Assembly.GetName().Version?.ToString(3)
        ?? "Indisponible";

    private readonly TimeProvider _timeProvider;
    private readonly ProductionRunReportOutputService _outputService;

    public ProductionRunReportPresenter(
        TimeProvider timeProvider,
        ProductionRunReportOutputService outputService)
    {
        _timeProvider = timeProvider;
        _outputService = outputService;
    }

    public void Show(ProductionRunHistoryDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        ProductionRunReport report = ProductionRunReportFactory.Create(
            details,
            _timeProvider.GetUtcNow(),
            ApplicationVersion);
        CODE.Framework.Wpf.Documents.FlowDocumentEx document =
            ProductionRunReportFlowDocumentFactory.Create(report);
        ProductionRunReportWindow preview = new(
            document,
            _outputService,
            $"LayupPulse-cycle-{details.Run.Id:N}.xps");
        System.Windows.Window owner = System.Windows.Application.Current?.MainWindow
            ?? throw new InvalidOperationException(
                "La fenêtre principale doit être disponible avant d’ouvrir le rapport.");
        preview.Owner = owner;
        _ = preview.ShowDialog();
    }
}

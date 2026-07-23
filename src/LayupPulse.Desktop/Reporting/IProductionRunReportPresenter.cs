using LayupPulse.Application;

namespace LayupPulse.Desktop.Reporting;

/// <summary>
/// Présente le rapport correspondant aux détails complets d’un cycle.
/// </summary>
public interface IProductionRunReportPresenter
{
    public void Show(ProductionRunHistoryDetails details);
}

using System.Runtime.ExceptionServices;
using CODE.Framework.Wpf.Documents;
using LayupPulse.Desktop.Reporting;
using Xunit;

namespace LayupPulse.Tests;

public sealed class ProductionRunReportOutputServiceTests
{
    [Fact]
    public void ClosedPrintDialogAndCancelledSaveDialogReturnWithoutError()
    {
        RunInSta(() =>
        {
            CancelledReportDialogs dialogs = new();
            ProductionRunReportOutputService output = new(dialogs);
            FlowDocumentEx document = new()
            {
                PageWidth = 816,
                PageHeight = 1056,
            };

            Exception? printFailure = Record.Exception(() => output.Print(document));
            Exception? saveFailure = Record.Exception(
                () => output.SaveAsXps(document, "rapport-cycle.xps"));

            Assert.Null(printFailure);
            Assert.Null(saveFailure);
            Assert.Equal(1, dialogs.PrintDialogCount);
            Assert.Equal(1, dialogs.SaveDialogCount);
        });
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private sealed class CancelledReportDialogs : IProductionRunReportDialogs
    {
        public int PrintDialogCount { get; private set; }

        public int SaveDialogCount { get; private set; }

        public void ShowPrintDialog(FlowDocumentEx document)
        {
            PrintDialogCount++;
        }

        public string? ShowXpsSaveDialog(string suggestedFileName)
        {
            SaveDialogCount++;
            return null;
        }
    }
}

using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using CODE.Framework.Wpf.Documents;
using LayupPulse.Application;
using LayupPulse.Desktop.Reporting;
using LayupPulse.Domain;
using Xunit;

namespace LayupPulse.Tests;

public sealed class ProductionRunReportFlowDocumentFactoryTests
{
    [Fact]
    public void CreatesPrintableAndXpsExportableDocumentWithSafetyAndPageMetadata()
    {
        RunInSta(() =>
        {
            DateTimeOffset startedAt =
                new(2026, 7, 23, 8, 15, 0, TimeSpan.Zero);
            ProductionRunHistoryDetails details = new(
                new ProductionRunHistoryItem(
                    Guid.Parse("12345678-1234-1234-1234-1234567890ab"),
                    "Wing Panel Demo",
                    "LP-WING-DEMO-001",
                    startedAt,
                    startedAt.AddMinutes(12),
                    ProductionRunStatus.Completed,
                    100,
                    0,
                    145,
                    6,
                    450,
                    118,
                    96),
                [],
                []);
            ProductionRunReport report = ProductionRunReportFactory.Create(
                details,
                startedAt.AddMinutes(15),
                "0.3.0");
            FlowDocumentEx document = ProductionRunReportFlowDocumentFactory.Create(report);

            Assert.Equal(report.Title, document.Title);
            Assert.Equal(Brushes.White, document.Background);
            Assert.IsType<Grid>(document.PageHeader);
            Grid footer = Assert.IsType<Grid>(document.PageFooter);
            TextBlock pagination = Assert.IsType<TextBlock>(footer.Children[1]);
            Assert.Contains(pagination.Inlines, inline => inline is CurrentPage);
            Assert.Contains(pagination.Inlines, inline => inline is PageCount);
            Assert.Equal("DONNÉES SIMULÉES", Assert.IsType<TextBlock>(document.PrintWatermark).Text);

            BlockUIContainer warningContainer = Assert.Single(
                document.Blocks.OfType<BlockUIContainer>());
            Border warningBorder = Assert.IsType<Border>(warningContainer.Child);
            Assert.Equal(
                report.Warning,
                Assert.IsType<TextBlock>(warningBorder.Child).Text);
            string documentText = new TextRange(document.ContentStart, document.ContentEnd).Text;
            Assert.Contains(report.Title, documentText, StringComparison.Ordinal);
            Assert.Contains("Généré le", documentText, StringComparison.Ordinal);
            Assert.Contains("Version de LayupPulse", documentText, StringComparison.Ordinal);
            Assert.Contains(report.ApplicationVersion, documentText, StringComparison.Ordinal);

            string xpsPath = Path.Combine(
                Path.GetTempPath(),
                "LayupPulse.Tests",
                $"{Guid.NewGuid():N}.xps");
            Directory.CreateDirectory(Path.GetDirectoryName(xpsPath)!);
            try
            {
                document.SaveAsXps(xpsPath);

                Assert.True(File.Exists(xpsPath));
                Assert.True(new FileInfo(xpsPath).Length > 0);
            }
            finally
            {
                File.Delete(xpsPath);
            }
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
}

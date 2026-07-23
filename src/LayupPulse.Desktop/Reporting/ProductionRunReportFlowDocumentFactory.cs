using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using CODE.Framework.Wpf.Documents;
using LayupPulse.Application;
using LayupPulse.Domain;

namespace LayupPulse.Desktop.Reporting;

/// <summary>
/// Isole la conversion du modèle de rapport vers le document imprimable WPF.
/// </summary>
public static class ProductionRunReportFlowDocumentFactory
{
    private static readonly CultureInfo ReportCulture = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly Brush PrimaryTextBrush = CreateFrozenBrush(24, 36, 48);
    private static readonly Brush SecondaryTextBrush = CreateFrozenBrush(71, 85, 99);
    private static readonly Brush AccentBrush = CreateFrozenBrush(3, 105, 161);
    private static readonly Brush BorderBrush = CreateFrozenBrush(203, 213, 225);
    private static readonly Brush WarningBrush = CreateFrozenBrush(185, 28, 28);
    private static readonly Brush WarningBackgroundBrush = CreateFrozenBrush(254, 226, 226);
    private static readonly Brush TableHeaderBrush = CreateFrozenBrush(226, 232, 240);

    public static FlowDocumentEx Create(ProductionRunReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        FlowDocumentEx document = new()
        {
            Title = report.Title,
            Background = Brushes.White,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            Foreground = PrimaryTextBrush,
            PageWidth = 816,
            PageHeight = 1056,
            PagePadding = new Thickness(48),
            ColumnWidth = double.PositiveInfinity,
            PrintMargin = new Thickness(48),
            PageHeader = CreatePageHeader(report),
            PageFooter = CreatePageFooter(report),
            PrintWatermark = CreateWatermark(),
        };

        document.Blocks.Add(new Paragraph(new Run(report.Title))
        {
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Foreground = PrimaryTextBrush,
            Margin = new Thickness(0, 0, 0, 4),
        });
        document.Blocks.Add(new Paragraph(new Run($"Cycle {report.ProductionRunId:D}"))
        {
            FontSize = 10,
            Foreground = SecondaryTextBrush,
            Margin = new Thickness(0, 0, 0, 16),
        });
        document.Blocks.Add(CreateVisibleWarning(report.Warning));

        AddSectionTitle(document, "Identification");
        document.Blocks.Add(CreateDefinitionTable(
        [
            ("Identifiant du cycle", report.ProductionRunId.ToString("D", ReportCulture)),
            ("Recette", report.RecipeName),
            ("Référence pièce", report.PartReference),
        ]));

        AddSectionTitle(document, "Déroulement du cycle");
        document.Blocks.Add(CreateDefinitionTable(
        [
            ("Début", FormatTimestamp(report.StartedAt)),
            ("Fin", report.EndedAt is null ? "Non disponible" : FormatTimestamp(report.EndedAt.Value)),
            ("Durée", FormatDuration(report.Duration)),
            ("État final", FormatStatus(report.FinalStatus)),
            ("Progression", FormatValue(report.CompletionPercentage, "F1", "%")),
            ("Nombre d’alarmes", report.AlarmCount.ToString("N0", ReportCulture)),
        ]));

        AddSectionTitle(document, "Indicateurs synthétiques");
        document.Blocks.Add(CreateDefinitionTable(
        [
            ("Température moyenne", FormatValue(report.AverageTemperatureCelsius, "F1", "°C")),
            ("Pression moyenne", FormatValue(report.AveragePressureBar, "F2", "bar")),
            ("Force moyenne", FormatValue(report.AverageCompactionForceNewtons, "F0", "N")),
            ("Débit moyen", FormatValue(
                report.AverageFeedRateMillimetersPerSecond,
                "F1",
                "mm/s")),
            ("Santé minimale", FormatValue(report.MinimumProcessHealthPercentage, "F1", "%")),
        ]));

        AddTelemetrySummary(document, report.TelemetrySummary);
        AddAlarmDetails(document, report);

        AddSectionTitle(document, "Métadonnées");
        document.Blocks.Add(CreateDefinitionTable(
        [
            ("Généré le", FormatTimestamp(report.GeneratedAt)),
            ("Version de LayupPulse", report.ApplicationVersion),
        ]));

        return document;
    }

    private static void AddTelemetrySummary(
        FlowDocument document,
        ProductionRunReportTelemetrySummary? summary)
    {
        AddSectionTitle(document, "Résumé des agrégats télémétriques");
        if (summary is null)
        {
            document.Blocks.Add(CreateInformationParagraph(
                "Aucun agrégat télémétrique n’est disponible pour ce cycle."));
            return;
        }

        document.Blocks.Add(CreateDefinitionTable(
        [
            ("Période", $"{FormatTimestamp(summary.PeriodStartedAt)} — " +
                FormatTimestamp(summary.PeriodEndedAt)),
            ("Nombre de buckets", summary.BucketCount.ToString("N0", ReportCulture)),
            ("Nombre total d’échantillons", summary.TotalSampleCount.ToString("N0", ReportCulture)),
            ("Plage de température", FormatRange(summary.TemperatureCelsiusRange, "F1", "°C")),
            ("Plage de pression", FormatRange(summary.PressureBarRange, "F2", "bar")),
            ("Plage de force", FormatRange(summary.CompactionForceNewtonsRange, "F0", "N")),
            ("Plage des moyennes de débit par bucket", FormatRange(
                summary.AverageFeedRateMillimetersPerSecondRange,
                "F1",
                "mm/s")),
        ]));
    }

    private static void AddAlarmDetails(FlowDocument document, ProductionRunReport report)
    {
        AddSectionTitle(document, "Alarmes détaillées");
        if (report.OmittedAlarmCount > 0)
        {
            string alarmLabel = report.OmittedAlarmCount == 1 ? "alarme est omise" : "alarmes sont omises";
            document.Blocks.Add(new Paragraph(new Run(
                $"{report.OmittedAlarmCount:N0} {alarmLabel} afin de limiter le rapport à " +
                $"{ProductionRunReportFactory.MaximumDetailedAlarmCount} alarmes détaillées."))
            {
                FontWeight = FontWeights.SemiBold,
                Foreground = WarningBrush,
                Margin = new Thickness(0, 0, 0, 8),
            });
        }

        if (report.DetailedAlarms.IsEmpty)
        {
            document.Blocks.Add(CreateInformationParagraph(
                "Aucune alarme détaillée n’est disponible pour ce cycle."));
            return;
        }

        Table table = new()
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 0, 0, 12),
        };
        table.Columns.Add(new TableColumn { Width = new GridLength(90) });
        table.Columns.Add(new TableColumn { Width = new GridLength(80) });
        table.Columns.Add(new TableColumn { Width = new GridLength(115) });
        table.Columns.Add(new TableColumn());

        TableRowGroup rows = new();
        rows.Rows.Add(CreateAlarmHeaderRow());
        foreach (AlarmHistoryItem alarm in report.DetailedAlarms)
        {
            rows.Rows.Add(CreateAlarmRow(alarm));
        }

        table.RowGroups.Add(rows);
        document.Blocks.Add(table);
    }

    private static TableRow CreateAlarmHeaderRow()
    {
        TableRow row = new()
        {
            Background = TableHeaderBrush,
            FontWeight = FontWeights.SemiBold,
        };
        row.Cells.Add(CreateTableCell("Alarme"));
        row.Cells.Add(CreateTableCell("Sévérité"));
        row.Cells.Add(CreateTableCell("Horodatages"));
        row.Cells.Add(CreateTableCell("Détail"));
        return row;
    }

    private static TableRow CreateAlarmRow(AlarmHistoryItem alarm)
    {
        string timestamps = $"Levée : {FormatTimestamp(alarm.RaisedAt)}\n" +
            $"Acquittée : {FormatOptionalTimestamp(alarm.AcknowledgedAt)}\n" +
            $"Terminée : {FormatOptionalTimestamp(alarm.ClearedAt)}";
        string detail = $"{alarm.Message}\nSource : {alarm.Source}\n" +
            $"Identifiant : {alarm.Id:D}";
        TableRow row = new();
        row.Cells.Add(CreateTableCell(FormatAlarmCode(alarm.Code)));
        row.Cells.Add(CreateTableCell(FormatAlarmSeverity(alarm.Severity)));
        row.Cells.Add(CreateTableCell(timestamps));
        row.Cells.Add(CreateTableCell(detail));
        return row;
    }

    private static TableCell CreateTableCell(string value) => new(
        new Paragraph(new Run(value))
        {
            Margin = new Thickness(0),
        })
    {
        BorderBrush = BorderBrush,
        BorderThickness = new Thickness(0.5),
        Padding = new Thickness(5),
    };

    private static BlockUIContainer CreateVisibleWarning(string warning) => new(
        new Border
        {
            Background = WarningBackgroundBrush,
            BorderBrush = WarningBrush,
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(14),
            Child = new TextBlock
            {
                Text = warning,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = WarningBrush,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            },
        })
    {
        Margin = new Thickness(0, 0, 0, 18),
    };

    private static Grid CreatePageHeader(ProductionRunReport report)
    {
        Grid header = new()
        {
            Margin = new Thickness(0, 0, 0, 8),
        };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        TextBlock title = new()
        {
            Text = report.Title,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = SecondaryTextBrush,
        };
        TextBlock runId = new()
        {
            Text = report.ProductionRunId.ToString("D", ReportCulture),
            FontSize = 9,
            Foreground = SecondaryTextBrush,
        };
        Grid.SetColumn(runId, 1);
        header.Children.Add(title);
        header.Children.Add(runId);
        return header;
    }

    private static Grid CreatePageFooter(ProductionRunReport report)
    {
        Grid footer = new()
        {
            Margin = new Thickness(0, 8, 0, 0),
        };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        TextBlock metadata = new()
        {
            Text = $"Généré le {FormatTimestamp(report.GeneratedAt)} · " +
                $"LayupPulse {report.ApplicationVersion}",
            FontSize = 9,
            Foreground = SecondaryTextBrush,
        };
        TextBlock pagination = new()
        {
            FontSize = 9,
            Foreground = SecondaryTextBrush,
        };
        pagination.Inlines.Add(new Run("Page "));
        pagination.Inlines.Add(new CurrentPage());
        pagination.Inlines.Add(new Run(" / "));
        pagination.Inlines.Add(new PageCount());
        Grid.SetColumn(pagination, 1);
        footer.Children.Add(metadata);
        footer.Children.Add(pagination);
        return footer;
    }

    private static TextBlock CreateWatermark() => new()
    {
        Text = "DONNÉES SIMULÉES",
        FontSize = 64,
        FontWeight = FontWeights.Bold,
        Foreground = WarningBrush,
        Opacity = 0.08,
        LayoutTransform = new RotateTransform(-35),
        IsHitTestVisible = false,
    };

    private static void AddSectionTitle(FlowDocument document, string title) =>
        document.Blocks.Add(new Paragraph(new Run(title))
        {
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = AccentBrush,
            KeepWithNext = true,
            Margin = new Thickness(0, 12, 0, 6),
        });

    private static Table CreateDefinitionTable(
        IEnumerable<(string Label, string Value)> definitions)
    {
        Table table = new()
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 0, 0, 8),
        };
        table.Columns.Add(new TableColumn { Width = new GridLength(230) });
        table.Columns.Add(new TableColumn());

        TableRowGroup rows = new();
        foreach ((string label, string value) in definitions)
        {
            TableRow row = new();
            TableCell labelCell = CreateTableCell(label);
            labelCell.FontWeight = FontWeights.SemiBold;
            labelCell.Foreground = SecondaryTextBrush;
            row.Cells.Add(labelCell);
            row.Cells.Add(CreateTableCell(value));
            rows.Rows.Add(row);
        }

        table.RowGroups.Add(rows);
        return table;
    }

    private static Paragraph CreateInformationParagraph(string message) => new(new Run(message))
    {
        Foreground = SecondaryTextBrush,
        FontStyle = FontStyles.Italic,
        Margin = new Thickness(0, 0, 0, 8),
    };

    private static string FormatTimestamp(DateTimeOffset timestamp) => timestamp
        .ToLocalTime()
        .ToString("dd/MM/yyyy HH:mm:ss zzz", ReportCulture);

    private static string FormatOptionalTimestamp(DateTimeOffset? timestamp) =>
        timestamp is null ? "Non disponible" : FormatTimestamp(timestamp.Value);

    private static string FormatDuration(TimeSpan? duration)
    {
        if (duration is null)
        {
            return "Non disponible";
        }

        TimeSpan value = duration.Value;
        return value.Days > 0
            ? $"{value.Days:N0} j {value:hh\\:mm\\:ss}"
            : value.ToString(@"hh\:mm\:ss", ReportCulture);
    }

    private static string FormatStatus(ProductionRunStatus status) => status switch
    {
        ProductionRunStatus.Completed => "Terminé",
        ProductionRunStatus.Aborted => "Interrompu",
        ProductionRunStatus.Faulted => "En défaut",
        _ => "En cours",
    };

    private static string FormatAlarmCode(AlarmCode code) => code switch
    {
        AlarmCode.HighTemperature => "TEMP_HIGH",
        AlarmCode.LowMaterialPressure => "PRESSURE_LOW",
        AlarmCode.UnstableCompactionForce => "FORCE_UNSTABLE",
        AlarmCode.CommunicationTimeout => "COMM_TIMEOUT",
        AlarmCode.HeadPositionError => "HEAD_POSITION_ERROR",
        _ => code.ToString(),
    };

    private static string FormatAlarmSeverity(AlarmSeverity severity) => severity switch
    {
        AlarmSeverity.Critical => "Critique",
        AlarmSeverity.Warning => "Avertissement",
        _ => "Information",
    };

    private static string FormatValue(double value, string format, string unit) =>
        $"{value.ToString(format, ReportCulture)} {unit}";

    private static string FormatRange(
        ProductionRunReportRange range,
        string format,
        string unit) =>
        $"{range.Minimum.ToString(format, ReportCulture)}–" +
        $"{range.Maximum.ToString(format, ReportCulture)} {unit}";

    private static SolidColorBrush CreateFrozenBrush(byte red, byte green, byte blue)
    {
        SolidColorBrush brush = new(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}

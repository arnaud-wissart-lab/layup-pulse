using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LayupPulse.Domain;
using ScottPlot;
using ScottPlot.WPF;

namespace LayupPulse.Desktop.Controls;

public partial class TelemetryChartsControl : UserControl
{
    private const int MaximumRenderedPoints = 600;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(200);

    public static readonly DependencyProperty HistoryProperty = DependencyProperty.Register(
        nameof(History),
        typeof(IReadOnlyList<TelemetrySample>),
        typeof(TelemetryChartsControl),
        new PropertyMetadata(Array.Empty<TelemetrySample>(), OnHistoryChanged));

    private readonly DispatcherTimer _refreshTimer;
    private WpfPlot? _temperaturePlot;
    private WpfPlot? _pressurePlot;
    private WpfPlot? _forcePlot;
    private WpfPlot? _feedRatePlot;
    private bool _refreshPending;
    private bool _isLoaded;

    public TelemetryChartsControl()
    {
        InitializeComponent();
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = RefreshInterval,
        };
        _refreshTimer.Tick += OnRefreshTick;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        try
        {
            InitializePlots();
        }
        catch (Exception exception)
        {
            ShowFallback(exception);
        }
    }

    public IReadOnlyList<TelemetrySample> History
    {
        get => (IReadOnlyList<TelemetrySample>)GetValue(HistoryProperty);
        set => SetValue(HistoryProperty, value);
    }

    private static void OnHistoryChanged(DependencyObject sender, DependencyPropertyChangedEventArgs eventArgs) =>
        ((TelemetryChartsControl)sender)._refreshPending = true;

    private void InitializePlots()
    {
        _temperaturePlot = CreatePlot("Température chauffage", "°C");
        _pressurePlot = CreatePlot("Pression matériau", "bar");
        _forcePlot = CreatePlot("Force de compactage", "N");
        _feedRatePlot = CreatePlot("Débit réel", "mm/s");
        TemperatureHost.Content = _temperaturePlot;
        PressureHost.Content = _pressurePlot;
        ForceHost.Content = _forcePlot;
        FeedRateHost.Content = _feedRatePlot;
    }

    private static WpfPlot CreatePlot(string title, string unit)
    {
        WpfPlot plot = new();
        new ScottPlot.PlotStyles.Dark().Apply(plot.Plot);
        plot.Plot.Title(title, 11);
        plot.Plot.XLabel("secondes", 9);
        plot.Plot.YLabel(unit, 9);
        plot.Plot.Axes.SetLimitsX(-60, 0);
        return plot;
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        _isLoaded = true;
        _refreshPending = true;
        _refreshTimer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs eventArgs)
    {
        _isLoaded = false;
        _refreshTimer.Stop();
    }

    private void OnRefreshTick(object? sender, EventArgs eventArgs)
    {
        if (!_isLoaded || !_refreshPending)
        {
            return;
        }

        _refreshPending = false;
        RefreshPlots();
    }

    private void RefreshPlots()
    {
        if (_temperaturePlot is null || _pressurePlot is null || _forcePlot is null || _feedRatePlot is null)
        {
            return;
        }

        IReadOnlyList<TelemetrySample> history = History;
        int step = Math.Max(1, (int)Math.Ceiling(history.Count / (double)MaximumRenderedPoints));
        int pointCount = history.Count == 0 ? 0 : ((history.Count - 1) / step) + 1;
        double[] seconds = new double[pointCount];
        double[] temperature = new double[pointCount];
        double[] pressure = new double[pointCount];
        double[] force = new double[pointCount];
        double[] feedRate = new double[pointCount];
        DateTimeOffset latestTimestamp = history.Count == 0 ? default : history[^1].Timestamp;

        int targetIndex = 0;
        for (int sourceIndex = 0; sourceIndex < history.Count; sourceIndex += step)
        {
            TelemetrySample sample = history[sourceIndex];
            seconds[targetIndex] = Math.Max(-60, (sample.Timestamp - latestTimestamp).TotalSeconds);
            temperature[targetIndex] = sample.HeaterTemperatureCelsius;
            pressure[targetIndex] = sample.MaterialPressureBar;
            force[targetIndex] = sample.CompactionForceNewtons;
            feedRate[targetIndex] = sample.ActualFeedRateMillimetersPerSecond;
            targetIndex++;
        }

        UpdatePlot(_temperaturePlot, seconds, temperature, Colors.Orange, threshold: 165, Colors.Red);
        UpdatePlot(_pressurePlot, seconds, pressure, Colors.Cyan, threshold: 4, Colors.Yellow);
        UpdatePlot(_forcePlot, seconds, force, Colors.Green, threshold: null, Colors.Gray);
        double? targetFeedRate = history.Count == 0
            ? null
            : history[^1].TargetFeedRateMillimetersPerSecond;
        UpdatePlot(_feedRatePlot, seconds, feedRate, Colors.Blue, targetFeedRate, Colors.Gray);
    }

    private static void UpdatePlot(
        WpfPlot plot,
        double[] seconds,
        double[] values,
        ScottPlot.Color color,
        double? threshold,
        ScottPlot.Color thresholdColor)
    {
        plot.Plot.Clear();
        if (values.Length > 0)
        {
            var signal = plot.Plot.Add.ScatterLine(seconds, values);
            signal.Color = color;
            signal.LineWidth = 1.8f;
        }

        if (threshold is not null)
        {
            var thresholdLine = plot.Plot.Add.HorizontalLine(threshold.Value);
            thresholdLine.Color = thresholdColor;
            thresholdLine.LinePattern = LinePattern.Dashed;
            thresholdLine.LineWidth = 1;
        }

        plot.Plot.Axes.SetLimitsX(-60, 0);
        plot.Plot.Axes.AutoScaleY();
        plot.Refresh();
    }

    private void ShowFallback(Exception exception)
    {
        ChartsRoot.Children.Clear();
        ChartsRoot.RowDefinitions.Clear();
        ChartsRoot.ColumnDefinitions.Clear();
        ChartsRoot.Children.Add(new TextBlock
        {
            Text = $"Graphiques indisponibles : {exception.Message}",
            Foreground = (System.Windows.Media.Brush)FindResource("SecondaryTextBrush"),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        });
    }
}

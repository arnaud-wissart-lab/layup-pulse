using System.Windows;
using System.Windows.Threading;

namespace LayupPulse.Desktop;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _viewModel;
    private readonly DispatcherTimer _clockTimer;

    public MainWindow(ShellViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;

        _clockTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _clockTimer.Tick += OnClockTick;
        _clockTimer.Start();
        Closed += OnClosed;
    }

    private void OnClockTick(object? sender, EventArgs eventArgs) => _viewModel.RefreshClock();

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        _clockTimer.Stop();
        _clockTimer.Tick -= OnClockTick;
        Closed -= OnClosed;
    }
}

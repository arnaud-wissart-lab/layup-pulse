using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using LayupPulse.Application;
using LayupPulse.Desktop.Reporting;
using LayupPulse.Infrastructure;
using LayupPulse.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LayupPulse.Desktop;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "Le cycle de vie WPF libère explicitement le coordinateur dans OnExit.")]
public partial class App : System.Windows.Application
{
    private static readonly Action<ILogger, Exception?> ShutdownTimeoutLog = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(1, nameof(ShutdownTimeoutLog)),
        "Le délai d’arrêt propre de l’application a été dépassé.");
    private static readonly Action<ILogger, Exception?> ShutdownFailureLog = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(2, nameof(ShutdownFailureLog)),
        "Une erreur est survenue pendant l’arrêt de l’application.");
    private IHost? _host;
    private MainWindow? _mainWindow;
    private DesktopSingleInstanceCoordinator? _singleInstanceCoordinator;
    private bool _shutdownStarted;
    private bool _shutdownCompleted;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceCoordinator = new DesktopSingleInstanceCoordinator();
        if (!_singleInstanceCoordinator.IsPrimaryInstance)
        {
            bool activated = _singleInstanceCoordinator.TryActivateExistingInstance(
                TimeSpan.FromSeconds(2));
            if (!activated)
            {
                MessageBox.Show(
                    "LayupPulse est déjà ouvert dans cette session Windows.",
                    "LayupPulse déjà ouvert",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            Shutdown(0);
            return;
        }

        _singleInstanceCoordinator.StartListening(ActivateMainWindow);

        try
        {
            _host = BuildHost(e.Args);
            await _host.StartAsync();

            _mainWindow = _host.Services.GetRequiredService<MainWindow>();
            _mainWindow.Closing += OnMainWindowClosing;
            MainWindow = _mainWindow;
            _mainWindow.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"LayupPulse n’a pas pu démarrer.\n\n{exception.Message}",
                "Erreur de démarrage",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            await ShutdownHostAsync();
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceCoordinator?.Dispose();
        _singleInstanceCoordinator = null;
        base.OnExit(e);
    }

    private static IHost BuildHost(string[] arguments)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = arguments,
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Logging.AddDebug();

        GrpcMachineGatewayOptions gatewayOptions = new();
        builder.Configuration.GetSection("Machine").Bind(gatewayOptions);
        Uri machineEndpoint = gatewayOptions.GetValidatedEndpoint();

        MachineSessionOptions sessionOptions = new();
        builder.Configuration.GetSection("Session").Bind(sessionOptions);
        DemoModeOptions demoModeOptions = new();
        builder.Configuration.GetSection("DemoMode").Bind(demoModeOptions);

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddLayupPulseHistory();
        builder.Services.AddSingleton(gatewayOptions);
        builder.Services.AddSingleton(machineEndpoint);
        builder.Services.AddSingleton(sessionOptions);
        builder.Services.AddSingleton(demoModeOptions);
        builder.Services.AddSingleton<GrpcMachineGateway>();
        builder.Services.AddSingleton<IMachineGateway>(services =>
            services.GetRequiredService<GrpcMachineGateway>());
        builder.Services.AddSingleton<IDemoFaultGateway>(services =>
            services.GetRequiredService<GrpcMachineGateway>());
        builder.Services.AddSingleton<IMachineSessionService, MachineSessionService>();
        builder.Services.AddSingleton<IUiDispatcher, WpfUiDispatcher>();
        builder.Services.AddSingleton<OverviewViewModel>();
        builder.Services.AddSingleton<DiagnosticsViewModel>();
        builder.Services.AddSingleton<AlarmsViewModel>();
        builder.Services.AddSingleton<IProductionRunReportDialogs, WindowsProductionRunReportDialogs>();
        builder.Services.AddSingleton<ProductionRunReportOutputService>();
        builder.Services.AddSingleton<IProductionRunReportPresenter, ProductionRunReportPresenter>();
        builder.Services.AddSingleton<HistoryViewModel>();
        builder.Services.AddSingleton<ShellViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        return builder.Build();
    }

    private async void OnMainWindowClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (_shutdownCompleted)
        {
            return;
        }

        eventArgs.Cancel = true;
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        if (_mainWindow is not null)
        {
            _mainWindow.IsEnabled = false;
            _mainWindow.Title = "LayupPulse — arrêt en cours";
        }

        await ShutdownHostAsync();
        _shutdownCompleted = true;
        if (_mainWindow is not null)
        {
            _ = _mainWindow.Dispatcher.BeginInvoke(
                _mainWindow.Close,
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    private bool ActivateMainWindow()
    {
        MainWindow? window = _mainWindow;
        if (window is null)
        {
            return false;
        }

        _ = Dispatcher.BeginInvoke(window.ActivateExistingInstance);
        return true;
    }

    private async Task ShutdownHostAsync()
    {
        IHost? host = _host;
        if (host is null)
        {
            return;
        }

        _host = null;
        using CancellationTokenSource shutdownTimeout = new(TimeSpan.FromSeconds(8));
        ILogger<App>? logger = host.Services.GetService<ILogger<App>>();

        try
        {
            IMachineSessionService? sessionService =
                host.Services.GetService<IMachineSessionService>();
            if (sessionService is not null)
            {
                await sessionService.DisconnectAsync(shutdownTimeout.Token);
            }

            await host.StopAsync(shutdownTimeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (logger is not null)
            {
                ShutdownTimeoutLog(logger, null);
            }
        }
        catch (Exception exception)
        {
            if (logger is not null)
            {
                ShutdownFailureLog(logger, exception);
            }
        }
        finally
        {
            if (host is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                host.Dispose();
            }
        }
    }
}

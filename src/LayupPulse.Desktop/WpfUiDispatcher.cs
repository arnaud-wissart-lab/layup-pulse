using System.Windows.Threading;

namespace LayupPulse.Desktop;

public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    public WpfUiDispatcher()
    {
        _dispatcher = System.Windows.Application.Current.Dispatcher;
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.BeginInvoke(action, DispatcherPriority.Background);
    }
}

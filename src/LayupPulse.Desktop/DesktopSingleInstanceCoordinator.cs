using System.Diagnostics;

namespace LayupPulse.Desktop;

/// <summary>
/// Coordonne l’instance unique de l’application de bureau dans la session Windows active.
/// </summary>
public sealed class DesktopSingleInstanceCoordinator : IDisposable
{
    public const string MutexName = @"Local\LayupPulse.Desktop.SingleInstance.v1";
    public const string ActivationRequestEventName =
        @"Local\LayupPulse.Desktop.ActivationRequest.v1";
    public const string ActivationAcknowledgedEventName =
        @"Local\LayupPulse.Desktop.ActivationAcknowledged.v1";

    private readonly Mutex? _mutex;
    private readonly string _activationRequestName;
    private readonly string _activationAcknowledgedName;
    private readonly EventWaitHandle? _activationRequest;
    private readonly EventWaitHandle? _activationAcknowledged;
    private readonly EventWaitHandle? _stopListener;
    private Thread? _listenerThread;
    private bool _ownsMutex;
    private bool _disposed;

    public DesktopSingleInstanceCoordinator(string? instanceQualifier = null)
    {
        string suffix = string.IsNullOrWhiteSpace(instanceQualifier)
            ? string.Empty
            : $".{instanceQualifier}";
        string mutexName = MutexName + suffix;
        _activationRequestName = ActivationRequestEventName + suffix;
        _activationAcknowledgedName = ActivationAcknowledgedEventName + suffix;

        Mutex mutex = new(initiallyOwned: true, mutexName, out bool createdNew);
        bool acquired = createdNew;
        if (!createdNew)
        {
            try
            {
                acquired = mutex.WaitOne(TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }
        }

        if (!acquired)
        {
            mutex.Dispose();
            return;
        }

        _mutex = mutex;
        _ownsMutex = true;
        _activationRequest = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            _activationRequestName);
        _activationAcknowledged = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            _activationAcknowledgedName);
        _stopListener = new EventWaitHandle(initialState: false, EventResetMode.ManualReset);
    }

    public bool IsPrimaryInstance => _ownsMutex;

    public void StartListening(Func<bool> activationHandler)
    {
        ArgumentNullException.ThrowIfNull(activationHandler);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsPrimaryInstance)
        {
            throw new InvalidOperationException(
                "Seule l’instance principale peut écouter les demandes d’activation.");
        }

        if (_listenerThread is not null)
        {
            return;
        }

        _listenerThread = new Thread(() => RunActivationLoop(activationHandler))
        {
            IsBackground = true,
            Name = "LayupPulse activation interprocessus",
        };
        _listenerThread.Start();
    }

    public bool TryActivateExistingInstance(TimeSpan timeout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsPrimaryInstance)
        {
            return false;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                using EventWaitHandle request = EventWaitHandle.OpenExisting(
                    _activationRequestName);
                using EventWaitHandle acknowledged = EventWaitHandle.OpenExisting(
                    _activationAcknowledgedName);
                _ = acknowledged.WaitOne(TimeSpan.Zero);
                request.Set();

                TimeSpan remaining = timeout - stopwatch.Elapsed;
                return remaining > TimeSpan.Zero && acknowledged.WaitOne(remaining);
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(50);
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopListener?.Set();
        _listenerThread?.Join(TimeSpan.FromSeconds(2));
        _listenerThread = null;
        _stopListener?.Dispose();
        _activationAcknowledged?.Dispose();
        _activationRequest?.Dispose();

        if (_ownsMutex)
        {
            _mutex!.ReleaseMutex();
            _ownsMutex = false;
        }

        _mutex?.Dispose();
    }

    private void RunActivationLoop(Func<bool> activationHandler)
    {
        WaitHandle[] handles = [_activationRequest!, _stopListener!];
        while (WaitHandle.WaitAny(handles) == 0)
        {
            bool activated;
            try
            {
                activated = activationHandler();
            }
            catch (Exception)
            {
                activated = false;
            }

            if (activated)
            {
                _activationAcknowledged!.Set();
            }
        }
    }
}

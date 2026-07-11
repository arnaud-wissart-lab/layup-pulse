using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace LayupPulse.Simulator;

/// <summary>
/// Distribue les derniers échantillons dans des canaux bornés propres à chaque flux.
/// </summary>
public sealed class TelemetryStreamHub
{
    private const int SubscriberCapacity = 8;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Channel<SimulationSnapshot>> _subscribers = [];
    private bool _isConnected;
    private bool _communicationDropActive;

    public async IAsyncEnumerable<SimulationSnapshot> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Guid subscriptionId = Guid.NewGuid();
        Channel<SimulationSnapshot> channel;

        lock (_gate)
        {
            if (_communicationDropActive)
            {
                throw new TelemetryStreamInterruptedException(
                    TelemetryInterruptionReason.CommunicationDrop);
            }

            if (!_isConnected)
            {
                throw new TelemetryStreamInterruptedException(
                    TelemetryInterruptionReason.Disconnected);
            }

            channel = Channel.CreateBounded<SimulationSnapshot>(new BoundedChannelOptions(SubscriberCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest,
                AllowSynchronousContinuations = false,
            });
            _subscribers.Add(subscriptionId, channel);
        }

        try
        {
            await foreach (SimulationSnapshot snapshot in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return snapshot;
            }
        }
        finally
        {
            lock (_gate)
            {
                _subscribers.Remove(subscriptionId);
            }

            channel.Writer.TryComplete();
        }
    }

    public void Publish(SimulationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        ChannelWriter<SimulationSnapshot>[] writers;
        lock (_gate)
        {
            writers = _subscribers.Values
                .Select(static channel => channel.Writer)
                .ToArray();
        }

        foreach (ChannelWriter<SimulationSnapshot> writer in writers)
        {
            writer.TryWrite(snapshot);
        }
    }

    public void MarkConnected()
    {
        lock (_gate)
        {
            _isConnected = true;
            _communicationDropActive = false;
        }
    }

    public void MarkDisconnected()
    {
        CompleteSubscriptions(null, isConnected: false, communicationDropActive: false);
    }

    public void InterruptCommunication()
    {
        CompleteSubscriptions(
            new TelemetryStreamInterruptedException(TelemetryInterruptionReason.CommunicationDrop),
            isConnected: true,
            communicationDropActive: true);
    }

    public void RestoreCommunication()
    {
        lock (_gate)
        {
            _communicationDropActive = false;
        }
    }

    public void CompleteForShutdown()
    {
        CompleteSubscriptions(null, isConnected: false, communicationDropActive: false);
    }

    private void CompleteSubscriptions(
        Exception? exception,
        bool isConnected,
        bool communicationDropActive)
    {
        ChannelWriter<SimulationSnapshot>[] writers;

        lock (_gate)
        {
            _isConnected = isConnected;
            _communicationDropActive = communicationDropActive;
            writers = _subscribers.Values
                .Select(static channel => channel.Writer)
                .ToArray();
            _subscribers.Clear();
        }

        foreach (ChannelWriter<SimulationSnapshot> writer in writers)
        {
            writer.TryComplete(exception);
        }
    }
}

public enum TelemetryInterruptionReason
{
    Disconnected,
    CommunicationDrop,
}

/// <summary>
/// Distingue une fin de session normale d’une coupure de communication injectée.
/// </summary>
public sealed class TelemetryStreamInterruptedException : Exception
{
    public TelemetryStreamInterruptedException(TelemetryInterruptionReason reason)
        : base($"Le flux télémétrique a été interrompu : {reason}.")
    {
        Reason = reason;
    }

    public TelemetryInterruptionReason Reason { get; }
}

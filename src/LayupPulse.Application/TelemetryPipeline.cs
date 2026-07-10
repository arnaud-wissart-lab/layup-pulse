using LayupPulse.Domain;

namespace LayupPulse.Application;

/// <summary>
/// Traite chaque échantillon hors UI, applique une rétention bornée et ne publie que la dernière valeur à 10 Hz.
/// </summary>
public sealed class TelemetryPipeline
{
    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private readonly TelemetryPipelineOptions _options;
    private readonly AlarmEngine _alarmEngine;
    private readonly Queue<TelemetrySample> _history = [];
    private readonly Queue<TelemetryAggregate> _aggregates = [];
    private readonly Queue<DateTimeOffset> _sampleReceiptTimes = [];
    private readonly Queue<DateTimeOffset> _publicationTimes = [];
    private readonly AggregateAccumulator _accumulator = new();
    private TelemetrySample? _latestTelemetry;
    private TelemetryAggregate? _latestAggregate;
    private DateTimeOffset? _lastTelemetryReceivedAt;
    private DateTimeOffset? _lastPublicationAt;
    private DateTimeOffset? _aggregateWindowStartedAt;
    private long _receivedSamples;
    private long _droppedSamples;
    private long _coalescedSamples;
    private long _aggregateCount;
    private long _reconnectCount;

    public TelemetryPipeline(
        TimeProvider timeProvider,
        TelemetryPipelineOptions options,
        AlarmEngine alarmEngine)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(alarmEngine);
        options.Validate();
        _timeProvider = timeProvider;
        _options = options;
        _alarmEngine = alarmEngine;
    }

    public event EventHandler<TelemetryPipelinePublicationEventArgs>? PublicationReady;

    public void Accept(TelemetrySample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        TelemetryPipelinePublicationEventArgs? publication = null;
        DateTimeOffset receivedAt = _timeProvider.GetUtcNow();

        lock (_gate)
        {
            _receivedSamples++;
            _sampleReceiptTimes.Enqueue(receivedAt);
            TrimRateQueue(_sampleReceiptTimes, receivedAt);

            if (_latestTelemetry is not null && sample.SequenceNumber <= _latestTelemetry.SequenceNumber)
            {
                _coalescedSamples++;
                return;
            }

            if (_latestTelemetry is not null && sample.SequenceNumber > _latestTelemetry.SequenceNumber + 1)
            {
                _droppedSamples += sample.SequenceNumber - _latestTelemetry.SequenceNumber - 1;
            }

            _latestTelemetry = sample;
            _lastTelemetryReceivedAt = receivedAt;
            AddToHistory(sample);
            AddToAggregate(sample, receivedAt);
            _alarmEngine.EvaluateTelemetry(sample);

            if (_lastPublicationAt is null
                || receivedAt - _lastPublicationAt.Value >= _options.UiPublicationInterval)
            {
                publication = CreatePublication(receivedAt);
            }
            else
            {
                _coalescedSamples++;
            }
        }

        Publish(publication);
    }

    public void EvaluateCommunication(bool communicationExpected, DateTimeOffset? communicationStartedAt)
    {
        TelemetryPipelinePublicationEventArgs? publication = null;
        lock (_gate)
        {
            DateTimeOffset? lastFresh = _lastTelemetryReceivedAt ?? communicationStartedAt;
            if (_alarmEngine.EvaluateCommunication(communicationExpected, lastFresh))
            {
                publication = CreatePublication(_timeProvider.GetUtcNow());
            }
        }

        Publish(publication);
    }

    public bool AcknowledgeAlarm(Guid alarmId)
    {
        TelemetryPipelinePublicationEventArgs? publication = null;
        lock (_gate)
        {
            if (!_alarmEngine.Acknowledge(alarmId))
            {
                return false;
            }

            publication = CreatePublication(_timeProvider.GetUtcNow());
        }

        Publish(publication);
        return true;
    }

    public void RegisterReconnectAttempt()
    {
        lock (_gate)
        {
            _reconnectCount++;
        }
    }

    public TelemetryPipelinePublicationEventArgs GetCurrentPublication()
    {
        lock (_gate)
        {
            return BuildPublication(_timeProvider.GetUtcNow());
        }
    }

    public IReadOnlyList<TelemetrySample> GetHistorySnapshot()
    {
        lock (_gate)
        {
            return _history.ToArray();
        }
    }

    public IReadOnlyList<TelemetryAggregate> GetAggregateSnapshot()
    {
        lock (_gate)
        {
            return _aggregates.ToArray();
        }
    }

    public void Complete()
    {
        TelemetryPipelinePublicationEventArgs? publication = null;
        lock (_gate)
        {
            if (_accumulator.SampleCount > 0 && _aggregateWindowStartedAt is not null)
            {
                CompleteAggregate(_timeProvider.GetUtcNow());
                publication = CreatePublication(_timeProvider.GetUtcNow());
            }
        }

        Publish(publication);
    }

    private void AddToHistory(TelemetrySample sample)
    {
        _history.Enqueue(sample);
        while (_history.Count > _options.HistoryCapacity)
        {
            _history.Dequeue();
        }

        while (_history.Count > 0
            && sample.Timestamp - _history.Peek().Timestamp > _options.HistoryDuration)
        {
            _history.Dequeue();
        }
    }

    private void AddToAggregate(TelemetrySample sample, DateTimeOffset receivedAt)
    {
        _aggregateWindowStartedAt ??= receivedAt;
        if (_accumulator.SampleCount > 0
            && receivedAt - _aggregateWindowStartedAt.Value >= _options.AggregateInterval)
        {
            CompleteAggregate(receivedAt);
            _aggregateWindowStartedAt = receivedAt;
        }

        _accumulator.Add(sample);
    }

    private void CompleteAggregate(DateTimeOffset endedAt)
    {
        if (_aggregateWindowStartedAt is null || _accumulator.SampleCount == 0)
        {
            return;
        }

        _latestAggregate = _accumulator.Create(_aggregateWindowStartedAt.Value, endedAt);
        _aggregates.Enqueue(_latestAggregate);
        while (_aggregates.Count > _options.AggregateCapacity)
        {
            _aggregates.Dequeue();
        }

        _aggregateCount++;
        _accumulator.Reset();
    }

    private TelemetryPipelinePublicationEventArgs CreatePublication(DateTimeOffset publishedAt)
    {
        _lastPublicationAt = publishedAt;
        _publicationTimes.Enqueue(publishedAt);
        TrimRateQueue(_publicationTimes, publishedAt);
        return BuildPublication(publishedAt);
    }

    private TelemetryPipelinePublicationEventArgs BuildPublication(DateTimeOffset now)
    {
        TrimRateQueue(_sampleReceiptTimes, now);
        TrimRateQueue(_publicationTimes, now);
        TelemetryPipelineMetrics metrics = new(
            _receivedSamples,
            _droppedSamples,
            _coalescedSamples,
            _latestTelemetry?.SequenceNumber ?? 0,
            CalculateRate(_sampleReceiptTimes),
            CalculateRate(_publicationTimes),
            _aggregateCount,
            _reconnectCount,
            _options.HistoryCapacity,
            _history.Count);
        return new TelemetryPipelinePublicationEventArgs(
            _latestTelemetry,
            _lastTelemetryReceivedAt,
            _latestAggregate,
            metrics,
            _alarmEngine.ActiveAlarms,
            _alarmEngine.History);
    }

    private void TrimRateQueue(Queue<DateTimeOffset> queue, DateTimeOffset now)
    {
        while (queue.Count > 0 && now - queue.Peek() > _options.RateWindow)
        {
            queue.Dequeue();
        }

        int maximumCount = Math.Max(_options.HistoryCapacity, 100);
        while (queue.Count > maximumCount)
        {
            queue.Dequeue();
        }
    }

    private static double CalculateRate(Queue<DateTimeOffset> timestamps)
    {
        if (timestamps.Count < 2)
        {
            return 0;
        }

        TimeSpan duration = timestamps.Last() - timestamps.Peek();
        return duration <= TimeSpan.Zero
            ? 0
            : (timestamps.Count - 1) / duration.TotalSeconds;
    }

    private void Publish(TelemetryPipelinePublicationEventArgs? publication)
    {
        if (publication is not null)
        {
            PublicationReady?.Invoke(this, publication);
        }
    }

    private sealed class AggregateAccumulator
    {
        private double _feedRateSum;
        private double _forceSum;
        private double _temperatureSum;
        private double _pressureSum;
        private double _minimumForce = double.PositiveInfinity;
        private double _maximumForce = double.NegativeInfinity;
        private long _firstSequenceNumber;
        private long _lastSequenceNumber;

        public int SampleCount { get; private set; }

        public void Add(TelemetrySample sample)
        {
            if (SampleCount == 0)
            {
                _firstSequenceNumber = sample.SequenceNumber;
            }

            SampleCount++;
            _lastSequenceNumber = sample.SequenceNumber;
            _feedRateSum += sample.ActualFeedRateMillimetersPerSecond;
            _forceSum += sample.CompactionForceNewtons;
            _temperatureSum += sample.HeaterTemperatureCelsius;
            _pressureSum += sample.MaterialPressureBar;
            _minimumForce = Math.Min(_minimumForce, sample.CompactionForceNewtons);
            _maximumForce = Math.Max(_maximumForce, sample.CompactionForceNewtons);
        }

        public TelemetryAggregate Create(DateTimeOffset startedAt, DateTimeOffset endedAt) => new(
            startedAt,
            endedAt,
            SampleCount,
            _firstSequenceNumber,
            _lastSequenceNumber,
            _feedRateSum / SampleCount,
            _forceSum / SampleCount,
            _temperatureSum / SampleCount,
            _pressureSum / SampleCount,
            _minimumForce,
            _maximumForce);

        public void Reset()
        {
            SampleCount = 0;
            _feedRateSum = 0;
            _forceSum = 0;
            _temperatureSum = 0;
            _pressureSum = 0;
            _minimumForce = double.PositiveInfinity;
            _maximumForce = double.NegativeInfinity;
            _firstSequenceNumber = 0;
            _lastSequenceNumber = 0;
        }
    }
}

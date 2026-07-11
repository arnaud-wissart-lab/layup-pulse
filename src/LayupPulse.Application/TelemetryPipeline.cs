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
    private DateTimeOffset? _aggregateBucketStartedAt;
    private Guid _activeProductionRunId;
    private long? _lastAcceptedSequenceNumber;
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

    public event EventHandler<TelemetryAggregateCompletedEventArgs>? AggregateCompleted;

    public event EventHandler<TelemetryPipelinePublicationEventArgs>? AlarmStateChanged;

    public void Accept(TelemetrySample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        TelemetryPipelinePublicationEventArgs? publication = null;
        TelemetryPipelinePublicationEventArgs? alarmPublication = null;
        TelemetryAggregate? completedAggregate = null;
        DateTimeOffset receivedAt = _timeProvider.GetUtcNow();

        lock (_gate)
        {
            _receivedSamples++;
            _sampleReceiptTimes.Enqueue(receivedAt);
            TrimRateQueue(_sampleReceiptTimes, receivedAt);

            if (_lastAcceptedSequenceNumber is not null
                && sample.SequenceNumber <= _lastAcceptedSequenceNumber.Value)
            {
                _coalescedSamples++;
                return;
            }

            if (_lastAcceptedSequenceNumber is not null
                && sample.SequenceNumber > _lastAcceptedSequenceNumber.Value + 1)
            {
                _droppedSamples += sample.SequenceNumber - _lastAcceptedSequenceNumber.Value - 1;
            }

            _lastAcceptedSequenceNumber = sample.SequenceNumber;
            _latestTelemetry = sample;
            _lastTelemetryReceivedAt = receivedAt;
            AddToHistory(sample);
            completedAggregate = AddToAggregate(sample);
            bool alarmStateChanged = _alarmEngine.EvaluateTelemetry(sample);

            if (alarmStateChanged
                || _lastPublicationAt is null
                || receivedAt - _lastPublicationAt.Value >= _options.UiPublicationInterval)
            {
                publication = CreatePublication(receivedAt);
                alarmPublication = alarmStateChanged ? publication : null;
            }
            else
            {
                _coalescedSamples++;
            }
        }

        PublishAggregate(completedAggregate);
        Publish(publication);
        PublishAlarmState(alarmPublication);
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
        PublishAlarmState(publication);
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
        PublishAlarmState(publication);
        return true;
    }

    public void RegisterReconnectAttempt()
    {
        lock (_gate)
        {
            _reconnectCount++;
        }
    }

    /// <summary>
    /// Démarre la portée de séquence d’une nouvelle session de transport sans perdre l’historique borné.
    /// </summary>
    public void BeginSequenceScope()
    {
        lock (_gate)
        {
            _lastAcceptedSequenceNumber = null;
        }
    }

    /// <summary>
    /// Ferme le bucket hors cycle éventuel puis associe les prochains agrégats et alarmes au cycle indiqué.
    /// </summary>
    public void BeginProductionRun(Guid productionRunId)
    {
        if (productionRunId == Guid.Empty)
        {
            throw new ArgumentException("L’identifiant d’exécution ne peut pas être vide.", nameof(productionRunId));
        }

        TelemetryAggregate? completedAggregate;
        lock (_gate)
        {
            completedAggregate = CompleteAggregate();
            _aggregateBucketStartedAt = null;
            _activeProductionRunId = productionRunId;
            _alarmEngine.AssociateProductionRun(productionRunId);
        }

        PublishAggregate(completedAggregate);
    }

    /// <summary>
    /// Termine le dernier bucket du cycle et retire l’association pour la télémétrie suivante.
    /// </summary>
    public void EndProductionRun(bool retainAlarmAssociation = false)
    {
        TelemetryAggregate? completedAggregate;
        lock (_gate)
        {
            completedAggregate = CompleteAggregate();
            _aggregateBucketStartedAt = null;
            Guid completedProductionRunId = _activeProductionRunId;
            _activeProductionRunId = Guid.Empty;
            _alarmEngine.AssociateProductionRun(
                retainAlarmAssociation ? completedProductionRunId : null);
        }

        PublishAggregate(completedAggregate);
    }

    /// <summary>
    /// Retire l’association d’alarme conservée après un cycle en défaut.
    /// </summary>
    public void ClearProductionRunAssociation()
    {
        lock (_gate)
        {
            _alarmEngine.AssociateProductionRun(null);
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
        TelemetryAggregate? completedAggregate = null;
        lock (_gate)
        {
            if (_accumulator.SampleCount > 0 && _aggregateBucketStartedAt is not null)
            {
                completedAggregate = CompleteAggregate();
                publication = CreatePublication(_timeProvider.GetUtcNow());
            }
        }

        PublishAggregate(completedAggregate);
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

    private TelemetryAggregate? AddToAggregate(TelemetrySample sample)
    {
        DateTimeOffset bucketStartedAt = GetUtcBucketStart(sample.Timestamp);
        TelemetryAggregate? completedAggregate = null;
        if (_aggregateBucketStartedAt is not null
            && bucketStartedAt != _aggregateBucketStartedAt.Value)
        {
            completedAggregate = CompleteAggregate();
        }

        _aggregateBucketStartedAt = bucketStartedAt;
        _accumulator.Add(sample);
        return completedAggregate;
    }

    private TelemetryAggregate? CompleteAggregate()
    {
        if (_aggregateBucketStartedAt is null || _accumulator.SampleCount == 0)
        {
            return null;
        }

        _latestAggregate = _accumulator.Create(
            _activeProductionRunId,
            _aggregateBucketStartedAt.Value,
            _aggregateBucketStartedAt.Value + _options.AggregateInterval);
        _aggregates.Enqueue(_latestAggregate);
        while (_aggregates.Count > _options.AggregateCapacity)
        {
            _aggregates.Dequeue();
        }

        _aggregateCount++;
        _accumulator.Reset();
        return _latestAggregate;
    }

    private DateTimeOffset GetUtcBucketStart(DateTimeOffset timestamp)
    {
        long intervalTicks = _options.AggregateInterval.Ticks;
        long utcTicks = timestamp.UtcTicks;
        return new DateTimeOffset(utcTicks - (utcTicks % intervalTicks), TimeSpan.Zero);
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

    private void PublishAggregate(TelemetryAggregate? aggregate)
    {
        if (aggregate is not null)
        {
            AggregateCompleted?.Invoke(this, new TelemetryAggregateCompletedEventArgs(aggregate));
        }
    }

    private void PublishAlarmState(TelemetryPipelinePublicationEventArgs? publication)
    {
        if (publication is not null)
        {
            AlarmStateChanged?.Invoke(this, publication);
        }
    }

    private sealed class AggregateAccumulator
    {
        private double _feedRateSum;
        private double _forceSum;
        private double _temperatureSum;
        private double _pressureSum;
        private double _processHealthSum;
        private double _minimumTemperature = double.PositiveInfinity;
        private double _maximumTemperature = double.NegativeInfinity;
        private double _minimumPressure = double.PositiveInfinity;
        private double _maximumPressure = double.NegativeInfinity;
        private double _minimumForce = double.PositiveInfinity;
        private double _maximumForce = double.NegativeInfinity;
        private double _minimumProcessHealth = double.PositiveInfinity;
        private double _endOfBucketCycleProgress;
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
            _processHealthSum += sample.ProcessHealthPercentage;
            _minimumTemperature = Math.Min(_minimumTemperature, sample.HeaterTemperatureCelsius);
            _maximumTemperature = Math.Max(_maximumTemperature, sample.HeaterTemperatureCelsius);
            _minimumPressure = Math.Min(_minimumPressure, sample.MaterialPressureBar);
            _maximumPressure = Math.Max(_maximumPressure, sample.MaterialPressureBar);
            _minimumForce = Math.Min(_minimumForce, sample.CompactionForceNewtons);
            _maximumForce = Math.Max(_maximumForce, sample.CompactionForceNewtons);
            _minimumProcessHealth = Math.Min(_minimumProcessHealth, sample.ProcessHealthPercentage);
            _endOfBucketCycleProgress = sample.CycleProgressPercentage;
        }

        public TelemetryAggregate Create(
            Guid productionRunId,
            DateTimeOffset startedAt,
            DateTimeOffset endedAt) => new(
            Guid.NewGuid(),
            productionRunId,
            startedAt,
            endedAt,
            SampleCount,
            _firstSequenceNumber,
            _lastSequenceNumber,
            _feedRateSum / SampleCount,
            _forceSum / SampleCount,
            _temperatureSum / SampleCount,
            _minimumTemperature,
            _maximumTemperature,
            _pressureSum / SampleCount,
            _minimumPressure,
            _maximumPressure,
            _minimumForce,
            _maximumForce,
            _processHealthSum / SampleCount,
            _minimumProcessHealth,
            _endOfBucketCycleProgress);

        public void Reset()
        {
            SampleCount = 0;
            _feedRateSum = 0;
            _forceSum = 0;
            _temperatureSum = 0;
            _pressureSum = 0;
            _processHealthSum = 0;
            _minimumTemperature = double.PositiveInfinity;
            _maximumTemperature = double.NegativeInfinity;
            _minimumPressure = double.PositiveInfinity;
            _maximumPressure = double.NegativeInfinity;
            _minimumForce = double.PositiveInfinity;
            _maximumForce = double.NegativeInfinity;
            _minimumProcessHealth = double.PositiveInfinity;
            _endOfBucketCycleProgress = 0;
            _firstSequenceNumber = 0;
            _lastSequenceNumber = 0;
        }
    }
}

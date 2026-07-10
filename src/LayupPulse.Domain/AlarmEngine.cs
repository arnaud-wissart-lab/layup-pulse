namespace LayupPulse.Domain;

/// <summary>
/// Évalue les conditions physiques simulées et conserve un cycle de vie borné par code et source.
/// </summary>
public sealed class AlarmEngine
{
    public const string TemperatureSource = "Chauffage matière";
    public const string PressureSource = "Alimentation matière";
    public const string ForceSource = "Rouleau de compactage";
    public const string CommunicationSource = "Liaison simulateur";
    public const string HeadPositionSource = "Position tête de dépose";

    private readonly TimeProvider _timeProvider;
    private readonly AlarmEngineOptions _options;
    private readonly Dictionary<(AlarmCode Code, string Source), AlarmEvent> _active = [];
    private readonly Queue<AlarmEvent> _history = [];
    private readonly Queue<(DateTimeOffset Timestamp, double Value)> _forceWindow = [];
    private DateTimeOffset? _temperatureExceededAt;
    private DateTimeOffset? _pressureDroppedAt;
    private bool _pressureWasArmedWhileRunning;
    private bool _forceWasArmedWhileRunning;
    private MachineState _previousMachineState = MachineState.Disconnected;
    private int _communicationRecoverySamples;

    public AlarmEngine(TimeProvider timeProvider, AlarmEngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _timeProvider = timeProvider;
        _options = options;
    }

    public IReadOnlyList<AlarmEvent> ActiveAlarms => _active.Values
        .OrderByDescending(static alarm => alarm.Severity)
        .ThenBy(static alarm => alarm.RaisedAt)
        .ToArray();

    public IReadOnlyList<AlarmEvent> History => _history
        .Reverse()
        .ToArray();

    public bool EvaluateTelemetry(TelemetrySample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        bool changed = EvaluateTemperature(sample.HeaterTemperatureCelsius, now);
        changed |= EvaluatePressure(sample, now);
        changed |= EvaluateForce(sample, now);
        changed |= SetCondition(
            AlarmCode.HeadPositionError,
            HeadPositionSource,
            sample.HasActiveFault(FaultType.HeadPositionError),
            AlarmSeverity.Critical,
            "Écart de position de la tête de dépose détecté par le simulateur.",
            now);
        changed |= RegisterFreshCommunication(now);
        _previousMachineState = sample.MachineState;
        return changed;
    }

    public bool EvaluateCommunication(bool communicationExpected, DateTimeOffset? lastFreshTelemetry)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        bool timedOut = communicationExpected
            && lastFreshTelemetry is not null
            && now - lastFreshTelemetry.Value >= _options.CommunicationTimeout;

        if (!timedOut)
        {
            return communicationExpected
                ? false
                : ClearAlarm(AlarmCode.CommunicationTimeout, CommunicationSource, now);
        }

        _communicationRecoverySamples = 0;
        return RaiseAlarm(
            AlarmCode.CommunicationTimeout,
            CommunicationSource,
            AlarmSeverity.Critical,
            $"Aucune télémétrie fraîche reçue depuis {_options.CommunicationTimeout.TotalSeconds:F1} s.",
            now);
    }

    public bool Acknowledge(Guid alarmId)
    {
        foreach (((AlarmCode Code, string Source) key, AlarmEvent alarm) in _active)
        {
            if (alarm.Id != alarmId)
            {
                continue;
            }

            AlarmEvent acknowledged = alarm.Acknowledge(_timeProvider.GetUtcNow());
            if (ReferenceEquals(acknowledged, alarm))
            {
                return false;
            }

            _active[key] = acknowledged;
            return true;
        }

        return false;
    }

    private bool EvaluateTemperature(double temperature, DateTimeOffset now)
    {
        if (temperature > _options.HighTemperatureThresholdCelsius)
        {
            _temperatureExceededAt ??= now;
            if (now - _temperatureExceededAt.Value >= _options.HighTemperatureDebounce)
            {
                return RaiseAlarm(
                    AlarmCode.HighTemperature,
                    TemperatureSource,
                    AlarmSeverity.Critical,
                    $"Température supérieure à {_options.HighTemperatureThresholdCelsius:F0} °C pendant " +
                    $"{_options.HighTemperatureDebounce.TotalSeconds:F1} s.",
                    now);
            }

            return false;
        }

        if (temperature < _options.HighTemperatureClearThresholdCelsius)
        {
            _temperatureExceededAt = null;
            return ClearAlarm(AlarmCode.HighTemperature, TemperatureSource, now);
        }

        if (!IsActive(AlarmCode.HighTemperature, TemperatureSource))
        {
            _temperatureExceededAt = null;
        }

        return false;
    }

    private bool EvaluatePressure(TelemetrySample sample, DateTimeOffset now)
    {
        bool injectedFaultContinuesRunningCondition = sample.MachineState == MachineState.Faulted
            && sample.HasActiveFault(FaultType.LowMaterialPressure)
            && (_previousMachineState == MachineState.Running || _pressureWasArmedWhileRunning);
        bool eligible = sample.MachineState == MachineState.Running || injectedFaultContinuesRunningCondition;

        if (sample.MachineState == MachineState.Running)
        {
            _pressureWasArmedWhileRunning = true;
        }
        else if (!injectedFaultContinuesRunningCondition)
        {
            _pressureWasArmedWhileRunning = false;
        }

        if (eligible && sample.MaterialPressureBar < _options.LowPressureThresholdBar)
        {
            _pressureDroppedAt ??= now;
            if (now - _pressureDroppedAt.Value >= _options.LowPressureDebounce)
            {
                return RaiseAlarm(
                    AlarmCode.LowMaterialPressure,
                    PressureSource,
                    AlarmSeverity.Critical,
                    $"Pression matière inférieure à {_options.LowPressureThresholdBar:F1} bar pendant le cycle.",
                    now);
            }

            return false;
        }

        _pressureDroppedAt = null;
        if (sample.MaterialPressureBar > _options.LowPressureClearThresholdBar
            || (!eligible && !sample.HasActiveFault(FaultType.LowMaterialPressure)))
        {
            return ClearAlarm(AlarmCode.LowMaterialPressure, PressureSource, now);
        }

        return false;
    }

    private bool EvaluateForce(TelemetrySample sample, DateTimeOffset now)
    {
        bool injectedFaultContinuesRunningCondition = sample.MachineState == MachineState.Faulted
            && sample.HasActiveFault(FaultType.UnstableCompactionForce)
            && (_previousMachineState == MachineState.Running || _forceWasArmedWhileRunning);
        bool eligible = sample.MachineState == MachineState.Running || injectedFaultContinuesRunningCondition;

        if (sample.MachineState == MachineState.Running)
        {
            _forceWasArmedWhileRunning = true;
        }
        else if (!injectedFaultContinuesRunningCondition)
        {
            _forceWasArmedWhileRunning = false;
        }

        if (!eligible)
        {
            _forceWindow.Clear();
            return !sample.HasActiveFault(FaultType.UnstableCompactionForce)
                ? ClearAlarm(AlarmCode.UnstableCompactionForce, ForceSource, now)
                : false;
        }

        _forceWindow.Enqueue((now, sample.CompactionForceNewtons));
        while (_forceWindow.Count > 0
            && now - _forceWindow.Peek().Timestamp > _options.ForceVariationWindow)
        {
            _forceWindow.Dequeue();
        }

        while (_forceWindow.Count > 200)
        {
            _forceWindow.Dequeue();
        }

        if (_forceWindow.Count < _options.ForceMinimumSampleCount)
        {
            return false;
        }

        double minimum = _forceWindow.Min(static point => point.Value);
        double maximum = _forceWindow.Max(static point => point.Value);
        double variation = maximum - minimum;

        if (variation > _options.ForceVariationThresholdNewtons)
        {
            return RaiseAlarm(
                AlarmCode.UnstableCompactionForce,
                ForceSource,
                AlarmSeverity.Warning,
                $"Variation de force de compactage de {variation:F0} N sur " +
                $"{_options.ForceVariationWindow.TotalSeconds:F1} s.",
                now);
        }

        return variation < _options.ForceVariationClearThresholdNewtons
            ? ClearAlarm(AlarmCode.UnstableCompactionForce, ForceSource, now)
            : false;
    }

    private bool RegisterFreshCommunication(DateTimeOffset now)
    {
        if (!IsActive(AlarmCode.CommunicationTimeout, CommunicationSource))
        {
            _communicationRecoverySamples = 0;
            return false;
        }

        _communicationRecoverySamples++;
        if (_communicationRecoverySamples < _options.CommunicationRecoverySampleCount)
        {
            return false;
        }

        _communicationRecoverySamples = 0;
        return ClearAlarm(AlarmCode.CommunicationTimeout, CommunicationSource, now);
    }

    private bool SetCondition(
        AlarmCode code,
        string source,
        bool condition,
        AlarmSeverity severity,
        string message,
        DateTimeOffset now) => condition
            ? RaiseAlarm(code, source, severity, message, now)
            : ClearAlarm(code, source, now);

    private bool RaiseAlarm(
        AlarmCode code,
        string source,
        AlarmSeverity severity,
        string message,
        DateTimeOffset now)
    {
        (AlarmCode Code, string Source) key = (code, source);
        if (_active.ContainsKey(key))
        {
            return false;
        }

        _active.Add(key, new AlarmEvent(Guid.NewGuid(), code, severity, source, message, now));
        return true;
    }

    private bool ClearAlarm(AlarmCode code, string source, DateTimeOffset now)
    {
        (AlarmCode Code, string Source) key = (code, source);
        if (!_active.Remove(key, out AlarmEvent? alarm))
        {
            return false;
        }

        _history.Enqueue(alarm.Clear(now));
        while (_history.Count > _options.HistoryCapacity)
        {
            _history.Dequeue();
        }

        return true;
    }

    private bool IsActive(AlarmCode code, string source) => _active.ContainsKey((code, source));
}

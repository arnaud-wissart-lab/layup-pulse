using System.Collections.Immutable;
using LayupPulse.Application;
using LayupPulse.Domain;

namespace LayupPulse.Desktop.Reporting;

/// <summary>
/// Représente le contenu immuable d’un rapport de cycle sans dépendre de WPF.
/// </summary>
public sealed record ProductionRunReport(
    string Title,
    string Warning,
    Guid ProductionRunId,
    string RecipeName,
    string PartReference,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    TimeSpan? Duration,
    ProductionRunStatus FinalStatus,
    double CompletionPercentage,
    int AlarmCount,
    double AverageTemperatureCelsius,
    double AveragePressureBar,
    double AverageCompactionForceNewtons,
    double AverageFeedRateMillimetersPerSecond,
    double MinimumProcessHealthPercentage,
    ProductionRunReportTelemetrySummary? TelemetrySummary,
    ImmutableArray<AlarmHistoryItem> DetailedAlarms,
    int OmittedAlarmCount,
    DateTimeOffset GeneratedAt,
    string ApplicationVersion);

/// <summary>
/// Résume les agrégats télémétriques sans conserver leurs buckets individuels.
/// </summary>
public sealed record ProductionRunReportTelemetrySummary(
    DateTimeOffset PeriodStartedAt,
    DateTimeOffset PeriodEndedAt,
    int BucketCount,
    long TotalSampleCount,
    ProductionRunReportRange TemperatureCelsiusRange,
    ProductionRunReportRange PressureBarRange,
    ProductionRunReportRange CompactionForceNewtonsRange,
    ProductionRunReportRange AverageFeedRateMillimetersPerSecondRange);

/// <summary>
/// Décrit une plage numérique minimale et maximale.
/// </summary>
public sealed record ProductionRunReportRange(double Minimum, double Maximum);

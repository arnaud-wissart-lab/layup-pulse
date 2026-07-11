using LayupPulse.Domain;

namespace LayupPulse.Desktop;

public sealed record HistoryStatusFilter(string Label, ProductionRunStatus? Status);

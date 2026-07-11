using Microsoft.EntityFrameworkCore;

namespace LayupPulse.Infrastructure.Persistence;

/// <summary>
/// Contexte court dédié à l’historique local agrégé.
/// </summary>
public sealed class HistoryDbContext(DbContextOptions<HistoryDbContext> options) : DbContext(options)
{
    public DbSet<ProductionRunRecord> ProductionRuns => Set<ProductionRunRecord>();

    public DbSet<TelemetryAggregateRecord> TelemetryAggregates => Set<TelemetryAggregateRecord>();

    public DbSet<AlarmRecord> Alarms => Set<AlarmRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<ProductionRunRecord>(entity =>
        {
            entity.ToTable("ProductionRuns");
            entity.HasKey(static run => run.Id);
            entity.Property(static run => run.RecipeName).HasMaxLength(200).IsRequired();
            entity.Property(static run => run.PartReference).HasMaxLength(100).IsRequired();
            entity.HasIndex(static run => run.StartedAtUtc);
            entity.HasIndex(static run => run.FinalStatus);
        });

        modelBuilder.Entity<TelemetryAggregateRecord>(entity =>
        {
            entity.ToTable("TelemetryAggregates");
            entity.HasKey(static aggregate => new
            {
                aggregate.ProductionRunId,
                aggregate.BucketStartedAtUtc,
            });
            entity.HasOne(static aggregate => aggregate.ProductionRun)
                .WithMany(static run => run.TelemetryAggregates)
                .HasForeignKey(static aggregate => aggregate.ProductionRunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AlarmRecord>(entity =>
        {
            entity.ToTable("Alarms");
            entity.HasKey(static alarm => alarm.Id);
            entity.Property(static alarm => alarm.Source).HasMaxLength(200).IsRequired();
            entity.Property(static alarm => alarm.Message).HasMaxLength(1_000).IsRequired();
            entity.HasIndex(static alarm => alarm.RaisedAtUtc);
            entity.HasIndex(static alarm => alarm.ProductionRunId);
            entity.HasOne(static alarm => alarm.ProductionRun)
                .WithMany(static run => run.Alarms)
                .HasForeignKey(static alarm => alarm.ProductionRunId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LayupPulse.Infrastructure.Persistence;

/// <summary>
/// Fournit au générateur de migrations une configuration locale sans démarrer WPF.
/// </summary>
public sealed class HistoryDbContextDesignFactory : IDesignTimeDbContextFactory<HistoryDbContext>
{
    public HistoryDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<HistoryDbContext> options = new();
        options.UseSqlite($"Data Source={HistoryDatabasePath.GetDefault()}");
        return new HistoryDbContext(options.Options);
    }
}

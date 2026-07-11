using LayupPulse.Application;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LayupPulse.Infrastructure.Persistence;

/// <summary>
/// Compose l’adaptateur SQLite sans faire dépendre la racine WPF des types EF Core.
/// </summary>
public static class HistoryServiceCollectionExtensions
{
    public static IServiceCollection AddLayupPulseHistory(
        this IServiceCollection services,
        string? databasePath = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        string resolvedPath = string.IsNullOrWhiteSpace(databasePath)
            ? HistoryDatabasePath.GetDefault()
            : Path.GetFullPath(databasePath);
        SqliteConnectionStringBuilder connectionString = new()
        {
            DataSource = resolvedPath,
            ForeignKeys = true,
            Pooling = true,
        };

        services.AddSingleton(new HistoryStorageOptions(resolvedPath));
        services.AddPooledDbContextFactory<HistoryDbContext>(options =>
            options.UseSqlite(connectionString.ConnectionString));
        services.AddSingleton<SqliteHistoryStore>();
        services.AddSingleton<IHistoryWriter>(static services =>
            services.GetRequiredService<SqliteHistoryStore>());
        services.AddSingleton<IHistoryQueryService, SqliteHistoryQueryService>();
        services.AddHostedService(static services =>
            services.GetRequiredService<SqliteHistoryStore>());
        return services;
    }
}

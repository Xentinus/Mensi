using Mensi.Core.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Mensi.Tests;

/// <summary>
/// Egy megosztott, eldobható Postgres a teljes tesztfuttatásra; a migrációk egyszer futnak le.
/// A tesztek külön-külön takarítanak (TRUNCATE), így egymástól függetlenek maradnak.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public MensiDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<MensiDbContext>()
            .UseNpgsql(ConnectionString)
            .Options);

    public async Task ResetAsync()
    {
        await using var db = CreateContext();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE daily_log, intercourse_event, cycle, audit_log RESTART IDENTITY CASCADE");
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition("postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>;

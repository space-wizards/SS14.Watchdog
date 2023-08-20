using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SS14.Watchdog.Components.DataManagement;

/// <summary>
/// Manages the watchdog's SQLite database, used to store all persisted data of the watchdog itself.
/// </summary>
public sealed class DataManager : IHostedService
{
    private readonly ILogger<DataManager> _logger;
    private readonly IOptions<DataOptions> _options;

    public DataManager(ILogger<DataManager> logger, IOptions<DataOptions> options)
    {
        _logger = logger;
        _options = options;
    }

    public SqliteConnection OpenConnection()
    {
        var con = new SqliteConnection(GetConnectionString());
        con.Open();
        return con;
    }

    private string GetConnectionString()
    {
        return new SqliteConnectionStringBuilder
        {
            ForeignKeys = true,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DataSource = _options.Value.File
        }.ToString();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var con = OpenConnection();

        VerifyDBIntegrity(con);

        Migrator.Migrate(con, "SS14.Watchdog.Components.DataManagement.Migrations", _logger);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // No shutdown needed.
        return Task.CompletedTask;
    }

    private void VerifyDBIntegrity(SqliteConnection con)
    {
        _logger.LogInformation("Running database integrity checks...");

        _logger.LogDebug("PRAGMA integrity_check");

        var resultsIntegrity = con.Query<string>("PRAGMA integrity_check").ToArray();
        if (resultsIntegrity is not ["ok"])
        {
            // Oh fuck.
            _logger.LogCritical("Integrity errors found in database file ({File})", _options.Value.File);
            foreach (var result in resultsIntegrity)
            {
                _logger.LogError("Integrity error: {Error}", result);
            }

            throw new InvalidOperationException("Integrity errors have been found in database file. Refusing to start.");
        }

        _logger.LogDebug("PRAGMA foreign_key_check");

        var resultsFK = con.Query<(string table, long? rowId, string targetTable, long fkIndex)>("PRAGMA foreign_key_check").ToArray();
        if (resultsFK.Length != 0)
        {
            _logger.LogCritical("Foreign key errors have been found in the database file ({File})", _options.Value.File);
            foreach (var (table, rowId, targetTable, fkIndex) in resultsFK)
            {
                _logger.LogError(
                    "Foreign key error: {SourceTable}, {RowId}, {TargetTable}, {FKIndex}",
                    table, rowId, targetTable, fkIndex);
            }

            throw new InvalidOperationException("Integrity errors have been found in database file. Refusing to start.");
        }

        _logger.LogInformation("Database integrity check passed");
    }
}
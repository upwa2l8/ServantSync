using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace ServantSync.Services;

/// <summary>
/// Round-ACA-1.8: Microsoft.Data.Sqlite does NOT accept <c>busy_timeout</c>
/// as a connection-string keyword — its <see cref="SqliteConnectionStringBuilder"/>
/// throws <see cref="System.ArgumentException"/> on unrecognised keywords
/// (observed on revision servantsync--0000006's EF Core <c>MigrateAsync</c>
/// as "Connection string keyword 'busy_timeout' is not supported", which
/// crashes the app at startup before any connection is opened). The earlier
/// YAML-suffix attempt at the same goal failed on the parser layer.
/// Workaround: apply <c>PRAGMA busy_timeout=30000</c> via the EF Core
/// <see cref="DbConnectionInterceptor.ConnectionOpened"/> callback, so the
/// PRAGMA is set on every opened connection AFTER the parser has accepted
/// the connection string. Settles the Azure Files SMB cold-start
/// contention previously observed as <c>SQLite Error 5: 'database is
/// locked'</c> on revision servantsync--0000004's <c>MigrateAsync</c>.
/// </summary>
public class SqliteBusyTimeoutInterceptor : DbConnectionInterceptor
{
    /// <summary>
    /// 30 seconds. Absorbs transient Azure Files SMB oplock boundaries
    /// during cold start (the original symptom: SQLITE_BUSY against
    /// /data/servantsync.db on the MigrateAsync transaction).
    /// </summary>
    private const int BusyTimeoutMs = 30_000;

    /// <summary>
    /// Override the synchronous ConnectionOpened path. EF Core's
    /// <c>DbConnectionInterceptor.ConnectionOpenedAsync</c> default
    /// implementation falls through to this synchronous method, so async
    /// paths (MigrateAsync, ToListAsync, SaveChangesAsync, the
    /// SqliteBackupService VACUUM INTO) get the same treatment without a
    /// separate override. Pre-PRAGMA-ApplicationDbContext relational
    /// operations don't fire <c>ConnectionOpened</c> — but the
    /// EF Core SQLite provider's <c>SqliteDatabaseCreator.Exists()</c>
    /// path DOES open a connection (and that was where the original
    /// ArgumentException landed); once the parser accepts the connection
    /// string the interceptor's <see cref="ConnectionOpened"/> callback
    /// fires and the PRAGMA is set for the lifetime of that connection.
    /// </summary>
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (connection is SqliteConnection sqlite)
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = $"PRAGMA busy_timeout={BusyTimeoutMs}";
            cmd.ExecuteNonQuery();
        }
        base.ConnectionOpened(connection, eventData);
    }
}

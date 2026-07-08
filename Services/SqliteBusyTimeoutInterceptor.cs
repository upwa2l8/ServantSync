using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace ServantSync.Services;

/// <summary>
/// Round-ACA-1.8 + Round-ACA-1.17: Microsoft.Data.Sqlite does NOT accept
/// <c>busy_timeout</c> as a connection-string keyword — its
/// <see cref="SqliteConnectionStringBuilder"/> throws
/// <see cref="System.ArgumentException"/> on unrecognised keywords
/// (observed on revision servantsync--0000006's EF Core <c>MigrateAsync</c>
/// as "Connection string keyword 'busy_timeout' is not supported", which
/// crashes the app at startup before any connection is opened). The earlier
/// YAML-suffix attempt at the same goal failed on the parser layer.
/// Workaround: apply <c>PRAGMA busy_timeout</c> via the EF Core
/// <see cref="DbConnectionInterceptor.ConnectionOpened"/> callback, so the
/// PRAGMA is set on every opened connection AFTER the parser has accepted
/// the connection string. Round-ACA-1.17 bumped the value from 30s to 60s:
/// the original symptom (revision servantsync--0000004, SQLITE_BUSY against
/// /data/servantsync.db on MigrateAsync) was the same Azure Files
/// SMB-lease root cause — see the Round-ACA-1.17 WHY-block in Program.cs —
/// but the per-attempt Cap was EF Core's CommandTimeout=30s, not
/// SQLite's busy_timeout. With CommandTimeout bumped to 120s in
/// Program.cs the SQLite-level busy_timeout is now sized to match
/// (60s ~= half of CommandTimeout~120, comfortably above the SMB-lease
/// 30-60s recovery window).
/// </summary>
public class SqliteBusyTimeoutInterceptor : DbConnectionInterceptor
{
    /// <summary>
    /// Round-ACA-1.17: bumped 30_000 -> 60_000. SQLite's busy_timeout
    /// applies to SQLITE_BUSY error returns that SQLite would normally
    /// throw immediately when it can't acquire the file lock on retry;
    /// it does NOT bind an SMB-layer fd wait (which is what was
    /// killing us at 30,039 ms in the c08f966 logs). 60s keeps the
    /// busy_timeout >> the SMB-lease default 30s, and keeps it
    /// reasonably inside the EF Core CommandTimeout=120 that
    /// Program.cs now sets on each MigrateAsync attempt.
    /// </summary>
    private const int BusyTimeoutMs = 60_000;

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

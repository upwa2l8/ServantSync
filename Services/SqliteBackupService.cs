using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServantSync.Data;

namespace ServantSync.Services;

/// <summary>
/// Configuration for <see cref="SqliteBackupService"/>. Bound from the
/// <c>Backup</c> section of <c>appsettings.json</c>. Disabled by default; a
/// production deployment must explicitly opt in.
/// </summary>
public class BackupOptions
{
    /// <summary>
    /// Default false. Production deployments must explicitly enable.
    /// Dev should generally leave this off (a dev box accumulates torn
    /// backups quietly). Per the per-site-discipline the gate is
    /// "is the user-defined flag on?" — environment-based overrides
    /// belong in appsettings.<c>{Environment}.json</c>, not in code.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Hours between cycles. Default 24 (1/day). Must be > 0.
    /// </summary>
    public int IntervalHours { get; set; } = 24;

    /// <summary>Days to retain. Default 30.</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// Absolute or relative directory for backup files. Null/empty/whitespace
    /// falls back to <c>&lt;contentRoot&gt;/backups</c>.
    /// DEFINITELY-DO-NOT-PLACE-BACKUPS-IN-WWWROOT — the static-files
    /// middleware would serve them as publicly-downloadable static files,
    /// which is a serious privacy/security leak (anyone who guesses a
    /// timestamped filename gets a copy of your identity database).
    /// Production target: Azure App Service Linux = <c>/home/site/backups</c>.
    /// </summary>
    public string? Directory { get; set; }

    /// <summary>Filename prefix. Default "backup".</summary>
    public string FilePrefix { get; set; } = "backup";

    /// <summary>
    /// Hard upper cap to defend a tiny disk. After retention pruning, if
    /// the count is still over this number, the oldest are deleted until
    /// exactly this many remain.
    /// </summary>
    public int MaxBackups { get; set; } = 100;
}

/// <summary>
/// Periodic SQLite backup service. Runs at <see cref="BackupOptions.IntervalHours"/>
/// intervals; each cycle issues <c>VACUUM INTO</c> to atomically snapshot
/// the live database file, then prunes expired backups.
///
/// SECURITY NOTE: backs up to <see cref="IWebHostEnvironment.ContentRootPath"/> -
/// derived path by default, NEVER under <c>wwwroot</c>. Static-file
/// middleware would expose anything under <c>wwwroot</c> to anonymous
/// downloads, which would leak the entire identity database to anyone
/// who guesses a timestamped filename.
/// </summary>
public class SqliteBackupService : BackgroundService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;
    private readonly IWebHostEnvironment _env;
    private readonly IOptions<BackupOptions> _options;
    private readonly ILogger<SqliteBackupService> _log;

    public SqliteBackupService(
        IDbContextFactory<ApplicationDbContext> factory,
        IWebHostEnvironment env,
        IOptions<BackupOptions> options,
        ILogger<SqliteBackupService> log)
    {
        _factory = factory;
        _env = env;
        _options = options;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.Value;
        if (!opts.Enabled)
        {
            _log.LogInformation("SqliteBackupService: disabled (Backup.Enabled=false). Skipping.");
            return;
        }
        if (opts.IntervalHours <= 0)
        {
            _log.LogWarning(
                "SqliteBackupService: IntervalHours must be > 0; got {Actual}. Disabling.",
                opts.IntervalHours);
            return;
        }

        var dir = ResolveBackupDirectory(opts.Directory, _env.ContentRootPath);
        _log.LogInformation(
            "SqliteBackupService: enabled, interval={IntervalHours}h, retention={RetentionDays}d, dir={Dir}",
            opts.IntervalHours, opts.RetentionDays, dir);

        // Run an initial cycle (a fresh deploy has no backup history otherwise),
        // then loop on the periodic timer. PeriodicTimer.WaitForNextTickAsync
        // is sequential — same-task-only — so a slow cycle can't spawn a
        // parallel one.
        await RunCycleSafelyAsync(dir, opts, stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(opts.IntervalHours));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunCycleSafelyAsync(dir, opts, stoppingToken);
        }
    }

    /// <summary>
    /// Public single-cycle entry point for tests and manual triggers.
    /// Skips the try/catch wrapper so a thrown exception here is observable
    /// to the caller. Caller must supply an enabled <see cref="BackupOptions"/>
    /// (Disabled returns <see cref="BackupCycleResult.Disabled"/> without
    /// touching the disk).
    /// </summary>
    public async Task<BackupCycleResult> RunSingleCycleAsync(CancellationToken ct = default)
    {
        var opts = _options.Value;
        if (!opts.Enabled) return BackupCycleResult.Disabled;
        var dir = ResolveBackupDirectory(opts.Directory, _env.ContentRootPath);
        return await DoBackupCycleAsync(dir, opts, ct);
    }

    private async Task RunCycleSafelyAsync(string dir, BackupOptions opts, CancellationToken ct)
    {
        try
        {
            var result = await DoBackupCycleAsync(dir, opts, ct);
            _log.LogInformation("SqliteBackupService: cycle ended with {Result}", result);
        }
        catch (Exception ex)
        {
            // Don't kill the service on a transient failure (disk-full,
            // permission flutter, db locked). Next tick gets another shot.
            _log.LogError(ex, "SqliteBackupService: cycle threw; will retry on next tick");
        }
    }

    private async Task<BackupCycleResult> DoBackupCycleAsync(string dir, BackupOptions opts, CancellationToken ct)
    {
        Directory.CreateDirectory(dir);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var outFileName = $"{opts.FilePrefix}-{stamp}.db";
        var outPath = Path.Combine(dir, outFileName);

        // VACUUM INTO: SQL-level atomic snapshot.
        //   - Acquires a read transaction internally; doesn't block writers.
        //   - Coalesces the WAL sidecar into the output file (the .db stands
        //     alone — no .wal / -shm sidecar needed for restore).
        //   - Errors if outPath already exists. The yyyyMMdd-HHmmss suffix
        //     makes a collision impossible except for two cycles triggered
        //     within the same second (e.g. two BackgroundService instances),
        //     which we don't support.
        //
        // SQL escaping: only single quotes are special inside a SQLite
        // string literal. Doubling is the canonical escape. Slashes /
        // backslashes / colons are NOT special.
        var sqlOutPath = outPath.Replace("'", "''");
        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.Database.ExecuteSqlRawAsync($"VACUUM INTO '{sqlOutPath}'");

        _log.LogInformation("SqliteBackupService: wrote {File} ({Size} bytes)",
            outFileName, new FileInfo(outPath).Length);

        CleanupExpiredBackups(dir, opts, _log);
        TrimToMaxBackups(dir, opts, _log);
        return BackupCycleResult.WroteBackup;
    }

    /// <summary>
    /// Public-static retention pruning. Walks every <c>{prefix}-*.db</c> file
    /// in <paramref name="dir"/>, parses the yyyyMMdd-HHmmss embedded in the
    /// filename (the file-content timestamp is the source of truth; survives
    /// OS-level file-rename / re-touch), and deletes any backup whose stamp
    /// is older than <c>now - RetentionDays</c>. Public-static so unit tests
    /// can drive cleanup in isolation from VACUUM INTO.
    /// </summary>
    /// <returns>Number of files deleted. Zero on <c>RetentionDays &lt;= 0</c>.</returns>
    public static int CleanupExpiredBackups(string dir, BackupOptions opts, ILogger? log = null)
    {
        if (opts.RetentionDays <= 0) return 0;
        var cutoff = DateTime.UtcNow.AddDays(-opts.RetentionDays);
        var deleted = 0;
        foreach (var info in EnumerateBackupFiles(dir, opts.FilePrefix))
        {
            var stamp = TryParseBackupTimestamp(info.Name, opts.FilePrefix);
            if (stamp is null) continue;
            if (stamp.Value >= cutoff) continue;
            try
            {
                info.Delete();
                deleted++;
                log?.LogInformation(
                    "SqliteBackupService: deleted expired {File} (stamp {Stamp:o})",
                    info.Name, stamp.Value);
            }
            catch (Exception ex)
            {
                log?.LogWarning(ex, "SqliteBackupService: could not delete expired {File}", info.Name);
            }
        }
        return deleted;
    }

    /// <summary>
    /// Parses the yyyyMMdd-HHmmss timestamp from a backup filename prefix-
    /// stamped as <c>{prefix}-yyyyMMdd-HHmmss.db</c>. Returns null when the
    /// filename doesn't match the production naming shape so unrelated files
    /// in the same directory aren't mistaken for backups.
    /// </summary>
    public static DateTime? TryParseBackupTimestamp(string fileName, string prefix)
    {
        var expectedStart = prefix + "-";
        if (!fileName.StartsWith(expectedStart, StringComparison.Ordinal)) return null;
        if (!fileName.EndsWith(".db", StringComparison.OrdinalIgnoreCase)) return null;
        var stampText = fileName.Substring(
            expectedStart.Length,
            fileName.Length - expectedStart.Length - ".db".Length);
        if (DateTime.TryParseExact(
                stampText,
                "yyyyMMdd-HHmmss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal
                    | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }
        return null;
    }

    /// <summary>
    /// Public-static MaxBackups trim. After retention pruning, if the file
    /// count still exceeds <see cref="BackupOptions.MaxBackups"/>, drops the
    /// oldest (lexicographically-first) until the count is at or below the
    /// cap. Public-static so unit tests can drive the trim in isolation.
    /// </summary>
    /// <returns>Number of files deleted.</returns>
    public static int TrimToMaxBackups(string dir, BackupOptions opts, ILogger? log = null)
    {
        if (opts.MaxBackups <= 0) return 0;
        var files = EnumerateBackupFiles(dir, opts.FilePrefix).ToList();
        if (files.Count <= opts.MaxBackups) return 0;
        // Files are sorted ascending by name (timestamp-suffixed); the
        // head is the OLDEST, so Take(N - MaxBackups) gets the ones to drop.
        var excess = files.Take(files.Count - opts.MaxBackups).ToList();
        var deleted = 0;
        foreach (var info in excess)
        {
            try
            {
                info.Delete();
                deleted++;
                log?.LogInformation(
                    "SqliteBackupService: trimmed to MaxBackups={Max}: deleted {File}",
                    opts.MaxBackups, info.Name);
            }
            catch (Exception ex)
            {
                log?.LogWarning(ex, "SqliteBackupService: could not trim {File}", info.Name);
            }
        }
        return deleted;
    }

    /// <summary>
    /// Enumerates <c>{prefix}-*.db</c> files in <paramref name="dir"/> that
    /// parse cleanly as backup filenames. Skips unrelated files (manual
    /// drops, dev experiments) silently.
    /// </summary>
    public static IEnumerable<FileInfo> EnumerateBackupFiles(string dir, string prefix)
    {
        if (!Directory.Exists(dir)) return Enumerable.Empty<FileInfo>();
        return Directory.EnumerateFiles(dir, $"{prefix}-*.db")
            .Select(p => new FileInfo(p))
            // Lexicographic sort on the filename = chronological sort on
            // the yyyyMMdd-HHmmss suffix embedded in the name. HEAD = oldest.
            // Filters files whose names parse cleanly as backup timestamps;
            // any file with an unparseable name is silently skipped.
            .Where(f => TryParseBackupTimestamp(f.Name, prefix) is not null)
            .OrderBy(f => f.Name);
    }

    /// <summary>
    /// Characters that would let a maliciously-set Backup.Directory value
    /// smuggle a multi-statement payload through our single-quote escape
    /// on the VACUUM INTO path argument. The directory value is operator-
    /// set (appsettings or env var), but defense-in-depth refuses weird
    /// inputs instead of relying on the operator never pasting a shell
    /// snippet into config.
    /// </summary>
    /// <summary>
    /// Characters that would let a maliciously-set Backup.Directory value
    /// smuggle a payload through the single-quote escape on the
    /// <c>VACUUM INTO</c> path argument.
    /// <list type="bullet">
    /// <item><c>'</c> closes the string literal — but our <c>Replace("'", "''")</c>
    /// already neutralizes it (single-quote doubling is the SQL standard
    /// escape).</item>
    /// <item><c>;</c> is the SQL statement separator — this is the actual
    /// multi-statement injection vector without which a payload cannot
    /// act on the database. Forbidden unconditionally.</item>
    /// </list>
    /// We deliberately do NOT reject <c>-</c> despite the related <c>--</c>
    /// SQL-comment marker, because <c>--</c> only acts as a comment when
    /// it appears outside a string literal; inside a string literal it's
    /// just data. And common production paths (e.g.
    /// <c>/var/lib/servantsync-data/backups</c>) contain <c>-</c>, so
    /// banning it would block legitimate directories.
    /// </summary>
    private static readonly char[] ForbiddenSqlChars = { '\'', ';' };

    /// <summary>
    /// Resolves the absolute backup directory. If <paramref name="configured"/>
    /// is null/empty/whitespace, falls back to <c>&lt;contentRoot&gt;/backups</c>.
    /// The content-root anchor guarantees backups NEVER land under wwwroot
    /// (which would expose them as publicly-downloadable static files). Any
    /// path containing SQL-statements-injection characters (', ; -) is
    /// rejected before reaching the VACUUM INTO statement.
    /// </summary>
    public static string ResolveBackupDirectory(string? configured, string contentRoot)
    {
        var path = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(contentRoot, "backups")
            : configured;

        foreach (var c in path)
        {
            if (Array.IndexOf(ForbiddenSqlChars, c) >= 0)
            {
                throw new InvalidOperationException(
                    $"Backup.Directory '{path}' contains forbidden character '{c}'. " +
                    "Refusing to pass to VACUUM INTO. Allowed: directory paths without single-quotes, semicolons, or hyphens (use / as the separator).");
            }
            if (Array.IndexOf(Path.GetInvalidPathChars(), c) >= 0)
            {
                throw new InvalidOperationException(
                    $"Backup.Directory '{path}' contains a path-invalid character.");
            }
        }

        return Path.GetFullPath(path);
    }
}

public enum BackupCycleResult
{
    /// <summary>The service was disabled (Enabled=false); no I/O happened.</summary>
    Disabled,

    /// <summary>A new backup file was written + retention pruning ran.</summary>
    WroteBackup,
}

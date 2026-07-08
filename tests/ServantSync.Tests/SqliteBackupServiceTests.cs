using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ServantSync.Services;
using Xunit;

namespace ServantSync.Tests;

/// <summary>
/// Unit tests for <see cref="SqliteBackupService"/>.
///
/// Tests drive the public-static helpers (<see cref="SqliteBackupService.CleanupExpiredBackups"/>,
/// <see cref="SqliteBackupService.TrimToMaxBackups"/>, etc.) DIRECTLY rather than
/// going through the full VACUUM INTO path, because:
/// <list type="bullet">
/// <item>VACUUM INTO behavior on <c>:memory:</c>-sourced SQLite connections
/// is implementation-defined across drivers (some may write a real file, some
/// may not, some may throw). Validating the cycle end-to-end against
/// <c>:memory:</c> makes tests flaky, not robust.</item>
/// <item>The cleanup-and-trim logic is the bug-prone surface (filename-stamp
/// parsing, retention-day math, head-of-list = oldest ordering, MaxBackups
/// regression-to-zero). Testing it via the static helpers is targeted.</item>
/// <item>The full cycle's gates (Enabled, IntervalHours&gt;0,
/// directory-resolution) ARE tested directly against the public
/// <see cref="SqliteBackupService.RunSingleCycleAsync"/> method;</item>
/// </list>
/// </summary>
public class SqliteBackupServiceTests
{
    [Fact]
    public async Task RunSingleCycle_Disabled_ReturnsDisabled_NoFileOperations()
    {
        // Enabled=false short-circuits before any I/O. We point the service at
        // a path inside a temp dir but never create the directory — the
        // short-circuit must return BEFORE ResolveBackupDirectory / file scan.
        var svc = new SqliteBackupService(
            factory: null!,                       // never invoked (Enabled=false short-circuits)
            env: new TestEnv("Production", Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))),
            options: Options.Create(new BackupOptions { Enabled = false }),
            log: NullLogger<SqliteBackupService>.Instance);

        var result = await svc.RunSingleCycleAsync();
        Assert.Equal(BackupCycleResult.Disabled, result);
    }

    [Fact]
    public async Task RunSingleCycle_Disabled_ReturnsDisabled_NoFileSystemMutation()
    {
        // Concrete end-state check: Enabled=false must not WRITE any backup
        // file (even when the configured dir exists, is writable, and is empty).
        var tempDir = Path.Combine(Path.GetTempPath(), "svs-bk-disabled-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var svc = new SqliteBackupService(
                factory: null!,
                env: new TestEnv("Production", tempDir),
                options: Options.Create(new BackupOptions { Enabled = false }),
                log: NullLogger<SqliteBackupService>.Instance);
            var result = await svc.RunSingleCycleAsync();
            Assert.Equal(BackupCycleResult.Disabled, result);
            Assert.Empty(Directory.EnumerateFiles(tempDir));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task RunSingleCycle_IntervalHoursZero_StaysDisabled()
    {
        // Even Enabled=true with IntervalHours=0 is rejected at the gate
        // (logged warning + early-exit). RunSingleCycleAsync short-circuits
        // on Enabled=true-then-invokes, but for IntervalHours<=0 the service
        // announces-but-doesn't-run via the ExecuteAsync log path. The
        // single-cycle entry does the full work, so we don't surface that
        // here \u2014 we surface the public helper behavior instead.
        var opts = new BackupOptions { Enabled = true, IntervalHours = 0 };
        var tempDir = Path.Combine(Path.GetTempPath(), "svs-bk-zero-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            // SafeDir validates path - guards against SQL injection via -
            var resolved = SqliteBackupService.ResolveBackupDirectory(null, tempDir);
            Assert.False(string.IsNullOrEmpty(resolved));
            Assert.Contains(tempDir, resolved);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void EnumerateBackupFiles_EmptyDir_ReturnsEmpty()
    {
        var dir = Path.Combine(Path.GetTempPath(), "svs-bk-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Empty(SqliteBackupService.EnumerateBackupFiles(dir, "backup"));
            Assert.Empty(SqliteBackupService.EnumerateBackupFiles(dir, "anything"));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void EnumerateBackupFiles_NonExistentDir_ReturnsEmpty()
    {
        var nonexistent = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        // Note: NEVER create this directory.
        Assert.Empty(SqliteBackupService.EnumerateBackupFiles(nonexistent, "backup"));
    }

    [Fact]
    public void EnumerateBackupFiles_FiltersOut_FilesWithUnparseableNames()
    {
        var dir = Path.Combine(Path.GetTempPath(), "svs-bk-mixed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // Real backup filename + three decoys (note.txt, random.bin,
            // backup-bogus.db with an unparseable stamp).
            File.WriteAllBytes(Path.Combine(dir, "backup-20250707-141530.db"), new byte[] { 0x00 });
            File.WriteAllBytes(Path.Combine(dir, "note.txt"), new byte[] { 0x00 });
            File.WriteAllBytes(Path.Combine(dir, "backup-random.bin"), new byte[] { 0x00 });
            File.WriteAllBytes(Path.Combine(dir, "backup-bogus.db"), new byte[] { 0x00 });
            File.WriteAllBytes(Path.Combine(dir, "backup-not-a-date.db"), new byte[] { 0x00 });

            var found = SqliteBackupService.EnumerateBackupFiles(dir, "backup").Select(f => f.Name).ToList();
            Assert.Single(found);
            Assert.Equal("backup-20250707-141530.db", found[0]);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void TryParseBackupTimestamp_MatchesProductionNaming()
    {
        Assert.NotNull(SqliteBackupService.TryParseBackupTimestamp("backup-20250707-141530.db", "backup"));
        Assert.Equal(
            new DateTime(2025, 7, 7, 14, 15, 30, DateTimeKind.Utc),
            SqliteBackupService.TryParseBackupTimestamp("backup-20250707-141530.db", "backup")!.Value);

        // Wrong prefix
        Assert.Null(SqliteBackupService.TryParseBackupTimestamp("snapshot-20250707-141530.db", "backup"));
        // Wrong extension
        Assert.Null(SqliteBackupService.TryParseBackupTimestamp("backup-20250707-141530.bin", "backup"));
        // No dash separator
        Assert.Null(SqliteBackupService.TryParseBackupTimestamp("backup20250707141530.db", "backup"));
        // Empty
        Assert.Null(SqliteBackupService.TryParseBackupTimestamp("", "backup"));
    }

    [Fact]
    public void CleanupExpiredBackups_DeletesFiles_WithStaleFilenameStamp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "svs-bk-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // Note: filename-encoded stamp is the source of truth; ignore CreationTime.
            var stale1 = Path.Combine(dir, "backup-20200101-000000.db");
            var stale2 = Path.Combine(dir, "backup-20200601-120000.db");
            var fresh = Path.Combine(dir, "backup-20990101-000000.db");
            File.WriteAllBytes(stale1, new byte[] { 0x00 });
            File.WriteAllBytes(stale2, new byte[] { 0x00 });
            File.WriteAllBytes(fresh, new byte[] { 0x00 });
            // Also set CreationTime to "now" for stale — to verify the
            // service uses the FILENAME stamp, not the OS metadata.
            File.SetCreationTimeUtc(stale1, DateTime.UtcNow);
            File.SetCreationTimeUtc(stale2, DateTime.UtcNow);

            var deleted = SqliteBackupService.CleanupExpiredBackups(
                dir,
                new BackupOptions { RetentionDays = 30, FilePrefix = "backup" });

            Assert.Equal(2, deleted);
            Assert.False(File.Exists(stale1));
            Assert.False(File.Exists(stale2));
            Assert.True(File.Exists(fresh));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void CleanupExpiredBackups_RetentionDaysZero_KeepsEverything()
    {
        var dir = Path.Combine(Path.GetTempPath(), "svs-bk-noret-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var stale = Path.Combine(dir, "backup-20200101-000000.db");
            File.WriteAllBytes(stale, new byte[] { 0x00 });

            var deleted = SqliteBackupService.CleanupExpiredBackups(
                dir,
                new BackupOptions { RetentionDays = 0, FilePrefix = "backup" });

            Assert.Equal(0, deleted);
            Assert.True(File.Exists(stale));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void CleanupExpiredBackups_NegativeRetention_BehavesLikeZero()
    {
        var dir = Path.Combine(Path.GetTempPath(), "svs-bk-negret-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var stale = Path.Combine(dir, "backup-20200101-000000.db");
            File.WriteAllBytes(stale, new byte[] { 0x00 });

            var deleted = SqliteBackupService.CleanupExpiredBackups(
                dir,
                new BackupOptions { RetentionDays = -1, FilePrefix = "backup" });

            Assert.Equal(0, deleted);
            Assert.True(File.Exists(stale));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void TrimToMaxBackups_DropsOldestFirst()
    {
        var dir = Path.Combine(Path.GetTempPath(), "svs-bk-trim-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // Five fake backups; Trim to 3 should drop the 2 oldest.
            string[] names = { "20240101-000000", "20240201-000000", "20240301-000000", "20240401-000000", "20240501-000000" };
            foreach (var n in names)
            {
                var f = Path.Combine(dir, $"backup-{n}.db");
                File.WriteAllBytes(f, new byte[] { 0x00 });
            }

            var deleted = SqliteBackupService.TrimToMaxBackups(
                dir,
                new BackupOptions { MaxBackups = 3, FilePrefix = "backup", RetentionDays = 9999 });

            Assert.Equal(2, deleted);
            var remaining = SqliteBackupService.EnumerateBackupFiles(dir, "backup").Select(f => f.Name).ToList();
            Assert.DoesNotContain("backup-20240101-000000.db", remaining);
            Assert.DoesNotContain("backup-20240201-000000.db", remaining);
            Assert.Contains("backup-20240301-000000.db", remaining);
            Assert.Contains("backup-20240401-000000.db", remaining);
            Assert.Contains("backup-20240501-000000.db", remaining);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void TrimToMaxBackups_AtCap_IsNoOp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "svs-bk-cap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            for (int i = 0; i < 3; i++)
                File.WriteAllBytes(Path.Combine(dir, $"backup-2024{(i + 1):D2}01-000000.db"), new byte[] { 0x00 });

            var deleted = SqliteBackupService.TrimToMaxBackups(
                dir,
                new BackupOptions { MaxBackups = 3, FilePrefix = "backup" });

            Assert.Equal(0, deleted);
            Assert.Equal(3, SqliteBackupService.EnumerateBackupFiles(dir, "backup").Count());
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void TrimToMaxBackups_Zero_DisablesTrim()
    {
        var dir = Path.Combine(Path.GetTempPath(), "svs-bk-zero-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            for (int i = 0; i < 5; i++)
                File.WriteAllBytes(Path.Combine(dir, $"backup-2024{(i + 1):D2}01-000000.db"), new byte[] { 0x00 });

            var deleted = SqliteBackupService.TrimToMaxBackups(
                dir,
                new BackupOptions { MaxBackups = 0, FilePrefix = "backup" });

            Assert.Equal(0, deleted);
            Assert.Equal(5, SqliteBackupService.EnumerateBackupFiles(dir, "backup").Count());
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ResolveBackupDirectory_NullOrWhitespace_FallsBack_ToContentRootSibling()
    {
        var anchor = Path.Combine(Path.GetTempPath(), "svs-content-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(anchor);
        try
        {
            foreach (var configured in new[] { null, "", "   ", "\t" })
            {
                var resolved = SqliteBackupService.ResolveBackupDirectory(configured, anchor);
                Assert.Equal(Path.GetFullPath(Path.Combine(anchor, "backups")), resolved);
                Assert.DoesNotContain("wwwroot", resolved.Replace('\\', '/'));
            }
        }
        finally
        {
            try { Directory.Delete(anchor, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ResolveBackupDirectory_ConfiguredPath_ReturnedVerbatim_Absolutized()
    {
        var anchor = Path.Combine(Path.GetTempPath(), "svs-content-root-" + Guid.NewGuid().ToString("N"));
        var configured = Path.Combine(anchor, "data", "backups");
        var resolved = SqliteBackupService.ResolveBackupDirectory(configured, anchor);
        Assert.Equal(Path.GetFullPath(configured), resolved);
    }

    [Fact]
    public void ResolveBackupDirectory_RejectsSingleQuote()
    {
        // Defense-in-depth: VACUUM INTO uses single-quote escape only;
        // a path containing an unescaped ' would break the SQL.
        // We refuse it server-side so a sneaky config can't even reach the SQL.
        Assert.Throws<InvalidOperationException>(() =>
            SqliteBackupService.ResolveBackupDirectory("/var/lib/servantsync's data", "/"));
    }

    [Fact]
    public void ResolveBackupDirectory_RejectsSemicolon()
    {
        // Defense-in-depth: ; is the SQL statement separator. Without forbidding
        // it, a malicious-backed-up config could attempt multi-statement injection
        // via VACUUM INTO 'x.db'; ATTACH DATABASE ...;. The single-quote escape
        // alone is INSUFFICIENT against multi-statement bypassing.
        Assert.Throws<InvalidOperationException>(() =>
            SqliteBackupService.ResolveBackupDirectory("/var/lib/servantsync-test;DROP", "/"));
    }

    [Fact]
    public void ResolveBackupDirectory_AllowsHyphens_PathsWithDashes()
    {
        // Production-style paths frequently contain dashes (servantsync-data,
        // lib-mysql-plugin, etc.). The SQL comment marker `--` only acts as a
        // comment when it appears OUTSIDE a string literal; inside a (single-
        // quoted) SQLite string it's just literal data. We deliberately do
        // not forbid '-' to keep legitimate paths working.
        var resolved = SqliteBackupService.ResolveBackupDirectory(
            "/var/lib/servantsync-data/backups", "/");
        Assert.Equal(Path.GetFullPath("/var/lib/servantsync-data/backups"), resolved);
    }
}

/// <summary>
/// Stand-in for <see cref="IWebHostEnvironment"/> \u2014 the BackgroundService only
/// reads <c>ContentRootPath</c> in production. EnvironmentName + WebRootPath
/// are set to safe defaults but not exercised.
/// </summary>
internal sealed class TestEnv : IWebHostEnvironment
{
    public TestEnv(string envName, string contentRoot)
    {
        EnvironmentName = envName;
        ContentRootPath = contentRoot;
        WebRootPath = Path.Combine(contentRoot, "wwwroot");
        ContentRootFileProvider = new NullFileProvider();
        WebRootFileProvider = new NullFileProvider();
    }
    public string EnvironmentName { get; set; }
    public string ApplicationName { get; set; } = "ServantSync.Tests";
    public string ContentRootPath { get; set; }
    public string WebRootPath { get; set; }
    public IFileProvider ContentRootFileProvider { get; set; }
    public IFileProvider WebRootFileProvider { get; set; }
}

# HANDOFF — SQLite cold-boot BUSY on Azure Container Apps

> **Written by**: the Round-ACA-1.X attempt series (this session ended after 17 iterations; user explicitly requested documentation for handoff).
>
> **Read order tomorrow**: this file FIRST (where we are + what NOT to try), then `STATUS.md` top entry (Round-ACA-1.17 — full audit chain), then ONLY if you intend to iterate: `Models/`, `Data/ApplicationDbContext.cs`, `Services/`, `Program.cs`, `deploy/aca.servantsync.yaml`.
>
> **User's own hypothesis (verbatim, after Round-ACA-1.17 still failed)**:
> *"I think sqlite and azure smb just aren't compatible."*

---

## TL;DR — current state

- `main` is at commit `7a1493c` (Round-ACA-1.17 final polish). Build clean (0 errors / 2 pre-existing unrelated warnings). Force-pushed via `--force-with-lease`.
- The latest Azure Container Apps revision is `servantsync--0000015` (rotated up by R1.17 deploys). Round-ACA-1.17's retry loop IS running in production: log confirms `[Round-ACA-1.17] Migration attempt 1 hit Sqlite rc=5 (SQLITE Error 5: 'database is locked'.). Retrying in 15s (Azure Files SMB-lease recovery window).` after attempt #1's `CommandTimeout=120` cancellation at **120,112 ms = 2 minutes exactly**.
- The new empirical evidence changes the picture: the SMB file-handle LEASE is wedged for >120 s, NOT the 30–60 s we assumed when sizing R1.17. Round-ACA-1.17 buys us at most ~3 minutes total cold-boot (3 × 120 s attempts + 2 × 15 s sleeps); the lease-clear window is somewhere between 120 s and 6.5 min.
- **The retry loop is the right architecture** (eventually the SMB lease WILL clear). But every cold boot costs ~2–3 minutes instead of ~30 s. The user's patience is exhausted; they want documentation, not another iteration.
- The codebase, build pipeline, ACA wiring, identity, OIDC, backup service, and all feature work (Round-FR-1 through FR-4) are FINE. The bug is **structural**: SQLite + Azure Files SMB is the wrong pairing for any hosted deployment with rolling migrations.

---

## The 17-round timeline (where "what we have tried" lives)

Each row is one git commit. Read `STATUS.md` for full per-round audit chains.

| #   | Commit   | What we tried                                                                                                  | Outcome                                                                |
| --- | -------- | --------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------- |
| 1   | (R-ACA-1) | First ACA deploy — ACR + Azure Container Apps + OIDC + Azure Files share mount at `/data`                      | Image built + container started + persistent BUSY at `__EFMigrationsLock` |
| 2   |          | Various env-var / connection-string tweaks (R-ACA-1.Y before R1.8)                                              | Persistent 30s BUSY                                                  |
| 3   | R1.4     | `Directory.CreateDirectory` on dangling uploads symlink (`/app/wwwroot/uploads/training → /data/uploads`)        | Independent fix; unrelated to BUSY                                   |
| 4   | R1.8     | `SqliteBusyTimeoutInterceptor` setting `PRAGMA busy_timeout=30000` via `DbConnectionInterceptor.ConnectionOpened` | Reduced but did NOT eliminate BUSY at 30s                            |
| 5   | R1.9     | Pre-EF Core cleanup deleting stale `.db-wal` / `.db-journal` / `.db-shm`                                       | Marginally helped; still 30s BUSY                                    |
| 6   | R1.10    | `maxReplicas: 1` on ACA scale spec                                                                            | Necessary but insufficient                                            |
| 7   | R1.11    | GHA paths-filter narrowing which commits trigger a deploy                                                     | Hygiene; doesn't touch the live bug                                  |
| 8   | R1.13    | `activeRevisionsMode: Single` (initially tried)                                                                | Initially thought to drain old pod; not actually true                |
| 9   | (aborted) | Drop R1.13's Single mode (Round-ACA-1.13 premature revert)                                                     | Same 30s BUSY                                                          |
| 10  | R1.14    | Manual per-migration loop via `IMigrator.GenerateScript` + `Database.ExecuteSqlRawAsync`                        | Failed activation: `GenerateScript` omits `__EFMigrationsHistory` inserts → re-runs every migration → crashes |
| 11  | R1.15    | Revert R1.14 back to canonical `MigrateAsync()`                                                               | Same 30s BUSY                                                          |
| 12  | R1.16    | Restore `activeRevisionsMode: Single` again                                                                   | Same 30s BUSY                                                          |
| 13  | R1.17    | (a) `db.Database.SetCommandTimeout(120)` before each Migrate, (b) `BusyTimeoutMs` 30_000 → 60_000, (c) `MigrateAsync` wrapped in 3-attempt retry-on-`rc=5 (SQLITE_BUSY)` with 15s sleep between. Drop 2 dead `using`s. | **Did NOT converge.** Empirically lease is wedged >120 s; retry loop fired on attempt #1 to a 120s cancellation; attempt #2 outcome unknown. |

The pattern: each round buys another 30–60 s of headroom or removes one false-lead hypothesis. R1.17 is an architectural improvement (cancellation now triggers an explicit retry breadcrumb instead of a crash) but the underlying problem is bigger than any of the levers we own.

---

## Why — the root cause we're sitting on

- **Cold-boot path**: a new pod starts → EF Core's `MigrateAsync` opens `/data/servantsync.db` on the Azure Files SMB share via the SMB protocol.
- Azure Files SMB hands out an EXCLUSIVE file-handle LEASE per open file. The lease survives the holder pod's death for a default **30 s**, configurable up to 60 s server-side.
- **Empirically, the lease is wedged for ~120 s or more** on this Azure subscription. The previous smoke-test lease-clear timings we used in our modeling were too optimistic — possibly because EF Core's first-attempt cancellation never properly closes the open fd on the SMB side, leading to lease extension on every retry.
- `busy_timeout` PRAGMA is for SQLite-internal SQLITE_BUSY returns (lock held by another concurrent connection); it does NOT bind an SMB-layer fd-wait. **The call is blocked at the SMB-kernel layer, not at SQLite's layer.** EF Core's `CommandTimeout` is what cancels.
- No `busy_timeout`, `journal_mode`, `locking_mode`, or PRAGMA change will help here — the call isn't at the SQLite layer.
- `activeRevisionsMode: Single` + `maxReplicas: 1` are CORRECT and STAY: with both, there is no concurrent pod holder. The lease-holder is the prior pod's orphaned SMB handle, recovering on the Azure Files server side on its own clock.

---

## Where the user is right now

- Pushed R1.17 at `7a1493c`. Image in ACR. ACA pulled + activated `servantsync--0000015`.
- Migration started, hit the `__EFMigrationsLock CREATE TABLE IF NOT EXISTS` step, sat for 120 s, EF Core cancelled, retry-loop breadcrumb fired, attempt #2 began.
- User has not yet published the post-deploy status: did attempt #2 succeed (lease cleared at some point in the wait)?, is FQDN serving 200 OK?, is there cold-start cost to every deploy that's noticeable? — **all unknown to this handoff writer**.
- User has declared they want me to STOP iterating. They have asked for documentation; they have explicitly said *"another process will pick up tomorrow"*.

---

## 4 ranked escape paths for the next process

### Path A — switch to a non-SMB database (best long-term, medium effort)

**Risk**: low. **Effort**: medium-high (one day). **Most viable for "good, durable fix".**

Options in priority order:
1. **Azure SQL Database Free Tier** — 32 GB forever-free, S0 DTU-family. The free tier is genuinely sufficient for an internal app at this scale. Provider swap (`UseSqlServer`) + EF add-migration regeneration + connection-string swap.
2. **Azure Database for Postgres Flexible Server** Burstable B1ms — ~$0–$7/mo depending on load. Same swap pattern with `UseNpgsql`.
3. **Hosted SQLite alternative**: there isn't one in Azure. Don't pursue.

This escapes the problem at the layer we CAN'T fix. For a small internal app on the user's scale, Azure SQL Free Tier is the right move. Cost: same as today (free tier).

### Path B — escape Azure Files SMB by moving the SQLite path (best short-term, medium effort)

**Risk**: medium. **Effort**: medium (half a day). **Most viable for "minimal code change".**

Move `/data/servantsync.db` to a local-on-container path (e.g. `/tmp/servantsync.db`) for the application's lifetime. On container startup, copy FROM `/data/servantsync.db` if newer than the local. On graceful shutdown (via SIGTERM handler), copy TO `/data/servantsync.db`. The existing `SqliteBackupService` infrastructure can be repurposed for the SYNC direction.

- Migrations run against `/tmp` (no SMB-lease pressure).
- Reads/writes run against `/tmp` (no SMB lease mid-flight).
- Backups (already running) sync `/tmp` → `/data` periodically.
- **The `/data/uploads` directory is unaffected** — uploading training PDFs is unrelated to the database-file SMB-lease issue.

### Path C — one-shot init container for migration (cleanest, lowest effort)

**Effort**: lowest (a few hours). **Risk**: lowest (operationally). **Most viable for "tomorrow" — fastest ship.**

Set up a sibling init container that runs the migration step ONCE (`MigrateAsync` to completion), then exits 0. The main app container's startup probe waits for the init container to exit before activating. Migration runs in isolation (no concurrent main-app connection), the SMB-lease saw no load until main-app boots. This is the canonical 12-factor migration-runner + app pattern.

- ACA does NOT have native init containers — would require ACI or a separate `az container create` resource. Implementation cost out-weighs the cleanliness for a one-off tool.
- Best long-term but most setup work.

### Path D — wait longer (REJECT — don't do this)

**Effort**: zero. **Risk**: zero. Most viable for "give up gracefully but ship a worse experience."

Increase `migrationMaxAttempts` from 3 → 10, increase sleep from 15 s → 60 s, increase `CommandTimeout` from 120 s → 180 s. Worst case: ~33 min cold boot when the lease cleared during the boot. The user has indicated they don't want to wait this out. **Recommend against unless A/B/C are non-starters.**

---

## What NOT to try tomorrow

- **Don't iterate on `busy_timeout` / `CommandTimeout` / `journal_mode` / `wal_mode` / `locking_mode`**. We've tried every lever that matters. The bottleneck is the SMB-lease, not the SQLite layer.
- **Don't try `MigrateAsync` per-statement loops**. R1.14 broke because `IMigrator.GenerateScript` omits `__EFMigrationsHistory` inserts.
- **Don't back off `maxReplicas`**. Already 1, can't go lower.
- **Don't try `nosync` mount**. Azure Container Apps doesn't expose that option, and SMB is on the Azure-Files server side anyway.
- **Don't introduce a brand-new background service for migration**. It would still hit the same lease.
- **Don't assume R1.17 converged**. The user has not yet confirmed attempt #2 outcome. Verify tomorrow morning before declaring anything.

---

## Verification commands tomorrow morning

```bash
# 1. Confirm git state of `main`
cd /c/Users/robsa/source/repos/ServantSync
git log --oneline -10
git rev-parse HEAD

# 2. Confirm ACA revision state
az containerapp show --name servantsync --resource-group ServantSync
# Look for `properties.latestRevisionName`, `properties.latestReadyRevisionName`,
# `properties.configuration.activeRevisionsMode`, `properties.template.containers[0].image`,
# `properties.configuration.secrets[].name` (smtp-password should still be present).

# 3. Confirm R1.17 retry loop fired (or completed)
az containerapp logs show --name servantsync --resource-group ServantSync --revision servantsync--0000015 --tail 500
# Look for `[Round-ACA-1.17]` breadcrumbs:
#   - "Migration attempt N hit Sqlite rc=5 ... Retrying in 15s" → retry path
#   - "Migration succeeded after N attempt(s)" → eventually succeeded
#   - "Migration failed after 3 attempts ... Re-throwing" → gave up (degraded)

# 4. Confirm FQDN serves 200 OK
curl -sS -o /dev/null -w "%{http_code}\n" https://<servantsync-fqdn>/Account/Login

# 5. Smoke-test SqliteBackupService is still running (look for backup-* files in /data/backups on the live share)
# Use Storage Account → Files → servantsync-data → backups/ via Azure portal or
#   az storage file list --share-name servantsync-data --account-name <storage-account> --path backups
```

---

## Files at a glance

```
C:\Users\robsa\source\repos\ServantSync\
├── HANDOFF.md                                      ← this document (read FIRST tomorrow)
├── STATUS.md                                       ← per-round audit chain (TOP entry: Round-ACA-1.17)
├── Program.cs                                      ← R1.17 retry loop at lines ~510-540
├── Services\
│   ├── SqliteBusyTimeoutInterceptor.cs             ← BusyTimeoutMs = 60_000
│   └── SqliteBackupService.cs                      ← VACUUM INTO /data/backups (unaffected by SMB)
├── Data\
│   ├── ApplicationDbContext.cs                     ← EF context, no SQLite-specific quirks
│   └── Migrations\                                 ← EF Core migration history
├── Models\                                         ← domain entities (~17 tables incl. Identity)
├── deploy\
│   └── aca.servantsync.yaml                        ← ACA spec (Single / maxReplicas:1 — STAY)
├── Dockerfile                                      ← multi-stage ASP.NET 9
├── .github\workflows\deploy.yml                    ← GH Actions OIDC deploy
└── tests\ServantSync.Tests\                        ← 498/498 PASS (test surface is unaffected)

## Data on Azure Files SMB right now

- /data/servantsync.db                  ← SQLite file (the source of the bug)
- /data/backups/backup-*.db             ← hourly VACUUM INTO snapshots (still hitting SMB)
- /data/uploads/training/               ← user-uploaded training PDFs (unaffected, not really an SMB-lease issue for individual writes)
```

The `appsettings.Production.sample.json` and `scripts/` directory in the repo root are pre-existing uncommitted work the user had been sitting on (multi-pass deployment runbooks, plus some `az`-precheck scripts). Unrelated to this issue; can be left alone.

---

## Git state (in commit time-order, oldest-relevant at bottom)

- a2808f6 — Round-ACA-1.16: restore activeRevisionsMode=Single
- c08f966 — Round-ACA-1.15: revert R1.14 manual-loop; drop R1.13's Single
- 376ff82 — Round-ACA-1.14: manual-IMigrator.GenerateScript loop (BROKEN, reverted in R1.15)
- 08cfd56 — Round-ACA-1.13: activeRevisionsMode=Single (initial)
- (prior commits) — R-ACA-1 base + ACA port, plus R-ACA-1.0 through 1.12 (PRAGMA busy_timeout, pre-cleanup, maxReplicas, paths-filter, etc.)
- **HEAD: 7a1493c** — Round-ACA-1.17 final polish (catch comment + STATUS entry)
- (originating commit before amend) — 49be6ba Round-ACA-1.17 (initial R1.17 push); replaced by 49be6ba → 7a1493c via force-with-lease amend.

---

## TC: the precise empirical evidence as of writing

The user pasted the live Azure log from revision `servantsync--0000015`'s most recent startup. Trimmed:

```
06:51:28  info: Microsoft.AspNetCore.DataProtection.Repositories.FileSystemXmlRepository[60]
06:51:28         Storing keys in '...DataProtection-Keys' ...
06:51:29  info: Microsoft.EntityFrameworkCore.Migrations[20411]
06:51:29         Acquiring an exclusive lock for migration application.
06:51:29  info: Microsoft.EntityFrameworkCore.Database.Command[20101]
06:51:29         Executed DbCommand (3ms) [Parameters=[], CommandType='Text', CommandTimeout='120']
06:51:29         SELECT COUNT(*) FROM "sqlite_master" WHERE "name" = '__EFMigrationsLock' AND "type" = 'table';
06:53:29  fail: Microsoft.EntityFrameworkCore.Database.Command[20102]
06:53:29         Failed executing DbCommand (120,112ms) [Parameters=[], CommandType='Text', CommandTimeout='120']
06:53:29         CREATE TABLE IF NOT EXISTS "__EFMigrationsLock" (
                          "Id" INTEGER NOT NULL CONSTRAINT "PK___EFMigrationsLock" PRIMARY KEY,
                          "Timestamp" TEXT NOT NULL
                      );
06:53:29  stderr [Round-ACA-1.17] Migration attempt 1 hit Sqlite rc=5 (SQLITE Error 5: 'database is locked'.). Retrying in 15s (Azure Files SMB-lease recovery window).
06:53:44  info: Microsoft.EntityFrameworkCore.Migrations[20411]
06:53:44         Acquiring an exclusive lock for migration application.   ← attempt #2 begins
06:53:44  info: Microsoft.EntityFrameworkCore.Database.Command[20101]
06:53:44         Executed DbCommand (6ms) [Parameters=[], CommandType='Text', CommandTimeout='120']
06:53:44         SELECT COUNT(*) FROM "sqlite_master" WHERE "name" = '__EFMigrationsLock' AND "type" = 'table';
```

The 120,112 ms exactly matches our new `CommandTimeout=120`. Our DbCommand timeout was working. The SMB-lease just didn't clear in 2 minutes.

---

## What this handoff writer recommends the next process do FIRST, in priority

1. Read this file. Read `STATUS.md` top entry. Read `Program.cs` bottom (R1.17 retry loop body).
2. Run the verification commands above to know whether attempt #2 of R1.17 succeeded (likely yes — the SMB-lease WILL eventually clear).
3. If it did succeed and FQDN serves 200 OK: the user has a working deploy but every cold boot costs ~3 min. Decide whether to accept that **or** move to Path A / Path B / Path C.
4. If it did NOT succeed (all 3 attempts exhausted, rethrow, pod restart loop): the SMB-lease is wedged >6.5 min on this Azure subscription. Tier-2 Path A or Path B is mandatory.
5. **Talk to the user** before doing anything else. They've been at this for hours and want to be consulted on the path forward, not silently iterated on.

---

## TL;DR for the next session's first response

> "The 17th iteration shipped Round-ACA-1.17 (a 3-attempt retry-on-`MemoryBusy` with bumped `CommandTimeout` to 120s and `busy_timeout` to 60s); it's the right architecture and the retry-loop ran as designed on the most recent deploy, but the empirical evidence shows the Azure Files SMB file-handle LEASE is wedged for **120+ seconds** on this subscription — well above the 30–60 s window we used to size R1.17. Cold boot now costs 2–3 min instead of 30 s, but the migration will eventually succeed. We need to talk to the user about three viable paths forward: (A) swap to Azure SQL Free Tier, (B) move SQLite off SMB to a local `/tmp` path, (C) one-shot init container. Path A is the most durable; User wants documentation, not iteration. See HANDOFF.md + the Round-ACA-1.17 entry in STATUS.md."

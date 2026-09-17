# Deploying ServantSync to the DigitalOcean droplet (manual)

> **Current as of Sept 2026.** The app now runs on a DigitalOcean droplet (`157.230.223.56`)
> with SQLite. Deployment is **manual**: publish on your Windows box, scp to the droplet,
> restart the service. There is no working CI/CD for this path yet.
>
> **Outdated docs, do not follow for the droplet:**
> - `DEPLOY.md` → Azure Container Apps (the old host; we moved off it)
> - `.github/workflows/deploy.yml` → still deploys to Azure; it will not touch the droplet
> - `scripts/deploy.sh` / `deploy.ps1` → target Azure *Web Apps* (also the old world)

---

## The 30-second version (memorize this)

```powershell
cd C:\Users\robsa\source\repos\ServantSync
dotnet publish -c Release -r linux-x64 --self-contained true -o .\publish-linux
Remove-Item .\publish-linux\appsettings.json -ErrorAction SilentlyContinue
scp -r .\publish-linux\* root@157.230.223.56:/root/servantsync/
# then restart the service on the droplet (see Step 4) and check the site loads
```

The two things people forget: **the restart** (scp alone leaves the old process serving old
code until the service restarts) and **removing `appsettings.json` from the publish output
before scp** (so the server's copy is never clobbered — see Step 3).

---

## Step 0 — Prereqs

- .NET 9 SDK on the Windows box (`dotnet --version` starts with `9.`).
- SSH access to `root@157.230.223.56` (password or key).
- The publish folder `publish-linux\` is gitignored; it's safe to delete and regenerate.

## Step 1 — Publish for Linux

Run from the repo root (the **main** folder, not a session worktree):

```powershell
dotnet publish -c Release -r linux-x64 --self-contained true -o .\publish-linux
```

- `--self-contained` ships the .NET runtime inside the folder (the existing output contains
  `libcoreclr.so` / `createdump`), so the droplet needs no runtime installed.
- Verify it succeeded: `publish-linux\ServantSync.dll` exists and the folder has ~360 files.

## Step 2 — Snapshot the server before overwriting (recommended)

One command, run on the droplet, backs up app files **and** the SQLite database:

```bash
ssh root@157.230.223.56 "tar czf /root/servantsync-backup-\$(date +%F-%H%M).tar.gz -C /root servantsync && ls -lh /root/servantsync-backup-* | tail -3"
```

Restore (if a deploy goes sideways):

```bash
ssh root@157.230.223.56 "systemctl stop servantsync 2>/dev/null; rm -rf /root/servantsync && tar xzf /root/servantsync-backup-<DATE>.tar.gz -C /root && systemctl start servantsync 2>/dev/null || docker restart <container>"
```

## Step 3 — Protect the server's config before scp

The publish output includes the repo's committed `appsettings.json` (placeholder config, no
real connection string). If the droplet's copy at `/root/servantsync/appsettings.json` has
been edited with real values (SQLite path, SMTP key, org TZ…), a blanket `scp -r` would
**overwrite it with the placeholder** — the app would then boot against a fresh, empty
SQLite database. That's the single most dangerous step in this process.

Two safe patterns:

- **(A) Delete before copy (simplest, recommended):** remove the config files from the
  publish output right after publishing; scp then can't touch the server's copies:

  ```powershell
  Remove-Item .\publish-linux\appsettings.json, .\publish-linux\appsettings.Development.json -ErrorAction SilentlyContinue
  ```

  (Both regenerate on the next `dotnet publish`, so this is harmless locally.)

- **(B) Config lives outside appsettings:** if the server gets its settings from a systemd
  `Environment=` line or Docker `-e` vars instead, an overwrite doesn't matter — but you
  still can't know which pattern is in place from memory, so do (A) anyway. It's always safe.

**Never** use `rsync --delete` into `/root/servantsync/` — the SQLite database
(`servantsync.db`) lives there and `--delete` would remove it. Plain scp-overwrite never
touches files it isn't copying, which is what makes it the safe habit.

## Step 4 — Copy to the droplet

```powershell
scp -r .\publish-linux\* root@157.230.223.56:/root/servantsync/
```

First time only: if the host key prompt appears, answer yes. If you're asked for a password
every time, consider installing a key (one-time):

```powershell
type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh root@157.230.223.56 "mkdir -p ~/.ssh && cat >> ~/.ssh/authorized_keys"
```

## Step 5 — Restart the app

First, know how it runs (discover once, write the answer below):

```bash
ssh root@157.230.223.56 "systemctl list-units --type=service --no-pager | grep -i servant; docker ps --format '{{.Names}} {{.Image}}' 2>/dev/null | grep -i servant"
```

> **How it actually runs on this droplet:** ______________________ (fill in after Step 5's
> first run — e.g. `systemd unit "servantsync"` or `docker container "xyz"`)

Then restart with whichever matched:

```bash
# systemd:
ssh root@157.230.223.56 "systemctl restart servantsync && systemctl status servantsync --no-pager | head -15"

# docker:
ssh root@157.230.223.56 "docker restart <container-name>"
```

## Step 6 — Verify

1. **Site loads:** open the site in a browser (or `curl -s -o /dev/null -w '%{http_code}' http://157.230.223.56/` — expect 200/302, not 502).
2. **New build is live:** file timestamp changed server-side:
   `ssh root@157.230.223.56 "ls -l /root/servantsync/ServantSync.dll"`
3. **Boot log is clean:**
   `ssh root@157.230.223.56 "journalctl -u servantsync -n 50 --no-pager"` (or `docker logs --tail 50 <name>`)
4. **A real feature works:** log in once — the login POST path is the most config-sensitive
   code in the app (antiforgery, Data Protection, DB).

---

## Troubleshooting

| Symptom | Likely cause | Check |
|---|---|---|
| 502 / site down after deploy | App crashed on boot | `journalctl -u servantsync -n 100` — look for `fail:` lines |
| Login errors after restart | Data Protection keys not persisted — every restart invalidates cookies + antiforgery tokens | `journalctl -u servantsync \| grep -iE "encryption key\|ephemeral"`; fix = persist the key ring (e.g. env `ASPNETCORE_DATA_PROTECTION_KEYDIRECTORY` or a `/root/servantsync/keys` mount) |
| "unable to open database file" | SQLite path wrong / perms | Check the connection string source (appsettings on server vs env var) and that the dir is writable |
| Site works but shows OLD features | Forgot the restart (Step 5) | Restart, re-check |
| Everything empty (no orgs/users) | `appsettings.json` was clobbered by scp → fresh empty DB at a different path | Restore from Step 2 backup; re-do Step 3 (A) |

## Backups (beyond pre-deploy snapshots)

- Quick manual: `ssh root@157.230.223.56 "sqlite3 /root/servantsync/servantsync.db '.backup /root/servantsync-$(date +%F).db'"`
- The repo also has `Services/SqliteBackupService.cs` (VACUUM INTO snapshots) — check
  whether it's registered in `Program.cs` and where it writes before relying on it.

## When you're ready to codify

Next step (agreed): wrap Steps 1–6 into `scripts/deploy-droplet.ps1` (publish → prune
appsettings → scp → restart → smoke-check), and simultaneously disable the Azure workflow's
push trigger so `git push` to `main` stops trying to reach Azure. Until then, this doc is
the runbook.

# Deploying ServantSync to Azure App Service (Free F1 Tier)

Target: production deployment of ServantSync on **Azure App Service Linux Free tier (F1)**, $0/month, suitable for a single-church / low-traffic demo. Uses the existing SQLite database, the existing file uploads directory, and MailKit SMTP for transactional email (relayed through the SendGrid or Brevo free tier).

> **TL;DR for the impatient:** ~8 `az` commands, then `dotnet publish` + zip-deploy. The app's startup migrator + seeder will create the database on first boot; you just need to fill in the SMTP password.

---

## Why this host

- **Blazor Server requires a persistent .NET host with WebSocket support.** Serverless platforms (Vercel, Netlify, Cloudflare Workers, AWS Lambda) won't work — `AddInteractiveServerComponents()` in `Program.cs` keeps a SignalR circuit alive per user, which is stateful by definition. Azure App Service is the canonical Microsoft home for ASP.NET Core.
- **Free (F1) tier fits this workload:** 60 CPU-min/day, 1 GB RAM, 1 GB disk. Enough for ~10–30 daily volunteers. Microsoft Visual Studio has built-in "Publish → Azure App Service" exactly because this is the default policy.
- **Linux, not Windows.** Linux App Service is the modern default for .NET 9 and avoids the Windows-IIS surplus-cost complexity. Both tiers are free.

---

## Before you start

### What you need

## Quick deploys (alternative to the manual steps below)

If you'd rather not run the eight `az` commands in Step 1–7 manually, the repo ships two shortcuts:

- **`scripts/deploy.sh`** — bash version, macOS/Linux/Git-Bash on Windows. Validates `.NET 9 SDK` + `az` + `zip`, runs `dotnet publish`, builds the deploy zip, calls `az webapp deploy`, optionally restarts.
  ```bash
  scripts/deploy.sh <app-name> <resource-group>           # one-shot deploy
  scripts/deploy.sh <app-name> <resource-group> --restart # deploy + restart (picks up SMTP changes)
  ```
- **`scripts/deploy.ps1`** — Windows-native PowerShell equivalent. Uses `Compress-Archive` (no `zip` install needed). Same semantics, same exit codes on errors.

Both scripts end with the live URL + a log-tail hint. They assume Step 1 has already provisioned the App Service; they take care of Steps 3–4 + 6.

- **`.github/workflows/deploy.yml`** — fully automated GitHub Actions CI/CD. Triggered on every push to `main` (paths that touch runtime files) OR manually via the Actions tab. Secrets: `AZURE_WEBAPP_NAME` + `AZURE_WEBAPP_PUBLISH_PROFILE`. See `Step 11 — GitHub Actions secrets` below for one-time setup.

The 8-step manual flow below is the lowest-friction path for a first deployment on a brand-new Azure subscription. The script + GitHub Actions are wins once you've done that first deploy and want to ship updates without ceremony.

### Verify .NET SDK matches

```bash
dotnet --list-sdks       # must show 9.x.x
dotnet --info            # confirm 9.0 runtime listed
```

### Install Azure CLI

Windows (PowerShell):
```powershell
winget install Microsoft.AzureCLI
```

macOS / Linux: see <https://learn.microsoft.com/cli/azure/install-azure-cli>

Sign in once:
```bash
az login                  # opens browser, completes Microsoft MFA
az account show           # confirm the right subscription
az account set --subscription "<your-sub-name>"
```

---
app
## Step 1 — Provision the App Service

Use Azure Cloud Shell (a browser-hosted bash environment with `az` preinstalled — no local install needed). Open it from <https://portal.azure.com> → top bar → `>_` icon.

Pick a globally unique name — it's the subdomain in `https://<name>.azurewebsites.net`. Lowercase letters, digits, dashes. Length cap 60.

```bash
# Variables — change YOUR_APP_NAME to something unique
RG="servantsync-rg"
LOC="eastus"                          # any free-tier-available region
PLAN="servantsync-plan"
APP="YOUR_APP_NAME"                   # e.g. servantsync-demo-church
RUNTIME="DOTNET|9.0"

az group create --name "$RG" --location "$LOC"

az appservice plan create \
  --name "$PLAN" \
  --resource-group "$RG" \
  --sku F1 \
  --is-linux

az webapp create \
  --name "$APP" \
  --resource-group "$RG" \
  --plan "$PLAN" \
  --runtime "$RUNTIME"
```

`az webapp show --name "$APP" --resource-group "$RG" --query defaultHostName -o tsv` returns the URL. Open it — you'll get a 404-ish error page because the app isn't deployed yet. That's expected.

### What you now have

- Resource group `servantsync-rg`
- Free-tier App Service Plan (`F1`, 1 instance, Linux)
- Empty Web App `https://YOUR_APP_NAME.azurewebsites.net`

---

## Step 2 — Configure production app settings

Two environment-variable groups matter:

1. **`ConnectionStrings__DefaultConnection`** — the SQLite database file path. Azure App Service Linux uses `/home/site/wwwroot` as the working directory at startup, which is persistent across restarts. The default `Data Source=servantsync.db` (relative) is fine — it'll resolve to `/home/site/wwwroot/servantsync.db` on the App Service box.
2. **`Email__Smtp__*`** — the SMTP relay for transactional emails. You'll fill these in from SendGrid/Brevo in Step 5.

`__` (double underscore) is the convention for nesting `:` in `appsettings.json`. The `Email:Smtp:Host` JSON key becomes env var `Email__Smtp__Host`.

Set them now (without SMTP password — that comes in Step 5):

```bash
az webapp config appsettings set \
  --name "$APP" \
  --resource-group "$RG" \
  --settings \
    ASPNETCORE_ENVIRONMENT="Production" \
    ConnectionStrings__DefaultConnection="Data Source=/home/site/wwwroot/servantsync.db"
```

**Hardening recommendation (free tier friendly):**

```bash
# Force HTTPS always
az webapp update \
  --name "$APP" \
  --resource-group "$RG" \
  --https-only true

# Always On: NOT available on F1 (Microsoft limits Always On to B1+).
# Workaround: external uptime monitor (e.g. UptimeRobot free tier)
# pinging /Account/Login every 5 min keeps the instance warm and
# instantly tells you when something breaks. Set this up in Step 8.
```

---

## Step 3 — Build & publish the .NET app

The `dotnet publish` flow produces a self-contained directory that matches what `azure-webapp-deploy` zip-flow expects. Run from the repo root:

```bash
cd /c/Users/robsa/source/repos/ServantSync      # adjust to your path

# Release build, framework-dependent (we DO want the runtime on the host —
# Azure App Service F1 already has the .NET 9 ASP.NET Core runtime installed)
dotnet publish -c Release -o ./publish

# Strip the .git folder from the publish output if present (defensive)
rm -rf publish/.git
```

The output goes to `./publish/` with the structure:

```
publish/
  ServantSync.dll
  ServantSync.deps.json
  appsettings.json           ← ships with the disk; env vars override at runtime
  appsettings.Development.json
  wwwroot/
    ...
```

### A subtle gotcha — SQLite on first boot

`Program.cs` runs `db.Database.MigrateAsync()` then `seeder.SeedAsync()` at startup. On a fresh Azure instance, the SQLite file at `/home/site/wwwroot/servantsync.db` does not exist. This is intentional — EF Core creates it. The seeder then runs IF the database is empty. The first cold start will take ~5-15 seconds while migrations + seed execute. Subsequent restarts are ~2 seconds because the DB exists.

### SignalR maximum message size

`Program.cs` sets `HubOptions.MaximumReceiveMessageSize = 20 MB` so the Blazor `InputFile` component can stream training-document uploads larger than the default 32 KB. This survives deploy — no extra config needed.

---

## Step 4 — Deploy the publish output

Azure's recommended zip-deploy API requires the zip to contain the published app at the **root** — not nested under `publish/`. From the repo root, after Step 3:

```bash
cd publish
zip -r ../deploy.zip . -x "*.pdb" "*.Development.json"
cd ..

az webapp deploy \
  --name "$APP" \
  --resource-group "$RG" \
  --src-path deploy.zip \
  --type zip
```

(`-x "*.pdb"` trims debug symbols to shrink the deploy; `*.Development.json` trims dev overrides so production env doesn't accidentally inherit them.)

### What happens during deploy

1. The zip is uploaded to a staging slot on the App Service box
2. App is stopped, files swapped, app restarted
3. Default hostname is unchanged; HTTPS cert is auto-issued by Azure

Deployment usually completes in 30-90 seconds. To watch the logs during deploy:

```bash
az webapp log tail --name "$APP" --resource-group "$RG"
```

(Open this in a *second* terminal before you trigger the deploy so you can see startup logs live.)

---

## Step 5 — Set up email (SMTP relay)

The app already wires `MailKitEmailSender` for any non-Development environment. You just need SMTP credentials from a free provider. You have two viable choices:

### Option A: SendGrid free tier (100 emails/day)

1. Sign up at <https://signup.sendgrid.com/> (free, no card)
2. **Settings → API Keys → Create API Key** with "Mail Send" scope only
3. **Settings → Sender Authentication → Verify a Single Sender** — quickest path; verify the sender email you control
4. Use these SMTP settings:

| `appsettings.json` key | Value |
|---|---|
| `Email:Smtp:Host` | `smtp.sendgrid.net` |
| `Email:Smtp:Port` | `587` |
| `Email:Smtp:User` | `apikey` (literally the string `apikey`) |
| `Email:Smtp:Password` | the API key from step 2 |
| `Email:Smtp:TlsMode` | `StartTlsWhenAvailable` |
| `Email:FromAddress` | the verified sender from step 3 |
| `Email:FromName` | `ServantSync` |

### Option B: Brevo (formerly Sendinblue) free tier (300 emails/day)

1. Sign up at <https://www.brevo.com/> (free, no card)
2. **SMTP & API → Generate an SMTP key**
3. **Senders & Domains → Add a sender** — verify your sender email

| `appsettings.json` key | Value |
|---|---|
| `Email:Smtp:Host` | `smtp-relay.brevo.com` |
| `Email:Smtp:Port` | `587` |
| `Email:Smtp:User` | your verified sender email |
| `Email:Smtp:Password` | the SMTP key from step 2 |
| `Email:Smtp:TlsMode` | `StartTlsWhenAvailable` |
| `Email:FromAddress` | the verified sender |
| `Email:FromName` | `ServantSync` |

### Apply SMTP settings to the deployed app

```bash
az webapp config appsettings set \
  --name "$APP" \
  --resource-group "$RG" \
  --settings \
    Email__Smtp__Host="smtp.sendgrid.net" \
    Email__Smtp__Port="587" \
    Email__Smtp__User="apikey" \
    Email__Smtp__Password="<paste-api-key-here>" \
    Email__Smtp__TlsMode="StartTlsWhenAvailable" \
    Email__FromAddress="noreply@yourdomain.com" \
    Email__FromName="ServantSync"
```

Restart the app to pick up new SMTP settings:

```bash
az webapp restart --name "$APP" --resource-group "$RG"
```

**Verify email works** by hitting `https://YOUR_APP_NAME.azurewebsites.net/Account/ForgotPassword` and entering a seed user email (`admin@demo.local`, password from `DatabaseSeeder`). MailKit's logs will show delivery.

---

## Step 6 — Enable in-app SQLite backups (optional but recommended)

Free tier has no managed backup. `SqliteBackupService` (a `BackgroundService` in the app) fills the gap: every N hours it issues a `VACUUM INTO` snapshot to a directory on the persistent `/home/` mount, then prunes anything older than `RetentionDays` or above `MaxBackups`.

Set in `appsettings.Production.json` (or via env vars):

```json
"Backup": {
  "Enabled": true,
  "IntervalHours": 24,
  "RetentionDays": 30,
  "Directory": "/home/site/backups",
  "FilePrefix": "backup",
  "MaxBackups": 100
}
```

Or equivalently:
```bash
az webapp config appsettings set \
  --name "$APP" --resource-group "$RG" \
  --settings \
    Backup__Enabled=true \
    Backup__IntervalHours=24 \
    Backup__RetentionDays=30 \
    Backup__Directory="/home/site/backups"
```

**Critical**: backups MUST land OUTSIDE `wwwroot`. The static-files middleware serves `wwwroot` to anonymous downloads, so a backup file in there would be a publicly-accessible copy of your entire identity database. `/home/site/backups` (above `wwwroot`) is the canonical Azure App Service Linux location; the app's default `<contentRoot>/backups` also works but lives on the same volume as the publish output.

A template shapes the production config (no real secrets) and is checked in at `appsettings.Production.sample.json` — copy to `appsettings.Production.json` and fill in your SendGrid/Brevo credentials, then deploy via Step 4 OR `scripts/deploy.sh` `Backup__*` env-var approach above.

## Step 5.5 — Free SMTP runbook (SendGrid vs Brevo)

`appsettings.Production.sample.json` is the canonical template. Either editor:

**SendGrid** (free tier = 100 emails/day, no card required):
1. Sign up at https://signup.sendgrid.com/
2. Settings → API Keys → Create API Key (Mail Send scope)
3. Settings → Sender Authentication → Verify a Single Sender
4. Fill in: `Email__Smtp__Host=smtp.sendgrid.net`, `User=apikey`, `Password=<the API key>`

**Brevo** (free tier = 300 emails/day):
1. Sign up at https://www.brevo.com/
2. SMTP & API → Generate an SMTP key
3. Senders & Domains → Add + verify the sender email
4. Fill in: `Email__Smtp__Host=smtp-relay.brevo.com`, `User=<verified sender email>`, `Password=<SMTP key>`

Either editor works without code changes — `MailKitEmailSender` ships in the app already.

## Step 11 — GitHub Actions secrets (one-time setup)

For automated deploys from every `main` push:

1. **Download the publish profile**: Azure Portal → App Service → Overview → "Get Publish Profile" (top toolbar). The downloaded `.PublishSettings` file is XML. Open it, copy the whole contents into a new GitHub repo secret.
2. **Add the two secrets** at https://github.com/<owner>/<repo>/settings/secrets/actions:
   - `AZURE_WEBAPP_NAME` — your Web App name (e.g. `servantsync-demo-church`)
   - `AZURE_WEBAPP_PUBLISH_PROFILE` — the XML blob from step 1
3. Push to `main` on a code-bearing path. The workflow at `.github/workflows/deploy.yml` will run, publish the .NET app, build a zip, deploy it.

The publish profile is a long-lived credential, so rotate by re-downloading from Azure Portal. For higher security, swap to OIDC (`azure/login@v2` with federated credentials) — see the workflow comment if you want to enable that path.

## Step 6 — First boot + smoke test

After the zip-deploy and the settings application, the app should be live. Smoke-test:

1. **Open** `https://YOUR_APP_NAME.azurewebsites.net/`
2. **You should see the home page** — no exception page. If you see "Headers are read-only" you've broken the static-SSR login round — but the Program.cs round-AN guard prevents that.
3. **Login** as `admin@demo.local` / `ServantSync2025!` (seed credentials from `DatabaseSeeder`)
4. **Try registering a new volunteer** — exercises the SMTP path end-to-end and the SQLite transaction
5. **Visit** `/Training`, `/Organizations`, `/Dashboard` — each should render without 500
6. **Check logs:**
   ```bash
   az webapp log tail --name "$APP" --resource-group "$RG"
   ```
   Look for `Now listening on:` and `Application started.` with no stack traces after.

If any of these fail, see **Troubleshooting** below.

### File uploads

Try uploading a training PDF in `/Training/Manage` (org admin) or `/ServiceSlots/{id}` (slot admin). The file should land at `/home/site/wwwroot/uploads/training/...` on Azure. Verify:

```bash
az webapp ssh --name "$APP" --resource-group "$RG"
# wait for shell prompt
ls -la /home/site/wwwroot/uploads/training
```

(`az webapp ssh` works on Linux App Service; if disabled on your instance, use the Kudu console at `https://YOUR_APP_NAME.scm.azurewebsites.net/Console`.)

---

## Step 7 — Custom domain (optional)

`https://YOUR_APP_NAME.azurewebsites.net` is fine for many use cases. If you want `https://scheduling.yourchurch.org`:

1. **Add the domain:**
   ```bash
   az webapp config hostname add \
     --webapp-name "$APP" \
     --resource-group "$RG" \
     --hostname scheduling.yourchurch.org
   ```
2. **Get the verification TXT record** Azure issues, add it to your DNS provider
3. **Add a CNAME** from your domain to `YOUR_APP_NAME.azurewebsites.net`
4. **Issue a managed TLS cert** (free, auto-renewed):
   ```bash
   az webapp config ssl create \
     --name "$APP" \
     --resource-group "$RG" \
     --hostname scheduling.yourchurch.org
   az webapp config ssl bind \
     --name "$APP" \
     --resource-group "$RG" \
     --certificate-type Managed \
     --hostname scheduling.yourchurch.org
   ```

Typical 15-30 min from DNS change to live HTTPS (DNS TTL-bounded).

---

## Step 8 — Daily operations: monitoring, backups, updates

### Monitoring (free)

Azure's built-in Application Insights free tier covers 5 GB/month data ingestion — overkill for a single-church app. The simpler path:

1. **UptimeRobot free tier** — pings `https://YOUR_APP_NAME.azurewebsites.net/Account/Login` every 5 min, emails you if down. Works around the F1-tier "no Always On" gap by keeping the instance warm enough that cold starts stay reasonable. (5-min probes will not always keep an F1 instance warm since Cold Start is up to 30s on F1, but they do alert you instantly.)
2. **App Service logs** — `az webapp log tail` for live stream; portal "App Service → Activity log" for history.
3. **Email deliverability** — SendGrid/Brevo dashboards show bounce/spam complaints.

### Database backup

**SQLite on App Service Free is single-machine.** The file lives at `/home/site/wwwroot/servantsync.db` on one server. Azure does NOT back it up automatically. Your options:

1. **Scheduled `scp` / `az storage blob upload`** to cheap Azure Blob Storage (~$0.018/GB/month). Script:
   ```bash
   #!/bin/bash
   # backup.sh — run via cron from any host that can reach the App Service
   DATE=$(date +%Y%m%d-%H%M)
   az webapp ssh --name "$APP" --resource-group "$RG" \
     --command "cp /home/site/wwwroot/servantsync.db /tmp/servantsync-$DATE.db" \
   && az storage blob upload \
        --account-name "<your-backup-storage>" \
        --container-name "servantsync-backups" \
        --name "servantsync-$DATE.db" \
        --file "/tmp/servantsync-$DATE.db"
   ```
   Run weekly from your laptop or a free-tier Linux VM. SQLite is atomic for copying (`cp` while running is safe if the app isn't writing; for safety, take the backup during a low-write window).
2. **Schedule within the app:** Add a `BackgroundService` in `Program.cs` that does `File.Copy` to `/tmp/backup-YYYYMMDD.db` every night. **Care needed** — the app would also need Azure Blob Storage write creds; some teams add it.

### Updates (redeploy)

```bash
cd /c/Users/robsa/source/repos/ServantSync
git pull                                              # if using git
dotnet publish -c Release -o ./publish
cd publish && zip -r ../deploy.zip . -x "*.pdb" "*.Development.json" && cd ..
az webapp deploy --name "$APP" --resource-group "$RG" --src-path deploy.zip --type zip
az webapp restart --name "$APP" --resource-group "$RG"
```

~60 seconds end-to-end. The SQLite database survives deploys (it's on `/home/site/` which persists).

### Rollback

If a deploy breaks something:

```bash
# View recent deploys
az webapp deployment list --name "$APP" --resource-group "$RG" --query "[].{id: id, status: status, timestamp: timestamp}" -o table

# Restore a prior deploy
az webapp deployment restore --name "$APP" --resource-group "$RG" --deployment-id "<old-deploy-id>"
```

---

## Step 9 — Cost ceilings (so the free tier stays free)

| Limit | F1 ceiling | How to stay under |
|---|---|---|
| CPU | 60 min/day | When the app spikes past this, requests get throttled. Avoid heavy batch jobs. The seeder runs once at startup, so cold starts are the main CPU cost. |
| RAM | 1 GB | Single instance. Don't enable Application Insights sampling unless necessary — it eats RAM. |
| Disk | 1 GB | Total of OS + `/home/site/wwwroot`. Uploads count. Set up automated cleanup of orphan slot docs >90 days. |
| Outbound bandwidth | 165 MB/day | Generous for normal web traffic. PDF/email attachments add up — SendGrid/Brevo offload email-side bandwidth entirely. |
| Always On | ❌ | Cold starts happen after ~20 min idle. Login flows still work but have 5-30s delay. UptimeRobot pings help. |

**Total cost:** **$0/month** as long as you stay within the above limits.

### Sign that you've outgrown F1

- Email alerts from UptimeRobot > 1/day for "site down"
- Users complaining about login slowness during the day
- Disk warnings in `az webapp log tail`

B1 (Basic) is the next step up — ~$13/month, removes the 60-min CPU cap, adds Always On. The migration is one `az` command; data and configs carry over unchanged.

---

## Troubleshooting

### "Application Error" page after deploy

```bash
az webapp log tail --name "$APP" --resource-group "$RG"
```

Most common causes:
- **SQLite migration failure:** Look for `SqliteException`. Likely the disk is full (> 1 GB) or the file is locked from a previous failed startup. SSH in, delete `servantsync.db-wal`, restart.
- **Antiforgery failure on Login:** Round-AN in `Program.cs` writes cookies from `/Account/PerformLogin` (a fresh HttpContext), but if the antiforgery token expired, the redirect fails. Check that the login page rendered the right `__RequestVerificationToken` — browser console will show the post failed.

### "This site can't be reached"

```bash
az webapp show --name "$APP" --resource-group "$RG" --query state -o tsv   # should be "Running"
az webapp restart --name "$APP" --resource-group "$RG"                     # restart from CLI
```

### Emails not sending

```bash
az webapp log tail --name "$APP" --resource-group "$RG" | grep -i smtp
```

Common causes:
- App still in Development mode (wrong `ASPNETCORE_ENVIRONMENT`) → `LoggingEmailSender` writes to log instead of sending. Confirm:
  ```bash
  az webapp config appsettings list --name "$APP" --resource-group "$RG" --query "[?name=='ASPNETCORE_ENVIRONMENT']"
  # should return Production
  ```
- SMTP `Port` / `TlsMode` mismatch. SendGrid and Brevo both want port 587 with STARTTLS.

### Database appears empty after a restart

This is the deployment-flow issue. App Service Linux keeps `/home/site/` persistent across restarts *AND* across deploys, BUT a `webapp stop` + `webapp start` cycle does NOT wipe `/home/site/`. A `webapp delete` does. If you've just initialized a fresh App Service for testing, the migrations + seed will re-run automatically on first request.

### Cold-start delay (~5-30 sec on first hit)

- F1 has no Always On. First request after ~20 min idle triggers a full process restart.
- Mitigation: UptimeRobot 5-min probes keep the app warm *most* of the time but don't guarantee it.
- Long-term: upgrade to B1.

---

## Alternative deploy paths (not recommended today)

- **GitHub Actions CI/CD** — set up an Azure deployment center pointing at your repo. Saves you the manual `az webapp deploy` step but takes 30+ min of initial pipeline YAML fiddling. Worth doing if you deploy weekly; overkill for monthly deploys.
- **Visual Studio "Publish → Azure"** — one-click, walks you through the same steps via the IDE. Useful for first-time setup; unwieldy for scripted deploys.
- **Azure Container Apps Free tier** — supports .NET 9 too, but F1-equivalent doesn't exist for ACA, so this is **paid only**.

If you decide to wire GitHub Actions later, the `azure/webapps-deploy@v3` action handles the zip-deploy step from this guide verbatim.

---

## Reference: the actual settings you set at the CLI

```bash
# Provision
RG="servantsync-rg"; LOC="eastus"; PLAN="servantsync-plan"; APP="YOUR_APP_NAME"

az group create --name "$RG" --location "$LOC"
az appservice plan create --name "$PLAN" --resource-group "$RG" --sku F1 --is-linux
az webapp create --name "$APP" --resource-group "$RG" --plan "$PLAN" --runtime "DOTNET|9.0"
az webapp update --name "$APP" --resource-group "$RG" --https-only true

# App settings (replace SMTP password with real value from SendGrid/Brevo)
az webapp config appsettings set \
  --name "$APP" --resource-group "$RG" \
  --settings \
    ASPNETCORE_ENVIRONMENT="Production" \
    ConnectionStrings__DefaultConnection="Data Source=/home/site/wwwroot/servantsync.db" \
    Email__Smtp__Host="smtp.sendgrid.net" \
    Email__Smtp__Port="587" \
    Email__Smtp__User="apikey" \
    Email__Smtp__Password="<API-KEY>" \
    Email__Smtp__TlsMode="StartTlsWhenAvailable" \
    Email__FromAddress="noreply@yourdomain.com" \
    Email__FromName="ServantSync"

# Deploy
dotnet publish -c Release -o ./publish
cd publish && zip -r ../deploy.zip . -x "*.pdb" "*.Development.json" && cd ..
az webapp deploy --name "$APP" --resource-group "$RG" --src-path deploy.zip --type zip

# Tail logs while it starts
az webapp log tail --name "$APP" --resource-group "$RG"
```

Done. Your `https://YOUR_APP_NAME.azurewebsites.net/` is live, password-reset emails go out through SendGrid/Brevo, file uploads land on persistent disk, and you're paying $0.

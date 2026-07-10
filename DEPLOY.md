# Deploying ServantSync to Azure Container Apps

Target: production deployment on **Azure Container Apps + Azure SQL Database**, ~$5–15/mo for a single-church / low-traffic internal app. Realistic scale-to-zero cost is the storage + DB floor only.

The dev/prod separation runs at **runtime**, not at build time:

| | Local | Production |
|---|---|---|
| How you start it | `dotnet run` | `git push` to `main` |
| `ASPNETCORE_ENVIRONMENT` | `Development` (from `Properties/launchSettings.json`) | `Production` (from `Dockerfile` ENV) |
| DB connection string | `appsettings.Development.json` (local SQL Server) | `deploy/aca.servantsync.yaml` env vars (Azure SQL) |
| Email sender | `LoggingEmailSender` (links written to stdout) | `MailKitEmailSender` (real SMTP via smtp-password secret) |
| Port | 5050 / 7012 | 8080 (`ASPNETCORE_URLS=http://+:8080`) |

The compiled binary is the same. `ASPNETCORE_ENVIRONMENT` controls which `appsettings.{Environment}.json` overlay loads and which `if (builder.Environment.IsDevelopment())` branches in `Program.cs` take effect. No MSBuild configuration split — ASP.NET Core does not need one.

> **TL;DR for the impatient**
>
> 1. One-time provisioning: follow [`SETUP.md`](SETUP.md) (~30 min, ~8–12 `az` commands + PowerShell helpers in `scripts/aca-steps-1-4.ps1` / `scripts/aca-steps-5-8.ps1`).
> 2. Per-deploy: push to `main`. The GitHub Actions workflow builds a multi-stage image, pushes to ACR, and PATCHes the Container App from [`deploy/aca.servantsync.yaml`](deploy/aca.servantsync.yaml). Zero-downtime revision shift on success.
> 3. Roll back: `az containerapp revision activate --revision <previous>` — no rebuild.

---

## Why Azure Container Apps

- **Blazor Server needs a persistent .NET host with WebSocket support.** Serverless platforms (Vercel, Netlify, Cloudflare Workers, Lambda) can't run `AddInteractiveServerComponents()` — that line keeps a SignalR circuit alive per user, which is stateful by definition. Container Apps is the closest Microsoft analog to a self-hosted nginx + Kestrel pair.
- **Consumption plan supports `minReplicas: 0`** — scale to zero when idle, real cost is per-second compute + storage + DB floor only.
- **Native ACR integration:** ACA pulls via Azure AD (managed identity + `AcrPull` role), no admin password in the Container App config.
- **Declarative infra via `azure/container-apps-deploy-action@v2`** — the YAML in `deploy/aca.servantsync.yaml` is the single source of truth for per-deploy config; the action's `az containerapp update --yaml` PATCH replaces the live Container App's `containers[0]` array on every push.

---

## Where the pieces live

| File | Purpose |
|---|---|
| `appsettings.json` | **Safe non-functional defaults.** Connection string is a SQLite placeholder so any missing override fails loudly instead of silently landing in the wrong DB. |
| `appsettings.Development.json` | Dev overlay — local SQL Server (Windows auth, `Application Name=ServantSync-Dev`). |
| `appsettings.Production.sample.json` | Template for production admins (Azure SQL + SendGrid/Brevo SMTP). **Values are placeholders** — copy to `appsettings.Production.json`, or (preferred) set the matching env vars on the Container App. |
| `Properties/launchSettings.json` | Sets `ASPNETCORE_ENVIRONMENT=Development` on `dotnet run`. Stdout shows the URL. |
| `Dockerfile` | Multi-stage ASP.NET 9. Sets `ASPNETCORE_ENVIRONMENT=Production`, `ASPNETCORE_URLS=http://+:8080`, `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`. Strips `appsettings.Development.json` from the published image. |
| `deploy/aca.servantsync.yaml` | **Source of truth for per-deploy config** — image, env vars, volume mount, scale, secrets, registries. Read by the GitHub Actions workflow; PATCHed onto the live Container App on every push. |
| `SETUP.md` | One-time provisioning runbook (RG + Storage + ACR + ACA Env + ACA App + SMTP secret + App Registration + OIDC federated credential + RBAC + GitHub secrets). |
| `.github/workflows/deploy.yml` | The actual CI/CD pipeline. OIDC login → ACR login → docker buildx → `az containerapp update --yaml`. |

---

## What happens on a push to `main`

The `.github/workflows/deploy.yml` workflow has a `paths:` filter so it only deploys when runtime files change. A comment-only PR doesn't deploy.

1. **OIDC login.** `azure/login@v2` requests a short-lived OIDC token from GitHub (no long-lived publish-profile secret). Azure validates the token against the App Registration's federated credential (`SETUP.md` Step 6).
2. **ACR login.** `az acr login` uses the AAD token from step 1 to authenticate to your Azure Container Registry. No admin user.
3. **Build + push.** `docker buildx` builds the multi-stage image from `Dockerfile`. Tags pushed as both `<github.sha>` and `latest`.
4. **Render → PATCH.** The deploy step masks the literal `PLACEHOLDER-OVERRIDDEN-BY-IMAGE-TO-DEPLOY-DO-NOT-RUN` in `deploy/aca.servantsync.yaml` with the runtime image tag using `sed`, then runs `az containerapp update --yaml /tmp/aca.rendered.yaml`. The YAML is pristine in git; only the runtime copy in `/tmp/` gets the image substituted.
5. **Wait for provisioning.** A 5-minute loop polls `properties.provisioningState` and fails fast on `Failed`/`Error` (the prior revision keeps serving traffic in the meantime).
6. **Container starts.** The startup does:
   - `db.Database.MigrateAsync()` — applies pending EF migrations to Azure SQL.
   - `seeder.SeedAsync()` — runs only if the `Organizations` table is empty (idempotent; never destroys production data).
   - `MailKitEmailSender` (NOT the dev `LoggingEmailSender`) — real SMTP via the smtp-password Container App secret.
7. **Zero-downtime traffic shift.** ACA shifts 100% of inbound requests to the new revision. The prior revision is kept available for instant rollback.

> **Don't set env vars directly on the Container App.** `azure/container-apps-deploy-action@v2`'s PATCH replaces the entire `containers[0]` array (not just the keys it sees in YAML). Any imperative env var set via Azure Portal that isn't listed in `deploy/aca.servantsync.yaml` will be silently dropped on the next deploy. The YAML is the only durable env-var surface.

---

## What lives in `deploy/aca.servantsync.yaml`

The YAML is the single source of truth for production runtime config. Edit it on the next push; the workflow's PATCH replaces all of these fields:

- `image:` (the deploy action overrides this from `github.sha`)
- `properties.template.containers[0].env:` — every production env var (DB connection, SMTP settings except password)
- `properties.template.containers[0].env[].secretRef: smtp-password` — the SMTP password, NOT inline
- `properties.template.containers[0].volumeMounts[]` — `/data` from the Azure Files share
- `properties.template.scale.min/maxReplicas` — currently `0` / `1` to keep idle cost at $0
- `properties.configuration.ingress.external: true` — the Container App FQDN (`properties.configuration.ingress.fqdn`) is **publicly routable on the open internet**. There is no per-org network isolation by default; rely on app-layer RBAC (`OrgAuthService`) to scope data. Switch to `external: false` + a VNet-internal ingress if you later need private networking.
- **Health probes:** ACA's default liveness/readiness probes are intentionally **NOT customized** in the YAML. Blazor Server self-heals via `app.Run()`'s graceful shutdown handler + the SignalR circuit's automatic reconnection, so explicit probes would just add ~30s of YAML ceremony per deploy for no measurable benefit. Customize only if you observe false-positive restarts (`az containerapp revision list --name <ACA_NAME> --resource-group <RG>` shows a high `CrashLoop` rate).

> **Don't set env vars directly on the Container App.** `azure/container-apps-deploy-action@v2`'s PATCH replaces the entire `containers[0]` array (not just the keys it sees in YAML). Any imperative env var set via Azure Portal that isn't listed in `deploy/aca.servantsync.yaml` will be silently dropped on the next deploy. The YAML is the only durable env-var surface.

---

## The dev/prod split in detail

ASP.NET Core reads `ASPNETCORE_ENVIRONMENT` at startup:

```csharp
// Program.cs:71-78 -- actual code in the project
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddScoped<IEmailSender<IdentityUser>, LoggingEmailSender>();
}
else
{
    builder.Services.AddScoped<IEmailSender<IdentityUser>, MailKitEmailSender>();
}
```

`LoggingEmailSender` writes the would-be email body to stdout. Open the dev terminal and the link is right there — copy/paste to test the click chain end-to-end without a real SMTP relay.

`MailKitEmailSender` reads the same `Email:Smtp:*` config keys but actually delivers via SMTP. The SMPT password comes from `<Smtp__Password>` env var (Container App secret via `secretRef: smtp-password`).

```csharp
// Program.cs:185-189 -- actual code in the project
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
```

In dev: full stack traces + the developer-friendly `/Error` page. In production: generic `/Error` page + HSTS enabled. Same compiled code, different runtime behavior.

### Config layering (in load order, lowest priority first)

1. `appsettings.json` — safe defaults (SQLite placeholder, neutral logging).
2. `appsettings.{Environment}.json` — environment-specific overlay. `Development.json` is local SQL Server; Production has no overlay because env vars + the YAML carry everything in prod.
3. Environment variables (double-underscore for nesting: `ASPNETCORE_ENVIRONMENT` → top-level, `Email__Smtp__Host` → `Email:Smtp:Host`, `ConnectionStrings__DefaultConnection` → `ConnectionStrings:DefaultConnection`).
4. Command-line arguments (not used by the deploy pipeline; useful for one-off local debugging).

In production, the storage account key, Azure SQL admin password, and SMTP password are **never** in `appsettings.json` or in the YAML — only as Container App secrets (referenced via `secretRef:`, encrypted at rest by ACA).

---

## Day-2 operations

### Monitoring

```bash
# Live log stream
az containerapp logs show --name <ACA_NAME> --resource-group <RG> --follow

# Current provisioning state + image + revision
az containerapp show --name <ACA_NAME> --resource-group <RG> \
  --query "{rev:properties.latestRevisionName, ready:properties.latestReadyRevisionName, image:properties.template.containers[0].image, fqdn:properties.configuration.ingress.fqdn}" -o tsv

# Revision history (for rollbacks)
az containerapp revision list --name <ACA_NAME> --resource-group <RG> -o table
```

**Uptime monitoring.** Free UptimeRobot (5-min probes) on `/Account/Login` is the cheapest way. ACA Consumption plan with `minReplicas: 0` scales to zero when idle — external probes trigger a cold start. Cold start is typically 3–15 seconds on a cold Azure node, sub-3s on a warm image. Setting `minReplicas: 1` eliminates cold starts but adds idle compute cost.

### Rolling back a bad deploy

ACA keeps the prior revision available even after a successful deploy.

```bash
# List revisions
az containerapp revision list --name <ACA_NAME> --resource-group <RG> -o table

# Activate an older revision (no rebuild)
az containerapp revision activate \
  --revision <previous-revision-name> \
  --resource-group <RG> \
  --name <ACA_NAME>
```

For more drastic rollbacks, re-deploy a specific image tag:

```bash
# Re-deploy an older SHA image
az containerapp update \
  --name <ACA_NAME> \
  --resource-group <RG> \
  --image <ACR_NAME>.azurecr.io/servantsync:<older-sha>
```

### Manual deploy (no code change needed)

The workflow also fires on `workflow_dispatch` (the "Run workflow" button in the Actions tab). Use this when you've edited `deploy/aca.servantsync.yaml` directly without touching any runtime file — the workflow's `paths:` filter would otherwise skip it.

Adding `deploy/**` to the push `paths:` filter is what makes a YAML-only change deploy automatically (`.github/workflows/deploy.yml:18` already includes that).

### Database access

The Azure SQL admin creds live as GitHub secrets + the YAML's `ConnectionStrings__DefaultConnection` env var on the live Container App.

```bash
# Get the FQDN
az sql server show --name <server-name> --resource-group <RG> \
  --query fullyQualifiedDomainName -o tsv

# Connect from a host the SQL firewall allows
sqlcmd -S <server>.database.windows.net -U <user> -P <password> -d <db-name>
```

For ad-hoc access from your dev box, add your IP to the SQL server's firewall:

```bash
az sql server firewall-rule create \
  --server <server-name> \
  --resource-group <RG> \
  --name AllowDevBox \
  --start-ip-address <your-ip> \
  --end-ip-address <your-ip>
```

### Backups

**Azure SQL Database has built-in automated backups.** No app-side backup service is needed — the old `SqliteBackupService` (file-level `VACUUM INTO` snapshots) is intentionally not registered in `Program.cs` for the Azure SQL path.

- Full + differential backups: Azure SQL handles automatically (7-day retention on Basic, up to 35 days on Standard+, configurable).
- Point-in-time restore: `az sql db restore --restore-point <datetime>`.
- Long-term retention: configure an LTR policy in Azure Portal (up to 10 years).
- Azure Files share (`/data/uploads/training`): use `az storage file download-batch` with an Azure Automation runbook, or a local cron job that does `cp` + `az storage blob upload`.

### Scaling

Edit `deploy/aca.servantsync.yaml`'s `scale:` block and push.

```yaml
scale:
  minReplicas: 0   # default; $0/mo when idle, ~3s cold start on first request
  maxReplicas: 1   # raise if you see throttling
```

Concurrent uploads + multiple SignalR circuits contend for the single replica. For ~30+ volunteers/day, raise `maxReplicas: 3` and add a CPU-threshold scale rule:

```yaml
scale:
  minReplicas: 0
  maxReplicas: 3
  rules:
    - name: cpu-rule
      custom:
        type: cpu
        metadata:
          type: Utilization
          value: "70"
```

### Custom domain + TLS

ACA has native custom-domain support with auto-issued managed certificates.

```bash
# 1. Add the domain
az containerapp hostname add \
  --hostname scheduling.yourchurch.org \
  --resource-group <RG> \
  --name <ACA_NAME>

# 2. Get the verification TXT record, add it to your DNS provider
az containerapp hostname list \
  --resource-group <RG> \
  --name <ACA_NAME> -o table

# 3. ACA provisions a managed TLS cert once DNS is verified
# (auto-issued + auto-renewed — no operator action needed)
```

---

## Cost expectations

For a single-church / low-traffic internal app:

| Resource | Cost |
|---|---|
| ACA Consumption (`minReplicas: 0`) | ~$0–2/mo (per-second compute) |
| Azure SQL Database Basic (5 DTU) | ~$5/mo |
| Azure Files share (pay only for used capacity, 100 GiB quota) | ~$0.02/GB/mo |
| Azure Container Registry Basic | ~$5/mo (or free tier in some regions) |
| SendGrid free (100 emails/day) or Brevo free (300/day) | $0 |

**Realistic total: $5–15/mo** with the default `minReplicas: 0`. Set `minReplicas: 1` to eliminate cold starts (~$2–5/mo more).

When to upgrade:

| Symptom | Fix |
|---|---|
| CPU >70% sustained on the single replica | `maxReplicas: 3` + CPU autoscale rule |
| SQL `DTU percentage` >70% on Basic | Upgrade to S0 ($10–15/mo, 10 DTU → 20 DTU → 50 DTU family) |
| Azure Files share >80% of quota | `az storage share update --quota <GiB>` or add file-upload retention pruning |
| Cold starts >3s after long idle | `minReplicas: 1` |
| Outbound bandwidth >5 GB/mo | SendGrid/Brevo offloads transactional email; switch heavy file downloads to direct blob URLs |

---

## Troubleshooting

### "Application Error" after deploy

```bash
az containerapp logs show --name <ACA_NAME> --resource-group <RG> --tail 200
```

Common causes (filter by `fail:` prefix):

- **Migrations failed.** Look for `Microsoft.EntityFrameworkCore.Database.Command` `fail:` lines. If Azure SQL firewall isn't open, the diagnostic is "A network-related or instance-specific error" — add your IP to the firewall (Day-2 ops > Database access above) or open `0.0.0.0` for the Container App's outbound range.
- **SMTP 5xx.** Look for `MailKit` errors. SendGrid wants `User=apikey` (literal), `TlsMode=StartTlsWhenAvailable`, port 587. Brevo wants the verified-sender email as `User`. Confirm the `smtp-password` Container App secret actually exists (`az containerapp secret list --name <ACA_NAME> --resource-group <RG>`).
- **Wrong env.** If the dev `LoggingEmailSender` is firing in production, `ASPNETCORE_ENVIRONMENT` is being set incorrectly somewhere outside the YAML — and the YAML's PATCH will silently drop the override on the next deploy. The Dockerfile sets `Production`; the YAML doesn't override.

### "Network-related or instance-specific error" on boot

Azure SQL server isn't reachable. Three layers to check:

1. **Firewall.** `az sql server firewall-rule list --server <name> --resource-group <RG>`. Azure services need `AllowAzureServices=true` (default) OR an explicit IP allowlist including ACA's outbound range.
2. **Connection string.** The YAML's `ConnectionStrings__DefaultConnection` should look like `Server=tcp:servantsyncdb.database.windows.net,1433;Initial Catalog=ServantSync;User ID=<user>;Password=<password>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;`.
3. **User.** `az sql db show --name <db> --server <server> --resource-group <RG>` to confirm the DB exists. `az sql server show --name <server> --resource-group <RG>` to confirm the server's active directory admin is set.

### Cold starts feel slow

ACA Consumption with `minReplicas: 0` pays ~3–15s on first request after ~5 min idle. Two mitigations: (a) `minReplicas: 1` in `deploy/aca.servantsync.yaml` — trades idle cost for instant-warm; (b) external uptime monitor (UptimeRobot free tier) probing `/Account/Login` every 5 min — keeps the single replica warm enough that the cold-start window stays small.

### Storage is full on the Azure Files share

```bash
# See what files are eating space
az storage file list \
  --share-name <share-name> \
  --account-name <storage-account-name> \
  --path uploads \
  -o table

# Raise the quota (you only pay for used, so the free headroom is fine)
az storage share update \
  --name <share-name> \
  --account-name <storage-account-name> \
  --quota 200
```

### I edited `deploy/aca.servantsync.yaml` but nothing deployed

The workflow's `paths:` filter only triggers on runtime files. Pushing YAML-only won't deploy unless `deploy/**` is in the filter (it IS — check `.github/workflows/deploy.yml:18`). If it's not deploying, run `workflow_dispatch` from the Actions tab manually.

### The SMTP password isn't taking effect

The YAML wires `Email__Smtp__Password` → `secretRef: smtp-password`. If you update the secret via `az containerapp secret set`, the running container still has the old env until you restart or re-deploy. `az containerapp revision restart --name <ACA_NAME> --resource-group <RG>` forces a rolling restart of the active revision.

---

## Reference: who owns what

| Layer | Owner | Lives in |
|---|---|---|
| Connection-string default | `appsettings.json` | Committed (placeholder; SQLite so missing-override fails loudly) |
| Connection-string dev | `appsettings.Development.json` | Committed (local SQL Server, `Trusted_Connection=true`) |
| Connection-string prod | `deploy/aca.servantsync.yaml` env list | Committed (Azure SQL with `Encrypt=True`) |
| SMTP password | ACA secret store (`smtp-password`) | Created by `SETUP.md` Step 5; never in YAML, never in git |
| SMTP non-secret fields | `deploy/aca.servantsync.yaml` env list | Committed |
| `ASPNETCORE_ENVIRONMENT` | `Dockerfile` ENV + `launchSettings.json` (opposite defaults per env) | Committed |
| Routing + scale + volume mount | `deploy/aca.servantsync.yaml` | Committed; PATCHed onto the Container App on every push |
| RBAC for the workflow | Entra ID App Registration + federated credential | Created by `SETUP.md` Steps 6–7 |
| GitHub-side secrets for the workflow | `<repo>/settings/secrets/actions` | Created by `SETUP.md` Step 8 |

---

## Related docs

- [`SETUP.md`](SETUP.md) — one-time provisioning runbook. Read first.
- [`appsettings.Production.sample.json`](appsettings.Production.sample.json) — production config template (placeholders).
- [`deploy/aca.servantsync.yaml`](deploy/aca.servantsync.yaml) — the YAML the workflow PATCHes from.
- [`.github/workflows/deploy.yml`](.github/workflows/deploy.yml) — the CI/CD pipeline definition.
- [`Dockerfile`](Dockerfile) — multi-stage ASP.NET 9 image build.
- [`scripts/aca-steps-1-4.ps1`](scripts/aca-steps-1-4.ps1) / [`scripts/aca-steps-5-8.ps1`](scripts/aca-steps-5-8.ps1) — PowerShell helpers that automate the SETUP.md `az` commands.

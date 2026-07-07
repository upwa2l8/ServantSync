# One-time setup for the Container Apps deploy

This guide is the bridge between "the workflow file is committed" and
"the first push to `main` actually deploys". The Container Apps
architecture is **stateless by default**, but ServantSync keeps a
SQLite database, file uploads, and periodic backups on disk. We
preserve them by mounting an **Azure Files share** at `/data` inside
the container. The Dockerfile already wires up the symlink from
`/app/wwwroot/uploads/training` to `/data/uploads` and the env vars
to point at `/data/servantsync.db` and `/data/backups` -- so all
that's left is to provision the share, the registry, the app, and the
OIDC identity in Azure.

The one-time setup is roughly **8 manual steps** with `az` commands.
After that, every push to `main` on a code path in
`.github/workflows/deploy.yml`'s `paths:` filter will trigger a fresh
deploy.

> **Source of truth split.** This guide is for the **one-time
> provisioning** (storage, registry, ACA env, ACA app, SMTP secret,
> OIDC identity, GitHub secrets). The **per-deploy config** (env vars,
> `/data` volume mount, ingress port, scale) lives in
> `deploy/aca.servantsync.yaml` and is applied by the deploy workflow
> on every push via `azure/container-apps-deploy-action@v2`'s PATCH.
> The steps below no longer set those per-deploy fields; if you need
> to change them, edit the YAML.

> **PowerShell one-shots.** If you're on Windows, two scripts
> automate the `az` CLI parts: `scripts/aca-steps-1-4.ps1` runs Steps
> 1-4 (RG, storage, ACR, ACA env + placeholder app), and
> `scripts/aca-steps-5-8.ps1` runs Steps 5-7 (SMTP secret, App
> Registration, RBAC) plus an optional `gh` CLI path for Step 8
> (GitHub secrets). Step 8 has a manual-UI fallback if `gh` isn't
> available.

> **TL;DR for the impatient:** provision the storage account + share,
> the ACR, the ACA Environment, the Container App (placeholder only --
> no env vars, no volume), the App Registration with OIDC federated
> credential, and add the GitHub secrets. Then push. ~30 min the
> first time.

---

## Variables (used in every step)

```bash
RG="servantsync-rg"                  # resource group, already exists from earlier attempts
LOC="eastus"                         # or eastus2 (where your existing 'ServantSync' RG lives)
ACR_NAME="servantsync$(openssl rand -hex 4)"   # globally unique, lowercase
STG_NAME="servantsyncstg$(openssl rand -hex 4)" # storage account name, lowercase alphanumeric
SHARE_NAME="servantsync-data"        # Azure Files share name
ACA_ENV="servantsync-env"            # Container Apps Environment name
ACA_APP="servantsync"                # Container App name
SUB_ID="$(az account show --query id -o tsv)"
```

---

## Step 1 — Make sure the resource group exists

You have several from earlier attempts (`DefaultResourceGroup-EUS2`,
`ServantSync` in eastus2, `cloud-shell-storage-eastus`, `servantsync-rg`).
The simplest path is to **reuse the `ServantSync` RG in eastus2** --
it has the cleanest state.

```bash
# If you want to start fresh in eastus with the correct region:
az group create --name "$RG" --location "$LOC"
```

## Step 2 — Storage Account + Azure Files share

This is the **persistent** backing for the SQLite database, file
uploads, and SQLite backups. The share is mounted at `/data` inside
the container.

```bash
# Storage account (Standard_LRS is fine for a single-church app)
az storage account create \
  --name "$STG_NAME" \
  --resource-group "$RG" \
  --location "$LOC" \
  --sku Standard_LRS \
  --kind StorageV2

# File share -- 100 GiB quota, but only pay for used capacity.
# Adjust the quota based on your retention policy.
az storage share create \
  --name "$SHARE_NAME" \
  --account-name "$STG_NAME" \
  --quota 100

# Capture the storage account key (needed to mount the share in ACA)
STG_KEY="$(az storage account keys list \
  --account-name "$STG_NAME" \
  --resource-group "$RG" \
  --query "[0].value" -o tsv)"

echo "$STG_KEY"   # you will need this in Step 4
```

## Step 3 — Azure Container Registry

The workflow builds the Docker image in CI and pushes it here. ACA
then pulls from the registry on every deploy.

```bash
# ACR -- Basic SKU is the cheapest paid tier; you can also use
# the free tier if your region supports it (some regions don't).
# Admin user is NOT required -- ACA uses the AAD token from OIDC.
az acr create \
  --name "$ACR_NAME" \
  --resource-group "$RG" \
  --location "$LOC" \
  --sku Basic \
  --admin-enabled false
```

## Step 4 — Container Apps Environment + Container App (with the volume mounted)

The Environment is a regional "wrapper" that holds the storage mount
+ ACA-internal VNet. The Container App is the actual running service.

```bash
# Create the Container Apps Environment
az containerapp env create \
  --name "$ACA_ENV" \
  --resource-group "$RG" \
  --location "$LOC"

# Add the Azure Files share to the Environment's storage config
az containerapp env storage set \
  --name "$ACA_ENV" \
  --resource-group "$RG" \
  --storage-name servantsync-data \
  --azure-file-account-name "$STG_NAME" \
  --azure-file-account-key "$STG_KEY" \
  --azure-file-share-name "$SHARE_NAME" \
  --access-mode ReadWrite

# Create the Container App as a PLACEHOLDER. The deploy workflow's
# PATCH (via deploy/aca.servantsync.yaml) will replace the image,
# flip the port to 8080, set all env vars, and attach the /data
# Azure Files volume on the first push -- so this create only needs
# a valid image that listens on the create-time port.
#
# Using nginx:alpine on port 80 because (a) it works immediately so
# you can hit the FQDN and confirm ACA ingress is reachable, and
# (b) ACA won't try to pull anything that the first PATCH will
# replace anyway. Once the workflow runs, this all gets swapped
# for the real ServantSync image + the YAML's env vars + the volume
# mount. Do NOT add env vars, volume flags, or scale flags here --
# the YAML is the source of truth and any inline values would be
# silently wiped on the first PATCH.
az containerapp create \
  --name "$ACA_APP" \
  --resource-group "$RG" \
  --environment "$ACA_ENV" \
  --image nginx:alpine \
  --target-port 80 \
  --ingress external \
  --cpu 0.5 \
  --memory 1.0Gi \
  --min-replicas 0 \
  --max-replicas 3
  # NOTE: --env-vars, --volume-name/--volume-type/--volume-mounts,
  # and --min-replicas/--max-replicas are intentionally absent.
  # They're all set by deploy/aca.servantsync.yaml on first deploy
  # via the workflow's PATCH. Adding them here would be wasted typing.
```

## Step 5 — Add the SMTP password as a Container App secret

ACA's secret store encrypts the value. The YAML references it by name
via `secretRef: smtp-password` and the deploy workflow's PATCH
wires the env var for you -- so this step only needs to create the
secret itself, not wire the env var.

```bash
az containerapp secret set \
  --name "$ACA_APP" \
  --resource-group "$RG" \
  --secrets "smtp-password=<your-real-sendgrid-api-key>"
```

> The first deploy's PATCH will resolve `secretRef: smtp-password`
> in `deploy/aca.servantsync.yaml` against this secret. If the
> secret is missing or the name doesn't match, the PATCH succeeds
> but the container fails to start with a "secret smtp-password
> not found" log line.

## Step 6 — App Registration + OIDC federated credential

The GitHub Actions workflow authenticates to Azure via a short-lived
OIDC token instead of a long-lived client secret. The federated
credential subject must match the workflow's identity.

```bash
# Create the App Registration
APP_ID="$(az ad app create \
  --display-name "ServantSync GitHub Actions" \
  --query appId -o tsv)"

# Create a service principal for the App Registration
SP_ID="$(az ad sp create --id "$APP_ID" --query id -o tsv)"

echo "APP_ID=$APP_ID"   # this is AZURE_CLIENT_ID below
echo "SP_ID=$SP_ID"     # needed for role assignments below

# Federated credential for push-to-main + workflow_dispatch
az ad app federated-credential create \
  --id "$APP_ID" \
  --parameters "{\"name\":\"github-main\",\"issuer\":\"https://token.actions.githubusercontent.com\",\"subject\":\"repo:YOUR_GITHUB_OWNER/ServantSync:ref:refs/heads/main\",\"audiences\":[\"api://AzureADTokenExchange\"]}"

# Optional: a second credential gated on a 'Production' environment
# (requires the GitHub repo to have a 'Production' environment set
# up; the workflow_dispatch trigger can then require approval before
# the deploy can run).
# az ad app federated-credential create \
#   --id "$APP_ID" \
#   --parameters "{\"name\":\"github-prod-env\",\"issuer\":\"https://token.actions.githubusercontent.com\",\"subject\":\"repo:YOUR_GITHUB_OWNER/ServantSync:environment:Production\",\"audiences\":[\"api://AzureADTokenExchange\"]}"
```

## Step 7 — RBAC role assignments for the App Registration's service principal

Three roles, each scoped to a different resource:

| Role | Scope | Why |
|---|---|---|
| `AcrPush` | the ACR | Push images from the workflow |
| `Container Apps Contributor` (or plain `Contributor`) | the RG | Update the Container App, manage revisions, read env-var wiring |
| `Storage File Data SMB Share Contributor` | the Azure Files share | The container's seed-time writes (migrations, first seed) need RW to the share. **Optional** -- if you pre-create the SQLite file via a separate path, skip this. |

```bash
# AcrPush on the registry
az role assignment create \
  --assignee "$SP_ID" \
  --role "AcrPush" \
  --scope "/subscriptions/$SUB_ID/resourceGroups/$RG/providers/Microsoft.ContainerRegistry/registries/$ACR_NAME"

# Container Apps Contributor on the RG (covers .update and .read on ACA)
az role assignment create \
  --assignee "$SP_ID" \
  --role "Container Apps Contributor" \
  --scope "/subscriptions/$SUB_ID/resourceGroups/$RG"

# Optional: Storage File Data SMB Share Contributor on the share
# SHARE_ID="$(az storage share show --name "$SHARE_NAME" --account-name "$STG_NAME" --query id -o tsv)"
# az role assignment create \
#   --assignee "$SP_ID" \
#   --role "Storage File Data SMB Share Contributor" \
#   --scope "$SHARE_ID"
```

## Step 8 — GitHub repository secrets

In GitHub: `https://github.com/<owner>/ServantSync/settings/secrets/actions`
→ "New repository secret" for each of these:

| Secret | Value | Notes |
|---|---|---|
| `AZURE_CLIENT_ID` | `$APP_ID` from Step 6 | The App Registration's application (client) ID |
| `AZURE_TENANT_ID` | the Entra ID tenant ID | `az account show --query tenantId -o tsv` |
| `AZURE_SUBSCRIPTION_ID` | `$SUB_ID` | The Azure subscription |
| `ACR_NAME` | `$ACR_NAME` | The registry name (NOT the FQDN) |
| `ACA_NAME` | `$ACA_APP` | The Container App name |
| `AZURE_RG` | `$RG` | The resource group name |
| `SMOKE_CHECK_URL` | (optional) `https://<ACA_FQDN>/Account/Login` | If set, the workflow curls this after deploy to catch 500s early |

You can find the ACA's FQDN with `az containerapp show --name $ACA_APP --resource-group $RG --query "properties.configuration.ingress.fqdn" -o tsv` -- that's the URL the live app will be reachable at.

---

## What happens on the first push

When the workflow runs:

1. `azure/login@v2` requests an OIDC token from GitHub. Azure
   validates the token against the App Registration's federated
   credential (subject match).
2. `az acr login` uses the AAD token to authenticate to the registry.
3. `docker buildx` builds the image from the Dockerfile, layers are
   cached against GitHub's GHA cache.
4. `docker push` uploads the image to ACR with tags
   `servantsync:${{ github.sha }}` and `servantsync:latest`.
5. `azure/container-apps-deploy-action@v2` runs `az containerapp
   update` under the hood with the YAML as the source of truth,
   which:
   - Pulls the new image from ACR (overrides the YAML's bogus
     `PLACEHOLDER-OVERRIDDEN-BY-IMAGE-TO-DEPLOY-DO-NOT-RUN` image
     string -- if the override ever silently fails, the bogus
     string makes it immediately obvious in ACA's "Running" view)
   - PATCHes the entire container template from
     `deploy/aca.servantsync.yaml`: flips target-port from 80 to
     8080, attaches the `/data` Azure Files volume, sets all 16
     non-secret env vars, and resolves `secretRef: smtp-password`
     against the secret you created in Step 5
   - Creates a new ACA revision
   - Shifts 100% of traffic to the new revision (zero-downtime)
   - Deactivates the old revision (kept for rollback)
6. ACA starts the new container. The startup does
   `db.Database.MigrateAsync()` + seed, which creates
   `/data/servantsync.db` on the Azure Files share if it doesn't
   exist. The `SqliteBackupService` BackgroundService starts a
   `PeriodicTimer` for the backup cycle.

## What to verify after the first push

```bash
# Watch the deploy log
az containerapp logs show \
  --name "$ACA_APP" \
  --resource-group "$RG" \
  --follow

# In another terminal, watch the new revision come up healthy
az containerapp revision list \
  --name "$ACA_APP" \
  --resource-group "$RG" \
  --output table

# The live URL
az containerapp show \
  --name "$ACA_APP" \
  --resource-group "$RG" \
  --query "properties.configuration.ingress.fqdn" -o tsv
```

Open the FQDN, log in as `admin@demo.local` / `ServantSync2025!` (the
seed credentials from `DatabaseSeeder`), and verify:
- `/Dashboard`, `/Organizations`, `/Training` all render without 500
- `/Training/Manage` → upload a small training PDF, verify the file
  lands in the Azure Files share (`az storage file list --share-name $SHARE_NAME --account-name $STG_NAME --path "uploads"`)

## Rollback

If a deploy breaks something, ACA keeps the prior revision available.
Roll back without rebuilding:

```bash
az containerapp revision activate \
  --revision "<previous-revision-name>" \
  --resource-group "$RG" \
  --name "$ACA_APP"
```

You can list revisions with `az containerapp revision list --name $ACA_APP --resource-group $RG -o table`.

## Cost expectations

Container Apps Consumption plan charges per request + per
vCPU-second + per GiB-second. For a single-church / low-traffic app,
expect **$0-5/month** even with always-on (set `--min-replicas 1` if
you want zero cold starts; the default `min-replicas 0` saves money
but the first request after ~5 min idle pays a cold-start penalty).
Storage is the larger ongoing cost: Standard_LRS at ~$0.018/GB/month
for the Azure Files share, so 5 GB of uploads + backups is ~$0.09/month.

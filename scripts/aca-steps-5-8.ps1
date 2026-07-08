# Steps 5-8 of SETUP.md: SMTP secret, App Registration + OIDC
# federated credential, RBAC role assignments, GitHub secrets.
# Run AFTER scripts/aca-steps-1-4.ps1 has provisioned the RG, storage
# share, ACR, ACA Env, and the placeholder Container App.
#
# Hardcodes RG / LOC / ACR_NAME / STG_NAME / SHARE_NAME / ACA_ENV /
# ACA_APP to match aca-steps-1-4.ps1. The SMTP password is the ONE
# thing the script must prompt for (or read from an env var) because
# it can't be derived from existing Azure state.
#
# Step 6 (OIDC federated credential) derives the GitHub repo
# identifier from `git remote -v` so the subject claim is correct
# without manual editing. Run this script from the repo root.

$ErrorActionPreference = 'Continue'

# Function-based az wrapper so 'az' resolves in any PowerShell host.
function global:az {
    & 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd' @args
}

# --- Variables (must match aca-steps-1-4.ps1) ---
$RG         = 'ServantSync'
$LOC        = 'eastus2'
$SHARE_NAME = 'servantsync-data'
$ACA_ENV    = 'servantsync-env'
$ACA_APP    = 'servantsync'
$ACR_NAME   = 'servantsyncacr'
$STG_NAME   = 'servantsyncstg'

$SUB_ID = az account show --query id -o tsv
$TENANT_ID = az account show --query tenantId -o tsv
$FQDN = az containerapp show `
    --name $ACA_APP `
    --resource-group $RG `
    --query "properties.configuration.ingress.fqdn" `
    -o tsv

Write-Host "============================================="
Write-Host " ACA Steps 5-8 (identity + secrets)"
Write-Host "============================================="
Write-Host "  RG         = $RG"
Write-Host "  ACA_APP    = $ACA_APP"
Write-Host "  ACR_NAME   = $ACR_NAME"
Write-Host "  FQDN       = $FQDN"
Write-Host "  SUB_ID     = $SUB_ID"
Write-Host "  TENANT_ID  = $TENANT_ID"
Write-Host ""

# --- Step 5: SMTP password as a Container App secret ---
Write-Host "=== Step 5: SMTP password as a Container App secret ==="
Write-Host "  The YAML's env list references this secret by name"
Write-Host "  (secretRef: smtp-password). On the first deploy's PATCH,"
Write-Host "  ACA wires Email__Smtp__Password -> secretref:smtp-password"
Write-Host "  automatically. If the secret is missing or the name doesn't"
Write-Host "  match, the container fails to start with a 'secret not found'"
Write-Host "  log line. Get the password from your SMTP provider (SendGrid"
Write-Host "  Settings -> API Keys, or Brevo SMTP & API -> SMTP keys)."
Write-Host ""
# Prefer the SMTP_PASSWORD env var (so this script is CI-friendly);
# fall back to a SecureString prompt.
if (-not $env:SMTP_PASSWORD) {
    $securePwd = Read-Host "  SMTP password (or set SMTP_PASSWORD env var and re-run)" -AsSecureString
    $SMTP_PASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePwd))
} else {
    $SMTP_PASSWORD = $env:SMTP_PASSWORD
    Write-Host "  Using SMTP_PASSWORD from environment."
}
Write-Host ""
az containerapp secret set `
    --name $ACA_APP `
    --resource-group $RG `
    --secrets "smtp-password=$SMTP_PASSWORD" `
    --output table
Write-Host ("  exit code: " + $LASTEXITCODE)
Write-Host ""
# Clear the local variable so the password doesn't linger in the
# script's process memory longer than necessary. NOTE: this is
# best-effort only -- (a) the plain string returned by
# PtrToStringAuto is in the managed heap and can survive until the
# next GC, and (b) the password was interpolated into the
# `--secrets` arg passed to the spawned `az.cmd`, so it's in that
# process's argv (visible to `Get-Process az` and any process
# inspector until az exits). Acceptable for a one-time setup
# script against your own resources; not a defense against a
# compromised host.
$SMTP_PASSWORD = $null
[System.GC]::Collect()

# --- Step 6: App Registration + OIDC federated credential ---
Write-Host "=== Step 6: App Registration + OIDC federated credential ==="

# Detect the GitHub repo from `git remote -v` so the federated
# credential's subject claim is correct without manual editing.
# Run from the repo root. Format expected: owner/repo.
$REPO_DIR = (git rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ERROR: not inside a git repo. Run this script from the"
    Write-Host "  ServantSync repo root so the federated credential subject"
    Write-Host "  can be derived from the remote URL."
    exit 1
}
$REPO_ROOT = (Get-Location).Path
Write-Host "  Detected repo root: $REPO_ROOT"
$REMOTE_URL = git config --get remote.origin.url
Write-Host "  Remote URL: $REMOTE_URL"
# Normalize: handle both git@github.com:owner/repo.git and https://github.com/owner/repo.git
if ($REMOTE_URL -match 'github\.com[:/]([^/]+)/(.+?)(\.git)?$') {
    $GITHUB_OWNER = $Matches[1]
    $GITHUB_REPO  = $Matches[2]
} else {
    Write-Host "  ERROR: remote URL doesn't look like a GitHub repo. Expected"
    Write-Host "  'github.com[:/]owner/repo(.git)?' but got: $REMOTE_URL"
    exit 1
}
Write-Host "  Detected GitHub repo: $GITHUB_OWNER/$GITHUB_REPO"
Write-Host ""

# Create the App Registration.
$APP_NAME = 'ServantSync GitHub Actions'
$existingApp = az ad app list --display-name $APP_NAME --query "[0].appId" -o tsv
if ($existingApp) {
    Write-Host "  App Registration '$APP_NAME' already exists; reusing appId $existingApp"
    $APP_ID = $existingApp
} else {
    Write-Host "  Creating App Registration '$APP_NAME'..."
    $APP_ID = az ad app create --display-name $APP_NAME --query appId -o tsv
    Write-Host ("  exit code: " + $LASTEXITCODE)
}
Write-Host "  APP_ID = $APP_ID"

# Service principal for the App Registration (needed for RBAC in Step 7).
Write-Host ""
$SP_ID = az ad sp create --id $APP_ID --query id -o tsv
Write-Host "  SP_ID  = $SP_ID"
Write-Host ""

# Federated credential for push-to-main.
$FED_NAME = 'github-main'
$existingFed = az ad app federated-credential list --id $APP_ID --query "[?name=='$FED_NAME'].name" -o tsv
if ($existingFed) {
    Write-Host "  Federated credential '$FED_NAME' already exists; skipping create."
} else {
    $SUBJECT = "repo:$GITHUB_OWNER/${GITHUB_REPO}:ref:refs/heads/main"
    $PARAMS_JSON = @{
        name      = $FED_NAME
        issuer    = 'https://token.actions.githubusercontent.com'
        subject   = $SUBJECT
        audiences = @('api://AzureADTokenExchange')
    } | ConvertTo-Json -Compress
    # PowerShell's `&` + cmd.exe re-parsing mangles the internal `"`
    # characters when JSON is passed inline (az returns "Failed to
    # parse string as JSON"). Write to a temp file + use the @file
    # syntax so az reads the file as the parameter value. The
    # `UTF8Encoding($false)` constructor writes WITHOUT a BOM
    # (default `Set-Content -Encoding utf8` adds one, which can
    # confuse some JSON parsers).
    $paramsFile = Join-Path $env:TEMP ("fedcred-params-" + $FED_NAME + ".json")
    [System.IO.File]::WriteAllText($paramsFile, $PARAMS_JSON, [System.Text.UTF8Encoding]::new($false))
    Write-Host ("  Creating federated credential with subject: " + $SUBJECT)
    az ad app federated-credential create --id $APP_ID --parameters ("@" + $paramsFile) --output table
    Write-Host ("  exit code: " + $LASTEXITCODE)
    Remove-Item $paramsFile -Force -ErrorAction SilentlyContinue
}

# Optional: Production-environment-gated credential for workflow_dispatch.
# Uncomment the block below if you've set up a 'Production' protected
# environment in the GitHub repo AND want workflow_dispatch to require
# approval before deploying.
# Write-Host ""
# $FED_PROD = 'github-prod-env'
# $existingFedProd = az ad app federated-credential list --id $APP_ID --query "[?name=='$FED_PROD'].name" -o tsv
# if ($existingFedProd) {
#     Write-Host "  Federated credential '$FED_PROD' already exists; skipping create."
# } else {
#     $SUBJECT_PROD = "repo:$GITHUB_OWNER/${GITHUB_REPO}:environment:Production"
#     $PARAMS_PROD = @{
#         name      = $FED_PROD
#         issuer    = 'https://token.actions.githubusercontent.com'
#         subject   = $SUBJECT_PROD
#         audiences = @('api://AzureADTokenExchange')
#     } | ConvertTo-Json -Compress
#     Write-Host "  Creating Production-environment federated credential with subject: $SUBJECT_PROD"
#     az ad app federated-credential create --id $APP_ID --parameters $PARAMS_PROD --output table
#     Write-Host ("  exit code: " + $LASTEXITCODE)
# }
Write-Host ""

# --- Step 7: RBAC role assignments ---
Write-Host "=== Step 7: RBAC role assignments ==="

# AcrPush on the registry.
$ACR_SCOPE = "/subscriptions/$SUB_ID/resourceGroups/$RG/providers/Microsoft.ContainerRegistry/registries/$ACR_NAME"
$existing = az role assignment list --assignee $SP_ID --role AcrPush --scope $ACR_SCOPE --query "[0].id" -o tsv
if ($existing) {
    Write-Host "  AcrPush on $ACR_NAME already assigned; skipping."
} else {
    az role assignment create --assignee $SP_ID --role AcrPush --scope $ACR_SCOPE --output table
    Write-Host ("  exit code: " + $LASTEXITCODE)
}
Write-Host ""

# Container Apps Contributor on the RG.
$RG_SCOPE = "/subscriptions/$SUB_ID/resourceGroups/$RG"
$existing = az role assignment list --assignee $SP_ID --role 'Container Apps Contributor' --scope $RG_SCOPE --query "[0].id" -o tsv
if ($existing) {
    Write-Host "  Container Apps Contributor on $RG already assigned; skipping."
} else {
    az role assignment create --assignee $SP_ID --role 'Container Apps Contributor' --scope $RG_SCOPE --output table
    Write-Host ("  exit code: " + $LASTEXITCODE)
}
Write-Host ""

# Optional: Storage File Data SMB Share Contributor on the share.
# Required for the container to write to /data at startup (migrations,
# first seed, backup). RBAC on storage is data-plane and doesn't
# propagate instantly -- it can take 5+ minutes after the assignment
# for the storage SDK to recognize the new role. If you skip this and
# the container fails to start with 403 on /data/servantsync.db, add
# it and re-deploy.
Write-Host "  (Optional) Storage File Data SMB Share Contributor on the share:"
Write-Host "  SHARE_ID=`$(az storage share show --name $SHARE_NAME --account-name $STG_NAME --query id -o tsv)"
Write-Host "  az role assignment create --assignee $SP_ID ``"
Write-Host "      --role 'Storage File Data SMB Share Contributor' ``"
Write-Host "      --scope `$SHARE_ID"
Write-Host ""

# --- Step 8: GitHub secrets ---
Write-Host "=== Step 8: GitHub repository secrets ==="
Write-Host "  The workflow needs 6 secrets (7 if you also set SMOKE_CHECK_URL):"
Write-Host "    AZURE_CLIENT_ID         = $APP_ID"
Write-Host "    AZURE_TENANT_ID         = $TENANT_ID"
Write-Host "    AZURE_SUBSCRIPTION_ID   = $SUB_ID"
Write-Host "    AZURE_RG                = $RG"
Write-Host "    ACR_NAME                = $ACR_NAME"
Write-Host "    ACA_NAME                = $ACA_APP"
Write-Host "    (optional) SMOKE_CHECK_URL = https://$FQDN/Account/Login"
Write-Host ""

# Prefer the `gh` CLI for a one-shot set. Check it's installed and
# authenticated; fall back to manual UI instructions if not.
$ghAvailable = $null
$ghAuthed = $null
try { $ghAvailable = (gh --version 2>$null) -ne $null } catch { $ghAvailable = $false }
if ($ghAvailable) {
    try { $ghAuthed = (gh auth status 2>&1 | Select-String -Pattern 'Logged in to') -ne $null } catch { $ghAuthed = $false }
}

if ($ghAvailable -and $ghAuthed) {
    Write-Host "  Detected 'gh' CLI is installed and authenticated. Setting"
    Write-Host "  the 6 secrets now (one 'gh secret set' per secret)."
    Write-Host ""

    # All 6 secrets are non-sensitive in the value (the SMTP password
    # is on the Container App, not in GitHub). The 7th (SMOKE_CHECK_URL)
    # is optional; only set it if we have a live FQDN to point at.
    $secretsToSet = @(
        @{ Name = 'AZURE_CLIENT_ID';       Value = $APP_ID },
        @{ Name = 'AZURE_TENANT_ID';       Value = $TENANT_ID },
        @{ Name = 'AZURE_SUBSCRIPTION_ID'; Value = $SUB_ID },
        @{ Name = 'AZURE_RG';              Value = $RG },
        @{ Name = 'ACR_NAME';              Value = $ACR_NAME },
        @{ Name = 'ACA_NAME';              Value = $ACA_APP }
    )
    if ($FQDN) {
        $secretsToSet += @{ Name = 'SMOKE_CHECK_URL'; Value = "https://$FQDN/Account/Login" }
    } else {
        Write-Warning "  FQDN is empty (was the first script's Step 4c successful?)."
        Write-Warning "  Skipping SMOKE_CHECK_URL; set it manually later via 'gh secret set' or the GitHub UI."
    }

    $setFailures = @()
    foreach ($s in $secretsToSet) {
        gh secret set $s.Name --body $s.Value 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            $setFailures += $s.Name
            Write-Warning "  Failed to set secret '$($s.Name)' (exit $LASTEXITCODE)"
        }
    }

    if ($setFailures.Count -eq 0) {
        Write-Host "  All $($secretsToSet.Count) secrets set successfully."
    } else {
        Write-Warning "  $($setFailures.Count) secret(s) failed: $($setFailures -join ', ')"
        Write-Warning "  Re-run after fixing the issue, or set them manually via the GitHub UI."
    }
    Write-Host "  Verify with: gh secret list"
} else {
    Write-Host "  'gh' CLI is not installed or not authenticated. Set the"
    Write-Host "  secrets manually:"
    Write-Host ""
    Write-Host "  1. Open https://github.com/$GITHUB_OWNER/$GITHUB_REPO/settings/secrets/actions"
    Write-Host "  2. Click 'New repository secret' for each of the 6 below:"
    Write-Host ""
    Write-Host "       Name                       Value"
    Write-Host "       ----                       -----"
    Write-Host "       AZURE_CLIENT_ID            $APP_ID"
    Write-Host "       AZURE_TENANT_ID            $TENANT_ID"
    Write-Host "       AZURE_SUBSCRIPTION_ID      $SUB_ID"
    Write-Host "       AZURE_RG                   $RG"
    Write-Host "       ACR_NAME                   $ACR_NAME"
    Write-Host "       ACA_NAME                   $ACA_APP"
    Write-Host "       SMOKE_CHECK_URL (optional) https://$FQDN/Account/Login"
    Write-Host ""
    if (-not $ghAvailable) {
        Write-Host "  (Tip: install the 'gh' CLI from https://cli.github.com/ and"
        Write-Host "   run 'gh auth login' to make future secret sets one-line.)"
    } elseif (-not $ghAuthed) {
        Write-Host "  (Tip: 'gh' is installed but not authenticated. Run"
        Write-Host "   'gh auth login' to make future secret sets one-line.)"
    }
}
Write-Host ""

# --- Final: dump all values for reference ---
Write-Host "============================================="
Write-Host " Summary"
Write-Host "============================================="
Write-Host "  SMTP secret 'smtp-password' on ${ACA_APP}: created"
Write-Host "  App Registration '$APP_NAME': $APP_ID"
Write-Host "  OIDC federated credential subject: repo:$GITHUB_OWNER/${GITHUB_REPO}:ref:refs/heads/main"
Write-Host "  RBAC: AcrPush on $ACR_NAME, Container Apps Contributor on $RG"
Write-Host "  GitHub secrets: see above (gh or manual)"
Write-Host ""
Write-Host "=== Next: push to main and watch the Actions tab ==="
Write-Host "  The first push will:"
Write-Host "    1. Build the ServantSync image (multi-stage .NET 9)"
Write-Host "    2. Push to $ACR_NAME.azurecr.io/servantsync:<commit-sha>"
Write-Host "    3. PATCH the Container App from deploy/aca.servantsync.yaml:"
Write-Host "       - replace nginx:alpine placeholder with the real image"
Write-Host "       - flip port from 80 to 8080"
Write-Host "       - attach the /data Azure Files volume"
Write-Host "       - set 16 non-secret env vars + secretRef for smtp-password"
Write-Host "    4. Start the new revision, route 100% traffic to it"
Write-Host ""
Write-Host "  Verify with:"
Write-Host "    az containerapp logs show --name $ACA_APP --resource-group $RG --follow"
Write-Host "    Open https://$FQDN/ in a browser."
Write-Host ""
Write-Host "=== Done. ==="

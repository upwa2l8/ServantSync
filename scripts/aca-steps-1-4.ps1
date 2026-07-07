# Steps 1-4 of SETUP.md: RG, Storage + share, ACR, ACA Env + App.
# Reuses the existing 'ServantSync' RG in eastus2 (cleanest state from
# earlier diagnostic work). If you want a fresh RG, change $RG and
# $LOC below.
#
# Hardcoded names (not Get-Random) so the script is fully re-runnable
# and the user knows exactly which resources to clean up if needed.
#
# Creates the Container App as a PLACEHOLDER ONLY:
#   - image: nginx:alpine (works on port 80, lets you curl the FQDN
#     and confirm ACA ingress is reachable before the first deploy)
#   - target-port: 80 (matches the placeholder image)
#   - no env vars, no volume, no scale flags
# The deploy workflow's PATCH (via deploy/aca.servantsync.yaml)
# replaces the image, flips the port to 8080, sets all 16 non-secret
# env vars, attaches the /data Azure Files volume, and applies
# scale: { minReplicas: 0, maxReplicas: 3 } on the first push.
# DO NOT add --env-vars / --volume-name / --volume-type /
# --volume-mounts / --min-replicas / --max-replicas to Step 4c --
# the YAML is the source of truth and any inline values would be
# silently wiped on the first PATCH.
#
# Run scripts/aca-steps-5-8.ps1 next to set up the SMTP secret, App
# Registration, OIDC federated credential, RBAC, and GitHub secrets.

$ErrorActionPreference = 'Continue'

# Function-based az wrapper so 'az' resolves in any PowerShell host.
function global:az {
    & 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd' @args
}

# --- Variables ---
$RG         = 'ServantSync'
$LOC        = 'eastus2'
$SHARE_NAME = 'servantsync-data'
$ACA_ENV    = 'servantsync-env'
$ACA_APP    = 'servantsync'
$ACR_NAME   = 'servantsyncacr'      # hardcoded for re-runnability
$STG_NAME   = 'servantsyncstg'      # hardcoded for re-runnability

$SUB_ID = az account show --query id -o tsv

Write-Host "============================================="
Write-Host " ACA Steps 1-4 (pre-deploy infrastructure)"
Write-Host "============================================="
Write-Host "  RG         = $RG"
Write-Host "  LOC        = $LOC"
Write-Host "  ACR_NAME   = $ACR_NAME  (globally unique)"
Write-Host "  STG_NAME   = $STG_NAME  (globally unique)"
Write-Host "  SHARE_NAME = $SHARE_NAME"
Write-Host "  ACA_ENV    = $ACA_ENV"
Write-Host "  ACA_APP    = $ACA_APP"
Write-Host "  SUB_ID     = $SUB_ID"
Write-Host ""

# --- Step 0: register resource providers (idempotent, --wait blocks until done) ---
# Microsoft.ContainerRegistry, Microsoft.App, and Microsoft.OperationalInsights
# are all needed for ACA + ACR + ACA logging. Registration is async; --wait
# blocks until all three are 'Registered'. A fresh PAYG subscription often
# has these in 'NotRegistered' state, which is what caused the first run's
# ACR step to fail with MissingSubscriptionRegistration.
Write-Host "=== Step 0: register resource providers (Microsoft.ContainerRegistry, Microsoft.App, Microsoft.OperationalInsights) ==="
az provider register --namespace Microsoft.ContainerRegistry --wait
Write-Host ("  ContainerRegistry exit: " + $LASTEXITCODE)
az provider register --namespace Microsoft.App --wait
Write-Host ("  App exit: " + $LASTEXITCODE)
az provider register --namespace Microsoft.OperationalInsights --wait
Write-Host ("  OperationalInsights exit: " + $LASTEXITCODE)
Write-Host ""

# --- Step 1: ensure RG exists ---
Write-Host "=== Step 1: ensure RG '$RG' exists in $LOC ==="
$rgExists = az group exists --name $RG
if ($rgExists -like '*false*') {
    Write-Host "  Creating RG..."
    az group create --name $RG --location $LOC
    Write-Host ("  exit code: " + $LASTEXITCODE)
} else {
    Write-Host "  RG already exists; OK"
}
Write-Host ""

# --- Step 2: Storage Account + share ---
Write-Host "=== Step 2a: Storage Account ==="
az storage account create `
    --name $STG_NAME `
    --resource-group $RG `
    --location $LOC `
    --sku Standard_LRS `
    --kind StorageV2 `
    --output table
Write-Host ("  exit code: " + $LASTEXITCODE)
Write-Host ""

Write-Host "=== Step 2b: Azure Files share '$SHARE_NAME' (100 GiB) ==="
az storage share create `
    --name $SHARE_NAME `
    --account-name $STG_NAME `
    --quota 100 `
    --output table
Write-Host ("  exit code: " + $LASTEXITCODE)
Write-Host ""

Write-Host "=== Step 2c: capture storage account key ==="
$STG_KEY = az storage account keys list `
    --account-name $STG_NAME `
    --resource-group $RG `
    --query "[0].value" `
    -o tsv
Write-Host ("  STG_KEY length: " + $STG_KEY.Length + " chars")
Write-Host ""

# --- Step 3: ACR ---
Write-Host "=== Step 3: Container Registry '$ACR_NAME' ==="
az acr create `
    --name $ACR_NAME `
    --resource-group $RG `
    --location $LOC `
    --sku Basic `
    --admin-enabled false `
    --output table
Write-Host ("  exit code: " + $LASTEXITCODE)
Write-Host ""

# --- Step 4: Container Apps Environment + App (PLACEHOLDER ONLY) ---
Write-Host "=== Step 4a: Container Apps Environment '$ACA_ENV' ==="
az containerapp env create `
    --name $ACA_ENV `
    --resource-group $RG `
    --location $LOC `
    --output table
Write-Host ("  exit code: " + $LASTEXITCODE)
Write-Host ""

Write-Host "=== Step 4b: Register the Azure Files share on the Env ==="
Write-Host "  (The share is registered at the Env level so the deploy"
Write-Host "   workflow can attach it as a /data mount via the YAML's"
Write-Host "   volumes[].storageName = '$SHARE_NAME'.)"
Write-Host ""
az containerapp env storage set `
    --name $ACA_ENV `
    --resource-group $RG `
    --storage-name $SHARE_NAME `
    --azure-file-account-name $STG_NAME `
    --azure-file-account-key $STG_KEY `
    --azure-file-share-name $SHARE_NAME `
    --access-mode ReadWrite `
    --output table
Write-Host ("  exit code: " + $LASTEXITCODE)
Write-Host ""

Write-Host "=== Step 4c: Create Container App as PLACEHOLDER (nginx:alpine on port 80) ==="
Write-Host "  No env vars, no volume flags, no scale flags -- the deploy"
Write-Host "  workflow's PATCH (via deploy/aca.servantsync.yaml) handles"
Write-Host "  all of those on the first push. See the script header for"
Write-Host "  the full rationale."
Write-Host ""
az containerapp create `
    --name $ACA_APP `
    --resource-group $RG `
    --environment $ACA_ENV `
    --image "nginx:alpine" `
    --target-port 80 `
    --ingress external `
    --cpu 0.5 `
    --memory 1.0Gi `
    --output table
# NOTE: --min-replicas / --max-replicas are intentionally absent.
# The YAML's scale: { minReplicas: 0, maxReplicas: 3 } PATCHes
# them on first deploy -- setting them here too would just be
# noise that gets immediately overwritten.
Write-Host ("  exit code: " + $LASTEXITCODE)
Write-Host ""

Write-Host "=== Step 4d: get the FQDN ==="
$FQDN = az containerapp show `
    --name $ACA_APP `
    --resource-group $RG `
    --query "properties.configuration.ingress.fqdn" `
    -o tsv
Write-Host "  FQDN: $FQDN"
Write-Host ""
if ($FQDN) {
    Write-Host "  >>> Live URL: https://$FQDN/ <<<"
    Write-Host "  Open that in your browser. You should see the placeholder"
    Write-Host "  nginx default page (Welcome to nginx!) -- that's your"
    Write-Host "  confirmation that ACA ingress + the placeholder Container"
    Write-Host "  App are working. The first real workflow run will replace"
    Write-Host "  this with the actual ServantSync image (port 8080, env vars"
    Write-Host "  from deploy/aca.servantsync.yaml, /data volume attached)."
} else {
    Write-Host "  >>> FQDN is empty -- Container App creation likely failed."
    Write-Host "      Check the Step 4c output above for the error. <<<"
}
Write-Host ""

# --- Final: dump all variables for the next steps ---
Write-Host "============================================="
Write-Host " Variables you'll need for Steps 5-8"
Write-Host "============================================="
Write-Host "  RG=$RG"
Write-Host "  LOC=$LOC"
Write-Host "  ACR_NAME=$ACR_NAME"
Write-Host "  STG_NAME=$STG_NAME"
Write-Host "  ACA_ENV=$ACA_ENV"
Write-Host "  ACA_APP=$ACA_APP"
Write-Host "  SHARE_NAME=$SHARE_NAME"
Write-Host "  SUB_ID=$SUB_ID"
Write-Host "  FQDN=https://$FQDN/"
Write-Host "  SMTP_LOGIN_URL=https://$FQDN/Account/Login"
Write-Host ""
Write-Host "=== Next: run scripts/aca-steps-5-8.ps1 ==="
Write-Host "  It will:"
Write-Host "    Step 5: prompt for the SMTP password, create the secret"
Write-Host "    Step 6: create the App Registration + OIDC federated"
Write-Host "            credential (uses git remote to detect the repo)"
Write-Host "    Step 7: assign AcrPush + Container Apps Contributor roles"
Write-Host "    Step 8: set 6 GitHub secrets via 'gh secret set' if 'gh'"
Write-Host "            is installed and authenticated, else print manual"
Write-Host "            instructions for the GitHub UI"
Write-Host ""
Write-Host "=== Verify the placeholder now if you want ==="
if ($FQDN) {
    Write-Host "  curl -sS -o /dev/null -w '%{http_code}' https://$FQDN/"
    Write-Host "  (expect: 200)"
}
Write-Host ""
Write-Host "=== Done. Stop here. Re-engage to run scripts/aca-steps-5-8.ps1. ==="

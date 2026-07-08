# Retry the ServantSync App Service plan create, this time with
# --location eastus. Uses the full path to az.cmd so it works in
# any PowerShell host (including VS Dev PowerShell, where the
# registry PATH write doesn't propagate).
# Read-only intent: only mutates the user's Azure subscription.

$ErrorActionPreference = 'Continue'

# Wrap az.cmd as a function so the rest of the script can call `az`
# naturally, even if the wbin dir isn't on PATH. Idempotent.
function global:az {
    & 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd' @args
}

# Configurable defaults - override by setting these env vars before running.
$rg      = if ($env:SS_RG)      { $env:SS_RG }      else { 'ServantSync-rg' }
$plan    = if ($env:SS_PLAN)    { $env:SS_PLAN }    else { 'ServantSync-plan' }
$location = if ($env:SS_LOCATION) { $env:SS_LOCATION } else { 'eastus' }
$sku     = if ($env:SS_SKU)     { $env:SS_SKU }     else { 'B1' }

Write-Host "=== Config ==="
Write-Host "  rg       = $rg"
Write-Host "  plan     = $plan"
Write-Host "  location = $location"
Write-Host "  sku      = $sku (Linux)"
Write-Host ""

Write-Host "=== Preflight 1: am I logged in? ==="
az account show --output table
Write-Host ("  exit code: " + $LASTEXITCODE)
Write-Host ""

Write-Host "=== Preflight 2: existing resource groups in current sub ==="
az group list --output table
Write-Host ("  exit code: " + $LASTEXITCODE)
Write-Host ""

Write-Host "=== Preflight 3: existing App Service plans in current sub ==="
az appservice plan list --output table
Write-Host ("  exit code: " + $LASTEXITCODE)
Write-Host ""

Write-Host "=== Step 1: ensure resource group '$rg' exists in '$location' ==="
az group create --name $rg --location $location --output table
Write-Host ("  exit code: " + $LASTEXITCODE)
Write-Host ""

Write-Host "=== Step 2: create App Service plan '$plan' (Linux, $sku) ==="
Write-Host "  This is the call that previously failed with quota=0."
Write-Host "  Now using --location $location explicitly instead of the blank location."
Write-Host ""
az appservice plan create `
    --name $plan `
    --resource-group $rg `
    --location $location `
    --sku $sku `
    --is-linux `
    --output table
Write-Host ("  exit code: " + $LASTEXITCODE)
Write-Host ""

Write-Host "=== Final state: re-list plans ==="
az appservice plan list --output table
Write-Host ("  exit code: " + $LASTEXITCODE)

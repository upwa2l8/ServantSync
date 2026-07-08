# Try the App Service plan create in several alternative regions to find
# one with non-zero default quota. If any region succeeds, we can deploy
# there immediately without a quota request.
$ErrorActionPreference = 'Continue'

function global:az {
    & 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd' @args
}

$rg   = 'servantsync-rg'
$plan = 'ServantSync-plan'
$sku  = 'B1'

# Pick regions that commonly have non-zero default quota on brand-new
# PAYG subs. Order: cheapest in the US, then Europe, then Asia.
$regions = @('westus2', 'westeurope', 'japaneast', 'northeurope')

# Ensure the RG exists (idempotent, region=eastus just for placement)
Write-Host "=== Pre-flight: ensure RG exists ==="
az group create --name $rg --location eastus --output none
Write-Host ("  exit code: " + $LASTEXITCODE)
Write-Host ""

foreach ($loc in $regions) {
    Write-Host ("=== Trying region: $loc ===")
    az appservice plan create `
        --name $plan `
        --resource-group $rg `
        --location $loc `
        --sku $sku `
        --is-linux `
        --output table 2>&1
    $ec = $LASTEXITCODE
    Write-Host ("  exit code: " + $ec)
    Write-Host ""
    if ($ec -eq 0) {
        Write-Host ("  >>> SUCCESS: plan created in $loc <<<")
        break
    }
}

Write-Host ""
Write-Host "=== Final state: list plans across all regions ==="
az appservice plan list --output table 2>&1
Write-Host ("  exit code: " + $LASTEXITCODE)

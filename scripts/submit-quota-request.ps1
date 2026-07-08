# Submit the App Service 'Total VMs' quota increase for eastus (limit 1).
# Idempotent steps (provider register) and read-only steps (list/show) are
# run first; the actual quota request submit is the only side-effect step.
# Read-only intent overall: only one mutation (the quota request).
$ErrorActionPreference = 'Continue'

function global:az {
    & 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd' @args
}

$subId    = az account show --query id -o tsv
$location = 'eastus'
$limit    = 1
$resName  = 'Microsoft.Web'
$resType  = 'serverFarms'
$scope    = "/subscriptions/$subId/providers/$resName/locations/$location"

Write-Host "=== Context ==="
Write-Host "  Sub ID    = $subId"
Write-Host "  Location  = $location"
Write-Host "  Resource  = $resName / $resType"
Write-Host "  Scope     = $scope"
Write-Host "  Requested limit = $limit (Total VMs)"
Write-Host ""

Write-Host "=== Step 1: Register Microsoft.Capacity provider (idempotent) ==="
az provider register --namespace Microsoft.Capacity
Write-Host ("  exit code: " + $LASTEXITCODE)
Write-Host ""

Write-Host "=== Step 2: List available quota counters in $location ==="
az quota list --scope $scope --output table 2>&1
Write-Host ("  exit code: " + $LASTEXITCODE)
Write-Host ""

Write-Host "=== Step 3: Show current Total VMs limit ==="
az quota show --resource-name $resName --resource-type $resType --scope $scope --output table 2>&1
Write-Host ("  exit code: " + $LASTEXITCODE)
Write-Host ""

# Build --limit-object JSON. The quota API expects:
#   { "name": "Total VMs", "limit": <int> }
$limitObj = '{"name": "Total VMs", "limit": ' + $limit + '}'

Write-Host "=== Step 4: Submit the quota increase request ==="
Write-Host "  --limit-object = $limitObj"
Write-Host ""
az quota create `
    --resource-name $resName `
    --resource-type $resType `
    --scope $scope `
    --limit-object $limitObj `
    --output table 2>&1
Write-Host ("  exit code: " + $LASTEXITCODE)
Write-Host ""

Write-Host "=== Step 5: Verify the request landed (re-list) ==="
az quota list --scope $scope --output table 2>&1
Write-Host ("  exit code: " + $LASTEXITCODE)

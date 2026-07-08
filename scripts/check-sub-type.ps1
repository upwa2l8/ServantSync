# Inspect the active subscription. Uses full-path az.cmd wrapper so it
# works in any PowerShell host (incl. VS Dev PowerShell where the
# wbin dir isn't on PATH).
$ErrorActionPreference = 'Continue'

function global:az {
    & 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd' @args
}

Write-Host "=== Subscription diagnostic ==="
az account show `
  --query "{name:name, id:id, offer:offer, state:state, spendingLimit:subscriptionPolicies.spendingLimit, quotaId:subscriptionPolicies.quotaId}" `
  -o table
Write-Host ("  exit code: " + $LASTEXITCODE)
Write-Host ""

Write-Host "=== Full tenant list (just to confirm active sub) ==="
az account list -o table
Write-Host ("  exit code: " + $LASTEXITCODE)

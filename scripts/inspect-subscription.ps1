# Inspect subscription in depth. The first-pass query (az account show
# --query "{name, id, offer, spendingLimit}") returned N/A for offer
# and spendingLimit because those fields aren't in az account show's
# response. Use the dedicated subscription show + raw JSON to see
# what's actually there.
$ErrorActionPreference = 'Continue'

function global:az {
    & 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd' @args
}

Write-Host "=== A. Raw az account show JSON (the source of truth) ==="
az account show | Out-String | Write-Host
Write-Host ""

Write-Host "=== B. Subscription policies via az account subscription show ==="
$subId = az account show --query id -o tsv
if ($LASTEXITCODE -eq 0 -and $subId) {
    Write-Host "  Sub ID: $subId"
    az account subscription show --subscription-id $subId --output json 2>&1
} else {
    Write-Host "  (could not read sub id)"
}
Write-Host ""
Write-Host ("  exit code: " + $LASTEXITCODE)
Write-Host ""

Write-Host "=== C. Other subscriptions visible to your account ==="
az account subscription list --output table 2>&1
Write-Host ""
Write-Host ("  exit code: " + $LASTEXITCODE)

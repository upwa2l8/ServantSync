# Pre-az-login precheck: clear stale creds, disable WAM silent-SSO,
# verify state. Does NOT run the blocking az login --use-device-code
# step (that needs to run interactively in the user's own pwsh).
# Read-only intent: only mutates local ~/.azure/ + az config.
$ErrorActionPreference = 'Continue'

# Patch current process PATH so 'az' resolves even in environments
# (e.g. a non-interactive basher subprocess) that don't pick up our
# HKCU\Environment write at startup. Idempotent.
$azWbin = 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin'
if ($env:Path -notlike ('*' + $azWbin + '*')) {
    [Environment]::SetEnvironmentVariable('Path', $env:Path + ';' + $azWbin, 'Process')
}

Write-Host "=== STEP 1: az account clear ==="
az account clear
Write-Host ("  exit code: " + $LASTEXITCODE)
Write-Host ""

Write-Host "=== STEP 2: az config set core.enable_wam=false ==="
az config set core.enable_wam=false
Write-Host ("  exit code: " + $LASTEXITCODE)
Write-Host ""

Write-Host "=== Verify: az config get core.enable_wam ==="
az config get core.enable_wam
Write-Host ("  exit code: " + $LASTEXITCODE)
Write-Host ""

Write-Host "=== Pre-login: az account show (expecting: not logged in) ==="
az account show 2>&1
Write-Host ("  exit code: " + $LASTEXITCODE)

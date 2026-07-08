# Non-interactive half of the az login flow:
# 1. Clear stale MSAL tokens, 2. disable WAM silent SSO, 3. verify state.
# Read-only intent: only mutates the local ~/.azure/ directory.
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

Write-Host "=== Current az account state (expecting: not logged in) ==="
az account show 2>&1
Write-Host ("  exit code: " + $LASTEXITCODE)
Write-Host ""

Write-Host "=== STEP 3: az login --use-device-code ==="
Write-Host "  (prints a URL + 9-char code, then blocks for up to 15 min"
Write-Host "   waiting for the user to enter the code at the URL.)"
Write-Host ""
az login --use-device-code

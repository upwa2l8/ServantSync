# Verify Azure CLI PATH fix - one-shot that writes the user PATH and
# confirms both the registry write and az end-to-end resolution.
# Read-only intent: it appends one path entry to HKCU\Environment.
$ErrorActionPreference = 'Continue'

Write-Host "============================================="
Write-Host " Azure CLI PATH fix + verification"
Write-Host "============================================="

$target = 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin'

# Snapshot the current state so the user can see the before/after.
Write-Host ""
Write-Host "[STEP 1] BEFORE - is az reachable in this PowerShell process?"
$hasAz = Get-Command az -ErrorAction SilentlyContinue
if ($hasAz) {
    Write-Host ("  FOUND: " + $hasAz.Source)
} else {
    Write-Host "  NOT FOUND in current process"
}

Write-Host ""
Write-Host "[STEP 2] Write target to user-level PATH (HKCU\Environment)"
$userPath = [Environment]::GetEnvironmentVariable('Path','User')
$alreadyPresent = $userPath -like ('*' + $target + '*')
if ($alreadyPresent) {
    Write-Host "  Already present - skipping write"
    $wroteUserPath = $false
} else {
    $newUserPath = "$userPath;$target"
    try {
        [Environment]::SetEnvironmentVariable('Path', $newUserPath, 'User')
        $wroteUserPath = $true
        Write-Host ("  Appended: " + $target)
        Write-Host ("  User Path length: " + $newUserPath.Length + " chars (was " + $userPath.Length + ")")
    } catch {
        $wroteUserPath = $false
        Write-Host ("  FAILED: " + $_.Exception.Message)
    }
}

Write-Host ""
Write-Host "[STEP 3] Re-read user-level PATH from registry to confirm persistence"
$verify = [Environment]::GetEnvironmentVariable('Path','User')
if ($verify -like ('*' + $target + '*')) {
    Write-Host "  CONFIRMED - target is persisted in HKCU\Environment"
    Write-Host ("  Full user PATH length now: " + $verify.Length + " chars")
} else {
    Write-Host "  PROBLEM - registry doesn't reflect the change"
}

# Windows loads user PATH from HKCU\Environment only at NEW-process startup.
# We can't simulate a fresh terminal from here, but we can update the
# current process PATH so the same-process verification proves az resolves.
Write-Host ""
Write-Host "[STEP 4] Mirror the change into this-process PATH so downstream"
Write-Host "          commands in this PS session can resolve az."
[Environment]::SetEnvironmentVariable('Path', $env:Path + ';' + $target, 'Process')

Write-Host ""
Write-Host "[STEP 5] After-fix resolution checks"
$where = & where.exe az 2>&1
$wex = $LASTEXITCODE
if ($wex -eq 0 -and $where) {
    Write-Host "  where.exe az FOUND:"
    foreach ($line in $where) { Write-Host ("    " + $line) }
} else {
    Write-Host "  where.exe az STILL MISSING"
}
$gcm = Get-Command az -ErrorAction SilentlyContinue
if ($gcm) {
    Write-Host ("  Get-Command az FOUND: " + $gcm.Source)
} else {
    Write-Host "  Get-Command az STILL MISSING"
}

Write-Host ""
Write-Host "[STEP 6] az --version (gold-standard end-to-end check)"
Write-Host "  ------------------------------"
try {
    $ver = & az --version 2>&1
    if ($ver) { Write-Host ($ver -join "`n") }
    else { Write-Host "  (no output captured)" }
} catch {
    Write-Host ("  az --version FAILED: " + $_.Exception.Message)
}
Write-Host "  ------------------------------"

Write-Host ""
Write-Host "============================================="
Write-Host " Verification complete"
Write-Host "============================================="

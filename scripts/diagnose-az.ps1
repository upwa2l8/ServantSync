# Azure CLI install diagnostic - read-only.
# ASCII-only, no inline subexpressions.
$ErrorActionPreference = 'Continue'

Write-Host "============================================="
Write-Host " Azure CLI install diagnostic"
Write-Host "============================================="

# 1. where.exe az (cmd's PATH resolver)
Write-Host ""
Write-Host "[1] where.exe az (cmd PATH scan)"
try {
    & where.exe az 2>&1
} catch {
    Write-Host ("  ERROR: " + $_.Exception.Message)
}

# 2. Get-Command az (PowerShell resolver)
Write-Host ""
Write-Host "[2] Get-Command az (PowerShell resolver)"
$gcm = Get-Command az -ErrorAction SilentlyContinue
if ($gcm) {
    Write-Host ("  FOUND: " + $gcm.Source)
} else {
    Write-Host "  NOT FOUND by Get-Command"
}

# 3. Standard MSI install targets
Write-Host ""
Write-Host "[3] Standard MSI install locations (az.cmd)"
$locations = @(
    "C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin",
    "C:\Program Files (x86)\Microsoft SDKs\Azure\CLI2\wbin"
)
foreach ($loc in $locations) {
    $shim = Join-Path $loc "az.cmd"
    $tag = "ABSENT"
    if (Test-Path -PathType Leaf $shim) { $tag = "EXIST " }
    Write-Host ("  [" + $tag + "] " + $shim)
}

# 4. WindowsApps folder (Microsoft Store shim)
Write-Host ""
Write-Host "[4] WindowsApps shim (Store install)"
$wa = Join-Path $env:LOCALAPPDATA "Microsoft\WindowsApps"
$waExists = Test-Path $wa
if ($waExists) {
    $shims = Get-ChildItem $wa -Filter "az*" -ErrorAction SilentlyContinue
    if ($shims) {
        foreach ($s in $shims) {
            Write-Host ("  " + $s.Name + "  (" + $s.Length + " bytes)  " + $s.LastWriteTime)
        }
    } else {
        Write-Host "  (no az* shims in WindowsApps)"
    }
} else {
    Write-Host "  (WindowsApps not present)"
}

# 5. PATH entries mentioning Azure
Write-Host ""
Write-Host "[5] PATH entries mentioning 'Azure'"
$azureEntries = @()
foreach ($p in $env:Path.Split(";")) {
    if ($p -like "*Azure*") { $azureEntries += $p }
}
if ($azureEntries.Count -gt 0) {
    foreach ($e in $azureEntries) { Write-Host ("  " + $e) }
} else {
    Write-Host "  (none)"
}

# 6. winget install record
Write-Host ""
Write-Host "[6] winget list --id Microsoft.AzureCLI"
$wgOutput = ""
$wgError = ""
$wgExit = 0
try {
    $wg = & winget list --id Microsoft.AzureCLI --accept-source-agreements
    $wgExit = $LASTEXITCODE
    if ($wg) { Write-Host ($wg -join "`n") }
    else { Write-Host "  (no output from winget)" }
} catch {
    Write-Host ("  winget error: " + $_.Exception.Message)
}
Write-Host ("  winget exit code: " + $wgExit)

# 7. Appx install (Store)
Write-Host ""
Write-Host "[7] Get-AppxPackage Microsoft.AzureCLI (Store install)"
$appx = Get-AppxPackage -AllUsers -Name "Microsoft.AzureCLI" -ErrorAction SilentlyContinue
if ($appx) {
    $tn = ""
    if ($appx.PackageFullName) { $tn = $appx.PackageFullName }
    Write-Host ("  Name: " + $appx.Name)
    Write-Host ("  PackageFullName: " + $tn)
    if ($appx.InstallLocation) { Write-Host ("  InstallLocation: " + $appx.InstallLocation) }
    if ($appx.Version)         { Write-Host ("  Version: " + $appx.Version) }
} else {
    Write-Host "  (no Microsoft Store install present)"
}

# 8. PowerShell environment
Write-Host ""
Write-Host "[8] PowerShell environment"
Write-Host ("  PSVersion = " + $PSVersionTable.PSVersion)
Write-Host ("  PSEdition = " + $PSVersionTable.PSEdition)
Write-Host ("  Processor = " + $env:PROCESSOR_ARCHITECTURE)

Write-Host ""
Write-Host "============================================="
Write-Host " End of diagnostic"
Write-Host "============================================="

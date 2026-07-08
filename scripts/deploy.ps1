<#
.SYNOPSIS
    Publish ServantSync (Release) + zip + az webapp deploy on Windows PowerShell.

.DESCRIPTION
    Mirrors scripts/deploy.sh for Windows-native PowerShell. Validates prereqs,
    publishes in Release config, zips the publish output, and pushes it to the
    given Azure Web App via the az CLI.

.PARAMETER AppName
    Azure Web App name (e.g. servantsync-demo-church).

.PARAMETER ResourceGroup
    Resource group containing the Web App (e.g. servantsync-rg).

.PARAMETER Restart
    Restart the Web App after deployment (helps pick up new SMTP settings
    without waiting for the next cold start).

.PARAMETER SkipZip
    Skip the zip step (used by CI pipelines that have their own zip step).

.EXAMPLE
    .\scripts\deploy.ps1 -AppName servantsync-demo-church -ResourceGroup servantsync-rg

.EXAMPLE
    .\scripts\deploy.ps1 -AppName servantsync-demo-church -ResourceGroup servantsync-rg -Restart

.NOTES
    Prereqs: .NET 9 SDK + az CLI + PowerShell 5+ (Compress-Archive built in).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$AppName,

    [Parameter(Mandatory = $true, Position = 1)]
    [string]$ResourceGroup,

    [switch]$Restart,

    [switch]$SkipZip
)

$ErrorActionPreference = 'Stop'

# ----- prereq checks --------------------------------------------------------
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet CLI not found in PATH. Install .NET 9 SDK."; exit 3
}

$dotnetVersion = dotnet --version
if (-not ($dotnetVersion -match '^9\.')) {
    Write-Error ".NET 9 SDK required, found $dotnetVersion."; exit 3
}
Write-Host "✓ dotnet $dotnetVersion" -ForegroundColor Green

if (-not $SkipZip) {
    # Compress-Archive is built into PowerShell 5+; no separate 'zip' install needed.
    Write-Host "✓ Compress-Archive available (PowerShell built-in)" -ForegroundColor Green
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    Write-Error "az CLI not found. Install: winget install Microsoft.AzureCLI"; exit 3
}
$azVersionRaw = az --version 2>$null | Select-Object -First 1
Write-Host "✓ $azVersionRaw" -ForegroundColor Green

# ----- anchor at repo root -------------------------------------------------
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path "$scriptDir\.."
Set-Location $repoRoot
Write-Host "→ Repo root: $repoRoot"

# ----- publish -------------------------------------------------------------
Write-Host "→ dotnet publish (Release)…"
dotnet publish -c Release -o .\publish
if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish failed (exit=$LASTEXITCODE)"; exit 5 }
Write-Host "✓ publish complete" -ForegroundColor Green

# ----- zip -----------------------------------------------------------------
if (-not $SkipZip) {
    $zipPath = Join-Path $repoRoot 'deploy.zip'
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Write-Host "→ building $zipPath…"

    # Compress-Archive expects a list of source paths OR a directory.
    # We want the publish/ contents AT THE ZIP ROOT (azure-webapps-deploy
    # requires that layout). Two-step approach:
    #   1. Copy publish -> publish-flat/ excluding .pdb + dev appsettings.
    #   2. Compress-Archive that flat directory into deploy.zip.
    #   3. Clean up the staging directory.
    $flat = Join-Path $repoRoot 'publish-flat'
    if (Test-Path $flat) { Remove-Item $flat -Recurse -Force }
    robocopy "$repoRoot\publish" $flat `
        /XF *.pdb appsettings.Development.json `
        /XD $flat `
        /NJH /NJS /NDL /NFL | Out-Null
    # Capture robocopy's exit BEFORE Compress-Archive overwrites $LASTEXITCODE.
    # robocopy exit codes 0..7 are success-shaped (1 = "files copied", 2 =
    # "extra files seen", 3 = "files copied + extra", etc.); only 8+ are
    # real errors. Without this capture, a real robocopy error (8+) would
    # silently pass through to Compress-Archive against an empty staging
    # dir, producing a partial zip that the Web App deploy still accepts.
    $robocopyExit = $LASTEXITCODE
    if ($robocopyExit -gt 7) {
        throw "robocopy failed with exit code $robocopyExit"
    }

    Compress-Archive -Path "$flat\*" -DestinationPath $zipPath -Force
    Remove-Item $flat -Recurse -Force
    $size = "{0:N2} MB" -f ((Get-Item $zipPath).Length / 1MB)
    Write-Host "✓ deploy.zip built ($size)" -ForegroundColor Green
}

# ----- verify Web App exists ----------------------------------------------
Write-Host "→ verifying Web App '$AppName' in resource group '$ResourceGroup'…"
$state = az webapp show --name $AppName --resource-group $ResourceGroup --query state -o tsv 2>$null
if ([string]::IsNullOrEmpty($state)) {
    Write-Error "Web App '$AppName' not found in resource group '$ResourceGroup'."
    Write-Error "Run: az webapp list --resource-group `"$ResourceGroup`""
    exit 4
}
Write-Host "✓ Web App found (state=$state)" -ForegroundColor Green

# ----- deploy --------------------------------------------------------------
Write-Host "→ az webapp deploy --type zip…"
az webapp deploy `
    --name $AppName `
    --resource-group $ResourceGroup `
    --src-path $zipPath `
    --type zip
if ($LASTEXITCODE -ne 0) { Write-Error "az webapp deploy failed (exit=$LASTEXITCODE)"; exit 6 }
Write-Host "✓ deploy dispatched" -ForegroundColor Green

# ----- optional restart ----------------------------------------------------
if ($Restart) {
    Write-Host "→ az webapp restart…"
    az webapp restart --name $AppName --resource-group $ResourceGroup
    if ($LASTEXITCODE -ne 0) { Write-Error "az webapp restart failed"; exit 7 }
    Write-Host "✓ restart complete" -ForegroundColor Green
}

# ----- success summary -----------------------------------------------------
$host = az webapp show --name $AppName --resource-group $ResourceGroup --query defaultHostName -o tsv
Write-Host ""
Write-Host "🎉 Deploy complete." -ForegroundColor Green
Write-Host ""
Write-Host "  App:         $AppName"
Write-Host "  ResourceGrp: $ResourceGroup"
Write-Host "  URL:         https://$host"
Write-Host ""
Write-Host "  Next steps:"
Write-Host "    1. Tail logs:  az webapp log tail --name `"$AppName`" --resource-group `"$ResourceGroup`""
Write-Host "    2. Smoke check: open https://$host/Account/Login in a browser."
Write-Host "    3. SQLite lives at /home/site/wwwroot on the App Service box; set"
Write-Host "       ConnectionStrings__DefaultConnection accordingly. (See DEPLOY.md.)"

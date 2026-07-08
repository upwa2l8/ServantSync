#!/usr/bin/env bash
# scripts/deploy.sh — Publish ServantSync (Release) + zip + az webapp deploy.
#
# Usage:  scripts/deploy.sh <app-name> <resource-group> [--restart] [--no-zip]
#   <app-name>        Azure Web App name (e.g. servantsync-demo-church)
#   <resource-group>  Resource group containing the Web App (e.g. servantsync-rg)
#   --restart         Restart the Web App after deployment (helps pick up new SMTP settings)
#   --no-zip          Skip the zip step (used by CI which has its own zip pipeline)
#   -h | --help       Show this message
#
# Examples:
#   scripts/deploy.sh servantsync-demo-church servantsync-rg
#   scripts/deploy.sh servantsync-demo-church servantsync-rg --restart
#
# Prereqs:
#   - .NET 9 SDK
#   - Azure CLI logged in (az login) OR running under Azure Cloud Shell
#   - The Web App must already exist (see DEPLOY.md "Step 1 — Provision")
#
# Stripped from .pdb + appsettings.Development.json to keep the zip small
# AND prevent accidental override of production config.

set -euo pipefail

show_help() {
  sed -n '2,16p' "$0"
}

# ----- args ----------------------------------------------------------------
APP_NAME=""
RG=""
RESTART="false"
SKIP_ZIP="false"

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help) show_help; exit 0;;
    --restart) RESTART="true"; shift;;
    --no-zip)  SKIP_ZIP="true"; shift;;
    -*) echo "Unknown arg: $1" >&2; show_help; exit 2;;
    *)
      if [[ -z "$APP_NAME" ]]; then APP_NAME="$1"; shift
      elif [[ -z "$RG" ]]; then RG="$1"; shift
      else echo "Too many positional args." >&2; show_help; exit 2; fi
      ;;
  esac
done

if [[ -z "$APP_NAME" || -z "$RG" ]]; then
  echo "Error: missing required <app-name> and/or <resource-group>." >&2
  show_help
  exit 2
fi

# ----- prereq checks -------------------------------------------------------
if ! command -v dotnet >/dev/null 2>&1; then
  echo "Error: dotnet CLI not found in PATH. Install .NET 9 SDK." >&2; exit 3
fi

DOTNET_VERSION="$(dotnet --version)"
# dotnet --version emits '9.0.x'; require the major version 9.
if ! [[ "$DOTNET_VERSION" =~ ^9\. ]]; then
  echo "Error: .NET 9 SDK required, found $DOTNET_VERSION." >&2; exit 3
fi
echo "✓ dotnet $DOTNET_VERSION"

if [[ "$SKIP_ZIP" != "true" ]]; then
  if ! command -v zip >/dev/null 2>&1; then
    echo "Error: 'zip' command not found. Install it or pass --no-zip." >&2; exit 3
  fi
  echo "✓ zip present"
fi

if ! command -v az >/dev/null 2>&1; then
  echo "Error: az CLI not found. Install Azure CLI: https://learn.microsoft.com/cli/azure/install-azure-cli" >&2; exit 3
fi
AZ_VERSION="$(az --version | head -n1)"
echo "✓ $AZ_VERSION"

# ----- publish -------------------------------------------------------------
# Run from the repo root (script's own parent), regardless of cwd invocation.
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

echo "→ dotnet publish (Release)…"
dotnet publish -c Release -o ./publish
echo "✓ publish complete ($(du -sh ./publish | cut -f1))"

# ----- zip -----------------------------------------------------------------
if [[ "$SKIP_ZIP" != "true" ]]; then
  rm -f deploy.zip
  # zip from INSIDE publish/ so the files land at the zip root (azure-webapps-deploy
  # requires that layout).
  (cd publish && zip -r ../deploy.zip . -x "*.pdb" "appsettings.Development.json") > /dev/null
  echo "✓ deploy.zip built ($(du -h deploy.zip | cut -f1))"
fi

# ----- verify Web App exists + is in a state that can be deployed to ------
STATE="$(az webapp show --name "$APP_NAME" --resource-group "$RG" --query state -o tsv 2>/dev/null || true)"
if [[ -z "$STATE" ]]; then
  echo "Error: Web App '$APP_NAME' not found in resource group '$RG'." >&2
  echo "Run az webapp list --resource-group \"$RG\" to confirm the spelling." >&2
  exit 4
fi
echo "✓ Web App '$APP_NAME' found (state=$STATE)"

# ----- deploy --------------------------------------------------------------
echo "→ az webapp deploy --type zip…"
az webapp deploy \
  --name "$APP_NAME" \
  --resource-group "$RG" \
  --src-path deploy.zip \
  --type zip
echo "✓ deploy dispatched"

# ----- optional restart ----------------------------------------------------
if [[ "$RESTART" == "true" ]]; then
  echo "→ az webapp restart…"
  az webapp restart --name "$APP_NAME" --resource-group "$RG"
  echo "✓ restart complete"
fi

# ----- success summary -----------------------------------------------------
HOST="$(az webapp show --name "$APP_NAME" --resource-group "$RG" --query defaultHostName -o tsv)"
cat <<EOF

🎉 Deploy complete.

  App:        $APP_NAME
  ResourceGrp: $RG
  URL:        https://$HOST

  Next steps:
    1. Tail logs:  az webapp log tail --name "$APP_NAME" --resource-group "$RG"
    2. Smoke check: open https://$HOST/Account/Login in a browser.
    3. SQLite lives at /home/site/wwwroot on the App Service box; set
       ConnectionStrings__DefaultConnection accordingly. (See DEPLOY.md.)

EOF

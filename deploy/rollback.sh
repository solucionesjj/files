#!/usr/bin/env bash
# Files — rollback a production deploy.
#
# Usage (on the host, as the deploy user):
#   cd /opt/files/app && ./deploy/rollback.sh [<previous-tag-or-sha>]
#
# Optional database restore (destructive; requires two-person approval per the runbook):
#   RESTORE_DUMP=s3://<bucket>/backups/postgres/<file>.dump.gz RESTORE_CONFIRM=YES ./deploy/rollback.sh <ref>
#
# Behaviour:
#   1. if RESTORE_DUMP is set, restore it into the running PostgreSQL (deploy/restore.sh)
#   2. checkout the previous ref (default: prev_sha recorded in /opt/files/REVISION)
#   3. rebuild/rsync the SPA at that ref and start api/worker/caddy
#   4. run deploy/smoke.sh and re-record /opt/files/REVISION
#
# Application rollback is preferred; a database restore is only for a migration that broke
# the database and cannot be forward-fixed (expand/contract is the norm — see
# docs/08-operations/deployment-and-rollback.md).
set -euo pipefail

APP_DIR="${APP_DIR:-/opt/files/app}"
ENV_FILE="${FILES_ENV_FILE:-/opt/files/.env}"
COMPOSE_FILE="$APP_DIR/deploy/docker-compose.prod.yml"
PORTAL_DIR="${PORTAL_DIR:-/var/lib/files/portal}"

DC=(docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE")
get_env() {
  grep -E "^${1}=" "$ENV_FILE" | tail -n 1 | cut -d= -f2- | sed -e 's/^"//' -e 's/"$//'
}
log() { printf '[rollback] %s\n' "$*"; }
die() { printf '[rollback] ERROR: %s\n' "$*" >&2; exit 1; }

# Render the SPA runtime config from the environment (same contract as deploy.sh).
write_spa_config() {
  local tenant_id client_id api_audience required_scope base_url public_links api_scope redirect_uri
  tenant_id="$(get_env Entra__TenantId)"
  client_id="$(get_env Portal__ClientId)"
  api_audience="$(get_env Entra__ApiAudience)"
  required_scope="$(get_env Entra__RequiredScope)"
  base_url="$(get_env Portal__BaseUrl)"
  public_links="$(get_env Features__PublicLinks)"

  [ -n "$tenant_id" ] || die "Entra__TenantId is required to build the SPA config"
  [ -n "$client_id" ] || die "Portal__ClientId is required to build the SPA config"
  [ -n "$api_audience" ] || die "Entra__ApiAudience is required to build the SPA config"
  [ -n "$base_url" ] || die "Portal__BaseUrl is required to build the SPA config"

  api_scope="$api_audience"
  [ -z "$required_scope" ] || api_scope="$api_audience/$required_scope"
  redirect_uri="$base_url/auth/callback"

  case "$public_links" in
    true|false) ;;
    *) public_links=true ;;
  esac

  log "Writing SPA runtime config to $PORTAL_DIR/assets/config.json"
  mkdir -p "$PORTAL_DIR/assets"
  cat > "$PORTAL_DIR/assets/config.json" <<EOF
{
  "environment": "production",
  "apiBaseUrl": "$base_url",
  "tenantId": "$tenant_id",
  "clientId": "$client_id",
  "apiScope": "$api_scope",
  "redirectUri": "$redirect_uri",
  "features": {
    "publicLinks": $public_links
  }
}
EOF
}

cd "$APP_DIR"

TARGET_REF="${1:-}"
if [ -z "$TARGET_REF" ] && [ -f /opt/files/REVISION ]; then
  TARGET_REF="$(grep -E '^prev_sha=' /opt/files/REVISION | head -n1 | cut -d= -f2- || true)"
fi
[ -n "$TARGET_REF" ] || die "no rollback ref; pass one or restore /opt/files/REVISION"

if [ -n "${RESTORE_DUMP:-}" ]; then
  [ "${RESTORE_CONFIRM:-}" = "YES" ] || die "RESTORE_DUMP set but RESTORE_CONFIRM != YES"
  log "Restoring pre-deploy dump $RESTORE_DUMP ..."
  RESTORE_DUMP="$RESTORE_DUMP" RESTORE_CONFIRM=YES "$APP_DIR/deploy/restore.sh"
fi

log "Checking out $TARGET_REF ..."
git fetch --tags --prune
git checkout "$TARGET_REF"
NEW_SHA="$(git rev-parse HEAD)"
export FILES_VERSION="$NEW_SHA"

log "Building and publishing the SPA at $TARGET_REF ..."
docker build -f "$APP_DIR/web/Dockerfile" --target build -t files-web:build "$APP_DIR/web"
SPA_CID="$(docker create files-web:build)"
trap 'docker rm -f "$SPA_CID" >/dev/null 2>&1 || true' EXIT
docker cp "$SPA_CID:/app/dist/web/browser/." "$PORTAL_DIR/"
write_spa_config
docker rm -f "$SPA_CID" >/dev/null
trap - EXIT

log "Starting api/worker/caddy at $TARGET_REF ..."
"${DC[@]}" up -d --build api worker caddy

log "Running smoke tests ..."
FILES_DOMAIN="$(get_env FILES_DOMAIN)" "$APP_DIR/deploy/smoke.sh"

{
  echo "ref=$TARGET_REF"
  echo "sha=$NEW_SHA"
  echo "prev_sha=$(git rev-parse HEAD)"
  echo "backup=rollback"
  echo "deployed_at=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
} > /opt/files/REVISION

log "Rollback complete: $TARGET_REF ($NEW_SHA). Record the incident and root cause."

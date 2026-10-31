#!/usr/bin/env bash
# Files — production deploy (single pre-existing host). Idempotent and fail-fast.
#
# Usage (on the host, as the deploy user):
#   cd /opt/files/app && ./deploy/deploy.sh <git-tag-or-sha>
#
# Stages:
#   1. git fetch + checkout the release ref (refuses a dirty tree or unknown ref)
#   2. ensure PostgreSQL is running, then take a pre-deploy backup and capture its S3 key
#   3. run the Flyway migrator explicitly (--profile tools) — migrations are NEVER run
#      automatically by the API/worker
#   4. build the Angular SPA (node:22-alpine via Docker, no host Node required) and rsync
#      it into /var/lib/files/portal
#   5. docker compose up -d --build api worker caddy (images tagged with the release SHA)
#   6. deploy/smoke.sh
#   7. record /opt/files/REVISION (ref, sha, previous sha, backup key, timestamp)
#
# Rollback: ./deploy/rollback.sh [<previous-ref>] [RESTORE_DUMP=s3://...]
#   (see docs/08-operations/deployment-and-rollback.md)
set -euo pipefail

APP_DIR="${APP_DIR:-/opt/files/app}"
ENV_FILE="${FILES_ENV_FILE:-/opt/files/.env}"
COMPOSE_FILE="$APP_DIR/deploy/docker-compose.prod.yml"
PORTAL_DIR="${PORTAL_DIR:-/var/lib/files/portal}"
RELEASE_REF="${1:?usage: deploy.sh <git-tag-or-sha>}"

DC=(docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE")

get_env() {
  grep -E "^${1}=" "$ENV_FILE" | tail -n 1 | cut -d= -f2- | sed -e 's/^"//' -e 's/"$//'
}

log() { printf '[deploy] %s\n' "$*"; }
die() { printf '[deploy] ERROR: %s\n' "$*" >&2; exit 1; }

# Render the SPA runtime config from the environment. The SPA reads /assets/config.json (no
# secrets); clientId/tenantId/redirectUri/apiScope must be real values or sign-in is disabled.
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

[ -f "$ENV_FILE" ] || die "missing $ENV_FILE (create it from deploy/env.prod.example, mode 0600)"
[ -d "$APP_DIR/.git" ] || die "$APP_DIR is not a git checkout"

log "Fetching and checking out $RELEASE_REF ..."
cd "$APP_DIR"
git fetch --tags --prune
PREV_SHA="$(git rev-parse HEAD)"
if ! git diff --quiet; then
  die "working tree is dirty; commit or stash before deploying"
fi
git checkout "$RELEASE_REF"
NEW_SHA="$(git rev-parse HEAD)"
[ "$NEW_SHA" != "$PREV_SHA" ] || log "note: $RELEASE_REF already matches HEAD ($NEW_SHA)"
export FILES_VERSION="$NEW_SHA"

log "Ensuring PostgreSQL is running ..."
"${DC[@]}" up -d postgres

log "Backing up the database before any migration ..."
BACKUP_OUT="$(mktemp)"
"$APP_DIR/deploy/backup.sh" | tee "$BACKUP_OUT"
BACKUP_KEY="$(grep -oE 's3://[^[:space:]]+\.dump\.gz' "$BACKUP_OUT" | tail -n 1 || true)"
[ -n "$BACKUP_KEY" ] || die "could not determine the pre-deploy backup key"
rm -f "$BACKUP_OUT"
log "Pre-deploy backup: $BACKUP_KEY"

log "Running Flyway migrations (explicit, never auto) ..."
"${DC[@]}" --profile tools run --rm migrator

log "Building the Angular SPA (Docker node:22-alpine) and publishing to $PORTAL_DIR ..."
docker build -f "$APP_DIR/web/Dockerfile" --target build -t files-web:build "$APP_DIR/web"
SPA_CID="$(docker create files-web:build)"
trap 'docker rm -f "$SPA_CID" >/dev/null 2>&1 || true' EXIT
docker cp "$SPA_CID:/app/dist/web/browser/." "$PORTAL_DIR/"
write_spa_config
docker rm -f "$SPA_CID" >/dev/null
trap - EXIT

log "Starting api/worker/caddy ..."
"${DC[@]}" up -d --build api worker caddy

log "Running smoke tests ..."
FILES_DOMAIN="$(get_env FILES_DOMAIN)" "$APP_DIR/deploy/smoke.sh"

log "Recording /opt/files/REVISION ..."
{
  echo "ref=$RELEASE_REF"
  echo "sha=$NEW_SHA"
  echo "prev_sha=$PREV_SHA"
  echo "backup=$BACKUP_KEY"
  echo "deployed_at=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
} > /opt/files/REVISION

log "Deploy complete: $RELEASE_REF ($NEW_SHA). Rollback: ./deploy/rollback.sh"

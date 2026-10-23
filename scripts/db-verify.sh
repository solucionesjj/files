#!/usr/bin/env bash
# Apply the migrations to a throwaway PostgreSQL 18 container and assert the result.
set -euo pipefail

cd "$(dirname "$0")/.."

NAME="files-db-verify-$$"
IMAGE="${POSTGRES_IMAGE:-postgres:18-alpine}"

cleanup() {
  docker rm -f "$NAME" >/dev/null 2>&1 || true
}
trap cleanup EXIT

echo "Starting $IMAGE as $NAME ..."
docker run -d --name "$NAME" \
  -e POSTGRES_DB=files \
  -e POSTGRES_PASSWORD=verify-only \
  "$IMAGE" >/dev/null

ready=0
for _ in $(seq 1 60); do
  if docker exec "$NAME" psql -U postgres -d files -c 'select 1' >/dev/null 2>&1; then
    ready=1
    break
  fi
  sleep 1
done

if [ "$ready" -ne 1 ]; then
  echo "PostgreSQL did not become ready" >&2
  docker logs "$NAME" >&2 || true
  exit 1
fi

echo "Applying V1 ..."
docker exec -i "$NAME" psql -U postgres -d files -v ON_ERROR_STOP=1 -q \
  < db/migrations/V1__initial_schema.sql

echo "Applying V2 ..."
docker exec -i "$NAME" psql -U postgres -d files -v ON_ERROR_STOP=1 -q \
  < db/migrations/V2__seed_reference_data.sql

query() {
  docker exec "$NAME" psql -U postgres -d files -tAc "$1" | tr -d '[:space:]'
}

tables=$(query "select count(*) from information_schema.tables where table_schema='public'")
policies=$(query "select count(*) from retention_policies where is_active")
templates=$(query "select count(*) from email_templates")
settings=$(query "select count(*) from platform_settings")

echo "tables=$tables active_retention_policies=$policies templates=$templates settings=$settings"

[[ "$tables" -ge 27 ]] || { echo "expected at least 27 tables, got $tables" >&2; exit 1; }
[[ "$policies" -eq 1 ]] || { echo "expected 1 active retention policy, got $policies" >&2; exit 1; }
[[ "$templates" -eq 2 ]] || { echo "expected 2 email templates, got $templates" >&2; exit 1; }
[[ "$settings" -ge 8 ]] || { echo "expected at least 8 settings, got $settings" >&2; exit 1; }

# Seed one user/file so the link constraint is reachable.
docker exec -i "$NAME" psql -U postgres -d files -v ON_ERROR_STOP=1 -q <<'SQL'
INSERT INTO users (tenant_id, object_id, display_name)
VALUES (gen_random_uuid(), gen_random_uuid(), 'Schema Verifier');
INSERT INTO files (owner_type, owner_user_id, original_filename, storage_bucket,
                   storage_key, size_bytes, content_type, status, retention_mode, expires_at)
SELECT 'human', id, 'verify.txt', 'files-verify', 'f/verify', 1, 'text/plain',
       'available', 'finite', now() + interval '1 day'
FROM users LIMIT 1;
SQL

# A few invariant checks (each must fail).
expect_failure() {
  local label="$1" sql="$2"
  if docker exec -i "$NAME" psql -U postgres -d files -v ON_ERROR_STOP=1 -q -c "$sql" >/dev/null 2>&1; then
    echo "invariant NOT enforced: $label" >&2
    exit 1
  fi
  echo "invariant ok: $label"
}

expect_failure "public link requires a key" \
  "INSERT INTO download_links (file_id, token_hash, access_mode, created_by_type, created_by)
   SELECT f.id, decode(repeat('ab',32),'hex'), 'public', 'user', u.id
   FROM files f, users u LIMIT 1;"

echo "OK: migrations applied and invariants enforced"

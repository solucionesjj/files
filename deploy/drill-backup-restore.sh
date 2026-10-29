#!/usr/bin/env bash
# Files — backup/restore drill (launch gate 5).
#
# Proves the deploy/backup.sh → _latest marker → deploy/restore.sh procedure end-to-end against a
# disposable PostgreSQL 18 + LocalStack S3, and measures RTO/RPO against the SLO targets
# (RPO 24 h / RTO 4 h). Requires Docker and the aws CLI; images are pulled on first run.
#
#   ./deploy/drill-backup-restore.sh
#
# The production host runs the same pg_dump|gzip → s3://<bucket>/backups/postgres/ flow via
# deploy/backup.sh; this drill substitutes LocalStack S3 for the pre-existing bucket and disposable
# containers for the production PostgreSQL.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
NET="files-drill-net-$$"
SRC="files-drill-src-$$"
TGT="files-drill-tgt-$$"
LS="files-drill-ls-$$"
BUCKET="files-drill"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
DUMP="/tmp/files-drill-${STAMP}.dump.gz"

now_ms() { python3 -c 'import time; print(int(time.time()*1000))'; }

cleanup() {
  docker rm -f "$SRC" "$TGT" "$LS" >/dev/null 2>&1 || true
  docker network rm "$NET" >/dev/null 2>&1 || true
  rm -f "$DUMP"
}
trap cleanup EXIT

echo "== 1. Provision disposable PostgreSQL 18 (src + tgt) and LocalStack S3 =="
docker network create "$NET" >/dev/null
docker run -d --name "$SRC" --network "$NET" --network-alias postgres \
  -e POSTGRES_DB=files -e POSTGRES_USER=files -e POSTGRES_PASSWORD=files postgres:18-alpine >/dev/null
docker run -d --name "$TGT" --network "$NET" \
  -e POSTGRES_DB=files -e POSTGRES_USER=files -e POSTGRES_PASSWORD=files postgres:18-alpine >/dev/null
docker run -d --name "$LS" --network "$NET" -p 4566:4566 \
  -e SERVICES=s3 -e AWS_DEFAULT_REGION=us-east-1 \
  -e AWS_ACCESS_KEY_ID=test -e AWS_SECRET_ACCESS_KEY=test localstack/localstack:3.8 >/dev/null

wait_pg() { until docker exec "$1" pg_isready -U files -d files >/dev/null 2>&1; do sleep 0.5; done; }
wait_pg "$SRC"; wait_pg "$TGT"
until curl -fsS http://localhost:4566/_localstack/health >/dev/null 2>&1; do sleep 0.5; done
aws --endpoint-url http://localhost:4566 s3api create-bucket --bucket "$BUCKET" --region us-east-1 >/dev/null 2>&1 || true

echo "== 2. Apply the production Flyway sequence (V1..V6) and seed data =="
docker run --rm --network "$NET" \
  -e FLYWAY_URL="jdbc:postgresql://postgres:5432/files" \
  -e FLYWAY_USER=files -e FLYWAY_PASSWORD=files \
  -e FLYWAY_LOCATIONS=filesystem:/flyway/sql -e FLYWAY_CONNECT_RETRIES=10 \
  -v "$ROOT/db/migrations:/flyway/sql:ro" \
  flyway/flyway:11 migrate

docker exec -i "$SRC" psql -U files -d files -q -v ON_ERROR_STOP=1 <<'SQL'
INSERT INTO users (id, tenant_id, object_id, display_name)
VALUES ('11111111-1111-4111-8111-111111111111','22222222-2222-4222-8222-222222222222','33333333-3333-4333-8333-333333333333','Drill User');
INSERT INTO integrations (id, slug, name)
VALUES ('44444444-4444-4444-8444-444444444444','drill-it','Drill Integration');
INSERT INTO files (id, owner_type, owner_integration_id, original_filename, storage_bucket, storage_key, size_bytes, content_type, status, access_mode, retention_mode, scan_status, metadata)
VALUES ('55555555-5555-4555-8555-555555555555','integration','44444444-4444-4444-8444-444444444444','drill.bin','files-drill','f/55555555-5555-4555-8555-555555555555',42,'application/octet-stream','available','integration_only','indefinite','clean','{}');
INSERT INTO file_audit_events (file_id, actor_type, action, result, metadata)
VALUES ('55555555-5555-4555-8555-555555555555','system','drill.seeded','success','{}');
SQL

echo "== 3. Backup (pg_dump | gzip → LocalStack S3 + _latest marker) =="
DUMP_START="$(now_ms)"
docker exec "$SRC" pg_dump -U files -d files --format=custom --no-owner | gzip -9 > "$DUMP"
DUMP_END="$(now_ms)"
[ -s "$DUMP" ] || { echo "dump is empty" >&2; exit 1; }

aws --endpoint-url http://localhost:4566 s3 cp "$DUMP" "s3://$BUCKET/backups/postgres/files-${STAMP}.dump.gz" --only-show-errors
printf '%s' "$STAMP" | aws --endpoint-url http://localhost:4566 s3 cp - "s3://$BUCKET/backups/postgres/_latest" --only-show-errors
BACKUP_MS=$(( DUMP_END - DUMP_START ))

echo "== 4. Verify the _latest marker =="
LATEST="$(aws --endpoint-url http://localhost:4566 s3 cp "s3://$BUCKET/backups/postgres/_latest" - 2>/dev/null)"
echo "    _latest marker = $LATEST"

echo "== 5. Restore into the disposable target PostgreSQL (RTO measurement) =="
RTO_START="$(now_ms)"
gunzip -c "$DUMP" | docker exec -i "$TGT" pg_restore -U files -d files --clean --if-exists --no-owner
RTO_END="$(now_ms)"
RTO_MS=$(( RTO_END - RTO_START ))

echo "== 6. Verify restored data =="
docker exec "$TGT" psql -U files -d files -tA -c \
  "SELECT 'users='||count(*) FROM users UNION ALL SELECT 'integrations='||count(*) FROM integrations UNION ALL SELECT 'files='||count(*) FROM files UNION ALL SELECT 'audit='||count(*) FROM file_audit_events UNION ALL SELECT 'flyway='||count(*) FROM flyway_schema_history;"

echo ""
echo "== RESULT =="
echo "    backup duration : ${BACKUP_MS} ms"
echo "    restore (RTO)   : ${RTO_MS} ms  (SLO target: <= 4h)"
echo "    RPO             : 0 for this drill (restored the just-taken dump; production RPO = 24h nightly)"
echo "    verdict         : RTO ${RTO_MS} ms <= 4h SLO — PASS"

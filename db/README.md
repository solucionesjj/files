# Database migrations

The schema is the canonical, executable data model. DDL lives here, not in EF Core migrations
(ADR-0002). The application's EF Core model must match this schema.

## Tooling

Flyway (community) runs these scripts:

- Locally: `docker compose --profile tools run --rm flyway migrate`
- CI: the same container against an ephemeral PostgreSQL 18 service.
- Deploy: a one-off ECS task using the API image's migration bundle or the Flyway container,
  before the new API tasks roll. Never auto-migrate on API startup in production.

Configuration is in `docker-compose.yml` (`FLYWAY_URL`, `FLYWAY_USER`, `FLYWAY_PASSWORD`) and CI
secrets for deployed environments.

**Extensions:** `V1` runs `CREATE EXTENSION IF NOT EXISTS citext/pg_trgm`. On RDS the migration
role must be allowed to create extensions (or an administrator pre-creates them once with the
master user). Pre-creating is recommended; `IF NOT EXISTS` then makes the migration a no-op.

## Rules

1. **Naming:** `V<n>__snake_case_description.sql` (`V1`, `V2`, …). Never renumber.
2. **Forward-only:** applied migrations are immutable; Flyway checksums enforce this. Fix mistakes
   with a new migration.
3. **Expand/contract** for breaking changes:
   - Release N: add nullable columns/tables, backfill, dual-write, deploy.
   - Release N+1: stop reading old columns, drop them later.
4. **No destructive change in the same release that stops using the old shape.**
5. **Idempotent seeds:** reference data uses explicit keys/`ON CONFLICT` where safe.
6. **Every migration is tested** against an empty PostgreSQL 18 database in CI, plus a test that
   applies all migrations to a snapshot of the previous version (once production exists).
7. **No `DROP` without a backup/rollback note** in the PR and a runbook step.
8. **Indexes:** add `CREATE INDEX CONCURRENTLY` for large production tables in a dedicated
   migration; it cannot run inside a transaction, so mark that migration `-- flyway:executeInTransaction=false`.

## Files

| Version | Purpose |
|---|---|
| `V1__initial_schema.sql` | All tables, constraints, indexes, triggers. |
| `V2__seed_reference_data.sql` | Active retention/notification policy, email templates, platform settings. |

## Local reset

```bash
docker compose down -v          # destroys local volumes (dev only!)
docker compose up -d postgres
docker compose --profile tools run --rm flyway migrate
```

## Verifying

```bash
docker compose --profile tools run --rm flyway info
make db-verify                  # applies migrations to a fresh throwaway database and asserts counts
```

## Conventions recap

- IDs: `uuid DEFAULT uuidv7()`; never expose another table's numeric ID.
- Enumerations: `text` + `CHECK` (no native enums).
- Time: `timestamptz`, UTC; expiry compared with database `now()`.
- Mutable rows: `created_at`, `updated_at` (trigger), `version` where optimistic concurrency is
  needed (`files`, `users`, `integrations`).
- Sensitive verifiers: `bytea` for hashes; never store plaintext keys/tokens.
- `NULL` semantics: indefinite retention uses `expires_at IS NULL`, never a far-future date.

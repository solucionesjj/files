-- V3 — Identity model per ADR-0014 (2026-10-27)
--
-- Entra ID authenticates humans only. Authorization (user roles) and machine
-- credentials (API keys) live in Files. Private links are deferred, so the
-- download_link_acl/allow_all_tenant objects remain but are unused in v1.
--
-- Forward-only migration; tested from an empty database.

-- 1) User roles live in Files instead of Entra app roles.
ALTER TABLE users
    ADD COLUMN role text NOT NULL DEFAULT 'user';

ALTER TABLE users
    ADD CONSTRAINT ck_users_role CHECK (role IN ('user', 'admin', 'auditor'));

-- The first administrator is bootstrapped by the deployment runbook:
--   UPDATE users SET role = 'admin' WHERE email = '<first-admin-email>';
-- JIT provisioning always creates 'user'.

-- 2) Files-issued API keys for integrations (replaces Entra client credentials).
--    The secret is shown once; only a SHA-256 hash is stored (high-entropy input).
CREATE TABLE integration_api_keys (
    id                  uuid PRIMARY KEY DEFAULT uuidv7(),
    integration_id      uuid NOT NULL REFERENCES integrations(id) ON DELETE CASCADE,
    name                text NOT NULL CHECK (length(btrim(name)) BETWEEN 1 AND 100),
    prefix              text NOT NULL CHECK (prefix ~ '^fsk_[A-Za-z0-9]{8,16}$'),
    secret_hash         bytea NOT NULL CHECK (octet_length(secret_hash) = 32),
    scopes              text[] NOT NULL DEFAULT '{}',
    created_by          uuid REFERENCES users(id),
    created_at          timestamptz NOT NULL DEFAULT now(),
    last_used_at        timestamptz,
    expires_at          timestamptz,
    revoked_at          timestamptz,
    revoked_by          uuid REFERENCES users(id),
    CONSTRAINT uq_integration_api_keys_prefix UNIQUE (prefix),
    CONSTRAINT ck_integration_api_keys_revoked CHECK (
        (revoked_at IS NULL AND revoked_by IS NULL) OR
        (revoked_at IS NOT NULL)
    )
);

CREATE INDEX ix_integration_api_keys_integration ON integration_api_keys (integration_id);

-- 3) integrations.entra_client_id is deprecated: it stays nullable for historical
--    rows and the unique constraint tolerates NULLs; new integrations do not set it.
COMMENT ON COLUMN integrations.entra_client_id IS
    'Deprecated (ADR-0014): Entra client credentials are no longer used for machines; API keys replace them.';

-- Files — initial schema
-- Target: PostgreSQL 18+
-- Managed by Flyway. Forward-only; breaking changes use expand/contract.
-- Conventions: uuidv7() PKs, timestamptz UTC, text + CHECK enums, snake_case.

CREATE EXTENSION IF NOT EXISTS citext;
CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- ---------------------------------------------------------------------------
-- Utility: updated_at maintenance
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION set_updated_at()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.updated_at := now();
    RETURN NEW;
END;
$$;

-- ---------------------------------------------------------------------------
-- users: human identities mapped to Entra (tid, oid)
-- ---------------------------------------------------------------------------
CREATE TABLE users (
    id              uuid PRIMARY KEY DEFAULT uuidv7(),
    tenant_id       uuid NOT NULL,
    object_id       uuid NOT NULL,
    email           citext,
    display_name    text NOT NULL CHECK (length(btrim(display_name)) BETWEEN 1 AND 256),
    status          text NOT NULL DEFAULT 'active'
                    CHECK (status IN ('active', 'disabled')),
    last_login_at   timestamptz,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now(),
    version         integer NOT NULL DEFAULT 1 CHECK (version > 0),
    CONSTRAINT uq_users_tenant_object UNIQUE (tenant_id, object_id)
);

CREATE INDEX ix_users_email ON users (email);

-- ---------------------------------------------------------------------------
-- integrations: machine consumers
-- ---------------------------------------------------------------------------
CREATE TABLE integrations (
    id                          uuid PRIMARY KEY DEFAULT uuidv7(),
    slug                        text NOT NULL
                                CHECK (slug ~ '^[a-z0-9][a-z0-9-]{2,63}$'),
    name                        text NOT NULL CHECK (length(btrim(name)) BETWEEN 1 AND 200),
    description                 text,
    status                      text NOT NULL DEFAULT 'active'
                                CHECK (status IN ('active', 'disabled')),
    entra_client_id             uuid,
    entra_service_principal_id  uuid,
    allowed_ip_cidrs            cidr[] NOT NULL DEFAULT '{}',
    disabled_at                 timestamptz,
    disabled_by                 uuid REFERENCES users(id),
    disabled_reason             text,
    created_by                  uuid REFERENCES users(id),
    created_at                  timestamptz NOT NULL DEFAULT now(),
    updated_at                  timestamptz NOT NULL DEFAULT now(),
    version                     integer NOT NULL DEFAULT 1 CHECK (version > 0),
    CONSTRAINT uq_integrations_slug UNIQUE (slug),
    CONSTRAINT uq_integrations_entra_client UNIQUE (entra_client_id),
    CONSTRAINT ck_integrations_disabled CHECK (
        (status = 'disabled' AND disabled_at IS NOT NULL) OR
        (status = 'active' AND disabled_at IS NULL)
    )
);

CREATE TABLE integration_permissions (
    integration_id  uuid NOT NULL REFERENCES integrations(id) ON DELETE CASCADE,
    permission      text NOT NULL
                    CHECK (permission IN ('file.write', 'file.read', 'file.delete')),
    created_at      timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (integration_id, permission)
);

CREATE TABLE integration_credentials (
    id                  uuid PRIMARY KEY DEFAULT uuidv7(),
    integration_id      uuid NOT NULL REFERENCES integrations(id) ON DELETE CASCADE,
    credential_type     text NOT NULL CHECK (credential_type IN ('api_key', 'entra_client')),
    name                text NOT NULL CHECK (length(btrim(name)) BETWEEN 1 AND 120),
    key_id              text UNIQUE CHECK (key_id IS NULL OR key_id ~ '^[a-z0-9]{12}$'),
    key_prefix          text,
    secret_hash         bytea CHECK (secret_hash IS NULL OR octet_length(secret_hash) = 32),
    hash_algorithm      text NOT NULL DEFAULT 'hmac-sha256'
                        CHECK (hash_algorithm IN ('hmac-sha256', 'none')),
    hash_key_version    smallint NOT NULL DEFAULT 1 CHECK (hash_key_version > 0),
    entra_client_id     uuid,
    expires_at          timestamptz,
    last_used_at        timestamptz,
    rotated_from_id     uuid REFERENCES integration_credentials(id),
    revoked_at          timestamptz,
    revoked_by          uuid REFERENCES users(id),
    revoked_reason      text,
    created_by          uuid REFERENCES users(id),
    created_at          timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_credentials_shape CHECK (
        (credential_type = 'api_key' AND key_id IS NOT NULL AND secret_hash IS NOT NULL
            AND hash_algorithm = 'hmac-sha256' AND entra_client_id IS NULL) OR
        (credential_type = 'entra_client' AND entra_client_id IS NOT NULL
            AND key_id IS NULL AND secret_hash IS NULL AND hash_algorithm = 'none')
    )
);

CREATE INDEX ix_credentials_integration ON integration_credentials (integration_id);
CREATE INDEX ix_credentials_active
    ON integration_credentials (integration_id, expires_at)
    WHERE revoked_at IS NULL;

-- Explicit read-only access to a file owned by another integration.
CREATE TABLE integration_file_grants (
    id              uuid PRIMARY KEY DEFAULT uuidv7(),
    file_id         uuid NOT NULL,
    integration_id  uuid NOT NULL REFERENCES integrations(id) ON DELETE CASCADE,
    granted_by      uuid REFERENCES users(id),
    expires_at      timestamptz,
    revoked_at      timestamptz,
    revoked_by      uuid REFERENCES users(id),
    created_at      timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX uq_integration_file_grants_active
    ON integration_file_grants (file_id, integration_id)
    WHERE revoked_at IS NULL;
CREATE INDEX ix_integration_file_grants_integration
    ON integration_file_grants (integration_id)
    WHERE revoked_at IS NULL;

-- ---------------------------------------------------------------------------
-- Policies and templates
-- ---------------------------------------------------------------------------
CREATE TABLE retention_policies (
    id                      uuid PRIMARY KEY DEFAULT uuidv7(),
    default_days            integer NOT NULL CHECK (default_days BETWEEN 1 AND 36500),
    max_days                integer NOT NULL CHECK (max_days BETWEEN 1 AND 36500),
    allow_user_override     boolean NOT NULL DEFAULT true,
    allow_indefinite        boolean NOT NULL DEFAULT false,
    public_links_enabled    boolean NOT NULL DEFAULT true,
    is_active               boolean NOT NULL DEFAULT true,
    effective_from          timestamptz NOT NULL DEFAULT now(),
    notes                   text,
    created_by              uuid REFERENCES users(id),
    created_at              timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_retention_max_ge_default CHECK (max_days >= default_days)
);

CREATE UNIQUE INDEX uq_retention_policies_active
    ON retention_policies ((is_active))
    WHERE is_active;

CREATE TABLE notification_policies (
    id               uuid PRIMARY KEY DEFAULT uuidv7(),
    name             text NOT NULL CHECK (length(btrim(name)) BETWEEN 1 AND 120),
    intervals_days   integer[] NOT NULL DEFAULT '{7,3,1}',
    template_key     text NOT NULL DEFAULT 'file.expiring',
    is_active        boolean NOT NULL DEFAULT true,
    effective_from   timestamptz NOT NULL DEFAULT now(),
    created_by       uuid REFERENCES users(id),
    created_at       timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_notification_intervals CHECK (
        cardinality(intervals_days) BETWEEN 1 AND 10
        AND NOT (0 = ANY (intervals_days))
        AND array_position(intervals_days, NULL) IS NULL
    )
);

CREATE UNIQUE INDEX uq_notification_policies_active
    ON notification_policies ((is_active))
    WHERE is_active;

CREATE TABLE email_templates (
    id          uuid PRIMARY KEY DEFAULT uuidv7(),
    key         text NOT NULL CHECK (key ~ '^[a-z0-9._-]{3,80}$'),
    locale      text NOT NULL DEFAULT 'es-CO' CHECK (locale ~ '^[a-z]{2}-[A-Z]{2}$'),
    subject     text NOT NULL CHECK (length(subject) BETWEEN 1 AND 300),
    body_html   text NOT NULL,
    body_text   text NOT NULL,
    version     integer NOT NULL DEFAULT 1 CHECK (version > 0),
    is_active   boolean NOT NULL DEFAULT true,
    created_by  uuid REFERENCES users(id),
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_email_templates_version UNIQUE (key, locale, version)
);

CREATE UNIQUE INDEX uq_email_templates_active
    ON email_templates (key, locale)
    WHERE is_active;

-- ---------------------------------------------------------------------------
-- files
-- ---------------------------------------------------------------------------
CREATE TABLE files (
    id                      uuid PRIMARY KEY DEFAULT uuidv7(),
    owner_type              text NOT NULL CHECK (owner_type IN ('human', 'integration')),
    owner_user_id           uuid REFERENCES users(id),
    owner_integration_id    uuid REFERENCES integrations(id),
    original_filename       text NOT NULL
                            CHECK (length(original_filename) BETWEEN 1 AND 512),
    storage_provider        text NOT NULL DEFAULT 's3'
                            CHECK (storage_provider IN ('s3', 'minio')),
    storage_bucket          text NOT NULL CHECK (length(storage_bucket) BETWEEN 3 AND 255),
    storage_key             text NOT NULL UNIQUE
                            CHECK (length(storage_key) BETWEEN 1 AND 1024
                                   AND storage_key !~ '^/'),
    size_bytes              bigint NOT NULL CHECK (size_bytes >= 0),
    content_type            text NOT NULL CHECK (length(content_type) BETWEEN 3 AND 255),
    content_type_source     text NOT NULL DEFAULT 'declared'
                            CHECK (content_type_source IN ('declared', 'sniffed', 'verified')),
    checksum_sha256         text CHECK (checksum_sha256 IS NULL
                                       OR checksum_sha256 ~ '^[a-f0-9]{64}$'),
    status                  text NOT NULL DEFAULT 'pending_upload'
                            CHECK (status IN ('pending_upload', 'processing', 'available',
                                              'quarantined', 'expired', 'pending_deletion',
                                              'deleted', 'upload_failed')),
    access_mode             text NOT NULL DEFAULT 'owner_only'
                            CHECK (access_mode IN ('owner_only', 'private_link',
                                                   'public_link', 'integration_only')),
    retention_mode          text NOT NULL CHECK (retention_mode IN ('finite', 'indefinite')),
    expires_at              timestamptz,
    retention_days          integer CHECK (retention_days IS NULL OR retention_days > 0),
    retention_policy_id     uuid REFERENCES retention_policies(id),
    scan_status             text NOT NULL DEFAULT 'not_scanned'
                            CHECK (scan_status IN ('not_scanned', 'pending', 'clean',
                                                   'infected', 'failed')),
    scan_completed_at       timestamptz,
    quarantine_reason       text,
    legal_hold              boolean NOT NULL DEFAULT false,
    successful_downloads    bigint NOT NULL DEFAULT 0 CHECK (successful_downloads >= 0),
    last_download_at        timestamptz,
    metadata                jsonb NOT NULL DEFAULT '{}'::jsonb,
    available_at            timestamptz,
    deletion_requested_by_type text CHECK (deletion_requested_by_type IS NULL
                                          OR deletion_requested_by_type IN ('user', 'integration',
                                                                            'admin', 'system')),
    deletion_requested_by   uuid,
    deletion_reason         text,
    deletion_attempts       integer NOT NULL DEFAULT 0 CHECK (deletion_attempts >= 0),
    next_deletion_at        timestamptz,
    last_deletion_error     text,
    deleted_at              timestamptz,
    created_at              timestamptz NOT NULL DEFAULT now(),
    updated_at              timestamptz NOT NULL DEFAULT now(),
    version                 integer NOT NULL DEFAULT 1 CHECK (version > 0),
    CONSTRAINT ck_files_owner CHECK (
        (owner_type = 'human' AND owner_user_id IS NOT NULL AND owner_integration_id IS NULL) OR
        (owner_type = 'integration' AND owner_integration_id IS NOT NULL AND owner_user_id IS NULL)
    ),
    CONSTRAINT ck_files_retention CHECK (
        (retention_mode = 'finite' AND expires_at IS NOT NULL) OR
        (retention_mode = 'indefinite' AND expires_at IS NULL)
    ),
    CONSTRAINT ck_files_deleted CHECK ((status = 'deleted') = (deleted_at IS NOT NULL)),
    CONSTRAINT ck_files_access_mode_owner CHECK (
        (owner_type = 'human' AND access_mode <> 'integration_only') OR
        (owner_type = 'integration' AND access_mode = 'integration_only')
    )
);

CREATE INDEX ix_files_owner_user ON files (owner_user_id, created_at DESC)
    WHERE owner_user_id IS NOT NULL;
CREATE INDEX ix_files_owner_integration ON files (owner_integration_id, created_at DESC)
    WHERE owner_integration_id IS NOT NULL;
CREATE INDEX ix_files_expiry ON files (expires_at)
    WHERE status = 'available' AND expires_at IS NOT NULL;
CREATE INDEX ix_files_status ON files (status);
CREATE INDEX ix_files_filename_trgm ON files USING gin (original_filename gin_trgm_ops);
CREATE INDEX ix_files_pending_deletion ON files (next_deletion_at)
    WHERE status = 'pending_deletion';
CREATE INDEX ix_files_scan_pending ON files (created_at)
    WHERE scan_status = 'pending';

-- ---------------------------------------------------------------------------
-- tags
-- ---------------------------------------------------------------------------
CREATE TABLE tags (
    id              uuid PRIMARY KEY DEFAULT uuidv7(),
    scope           text NOT NULL DEFAULT 'global' CHECK (scope IN ('global', 'user')),
    owner_user_id   uuid REFERENCES users(id),
    name            citext NOT NULL CHECK (length(btrim(name)) BETWEEN 1 AND 64),
    created_by      uuid REFERENCES users(id),
    created_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_tags_scope CHECK (
        (scope = 'global' AND owner_user_id IS NULL) OR
        (scope = 'user' AND owner_user_id IS NOT NULL)
    ),
    CONSTRAINT uq_tags_scope_name UNIQUE NULLS NOT DISTINCT (scope, owner_user_id, name)
);

CREATE TABLE file_tags (
    file_id     uuid NOT NULL REFERENCES files(id) ON DELETE CASCADE,
    tag_id      uuid NOT NULL REFERENCES tags(id) ON DELETE CASCADE,
    created_by  uuid REFERENCES users(id),
    created_at  timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (file_id, tag_id)
);

CREATE INDEX ix_file_tags_tag ON file_tags (tag_id, file_id);

-- ---------------------------------------------------------------------------
-- upload sessions
-- ---------------------------------------------------------------------------
CREATE TABLE upload_sessions (
    id                      uuid PRIMARY KEY DEFAULT uuidv7(),
    file_id                 uuid NOT NULL UNIQUE REFERENCES files(id) ON DELETE CASCADE,
    initiator_type          text NOT NULL CHECK (initiator_type IN ('user', 'integration')),
    initiator_id            uuid NOT NULL,
    status                  text NOT NULL DEFAULT 'initiated'
                            CHECK (status IN ('initiated', 'uploading', 'received',
                                              'committing', 'completed', 'failed',
                                              'expired', 'aborted')),
    expected_size_bytes     bigint NOT NULL CHECK (expected_size_bytes >= 0),
    expected_content_type   text NOT NULL,
    expected_checksum_sha256 text CHECK (expected_checksum_sha256 IS NULL
                                        OR expected_checksum_sha256 ~ '^[a-f0-9]{64}$'),
    chunk_size_bytes        bigint NOT NULL CHECK (chunk_size_bytes > 0),
    chunk_count             integer NOT NULL CHECK (chunk_count BETWEEN 1 AND 10000),
    received_bytes          bigint NOT NULL DEFAULT 0
                            CHECK (received_bytes >= 0 AND received_bytes <= expected_size_bytes),
    staging_path            text NOT NULL UNIQUE,
    s3_upload_id            text,
    s3_part_size_bytes      bigint CHECK (s3_part_size_bytes IS NULL OR s3_part_size_bytes > 0),
    expires_at              timestamptz NOT NULL,
    completed_at            timestamptz,
    last_error              text,
    created_at              timestamptz NOT NULL DEFAULT now(),
    updated_at              timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_upload_sessions_completed CHECK (
        (status = 'completed' AND completed_at IS NOT NULL) OR
        (status <> 'completed')
    )
);

CREATE INDEX ix_upload_sessions_open ON upload_sessions (expires_at)
    WHERE status IN ('initiated', 'uploading', 'received', 'committing');
CREATE INDEX ix_upload_sessions_cleanup ON upload_sessions (updated_at)
    WHERE status IN ('failed', 'expired', 'aborted');

CREATE TABLE upload_chunks (
    upload_session_id   uuid NOT NULL REFERENCES upload_sessions(id) ON DELETE CASCADE,
    chunk_number        integer NOT NULL CHECK (chunk_number BETWEEN 1 AND 10000),
    offset_bytes        bigint NOT NULL CHECK (offset_bytes >= 0),
    size_bytes          bigint NOT NULL CHECK (size_bytes >= 0),
    checksum_sha256     text CHECK (checksum_sha256 IS NULL
                                    OR checksum_sha256 ~ '^[a-f0-9]{64}$'),
    received_at         timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (upload_session_id, chunk_number)
);

-- ---------------------------------------------------------------------------
-- download links, grants, abuse counters
-- ---------------------------------------------------------------------------
CREATE TABLE download_links (
    id                      uuid PRIMARY KEY DEFAULT uuidv7(),
    file_id                 uuid NOT NULL REFERENCES files(id) ON DELETE CASCADE,
    token_hash              bytea NOT NULL UNIQUE CHECK (octet_length(token_hash) = 32),
    access_mode             text NOT NULL CHECK (access_mode IN ('public', 'private')),
    download_key_hash       text CHECK (download_key_hash IS NULL
                                        OR length(download_key_hash) BETWEEN 20 AND 512),
    download_key_version    smallint CHECK (download_key_version IS NULL
                                            OR download_key_version > 0),
    status                  text NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active', 'revoked', 'expired')),
    expires_at              timestamptz,
    max_downloads           integer CHECK (max_downloads IS NULL OR max_downloads > 0),
    successful_downloads    integer NOT NULL DEFAULT 0 CHECK (successful_downloads >= 0),
    reserved_downloads      integer NOT NULL DEFAULT 0 CHECK (reserved_downloads >= 0),
    created_by_type         text NOT NULL CHECK (created_by_type IN ('user', 'admin')),
    created_by              uuid NOT NULL REFERENCES users(id),
    revoked_at              timestamptz,
    revoked_by              uuid REFERENCES users(id),
    revoked_reason          text,
    created_at              timestamptz NOT NULL DEFAULT now(),
    updated_at              timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_links_key_shape CHECK (
        (access_mode = 'public' AND download_key_hash IS NOT NULL
            AND download_key_version IS NOT NULL) OR
        (access_mode = 'private' AND download_key_hash IS NULL
            AND download_key_version IS NULL)
    ),
    CONSTRAINT ck_links_status CHECK (
        (status = 'revoked' AND revoked_at IS NOT NULL) OR
        (status <> 'revoked')
    ),
    CONSTRAINT ck_links_limit CHECK (
        max_downloads IS NULL OR
        successful_downloads + reserved_downloads <= max_downloads
    )
);

CREATE INDEX ix_links_file ON download_links (file_id, created_at DESC);
CREATE INDEX ix_links_active_expiry ON download_links (expires_at)
    WHERE status = 'active';

CREATE TABLE download_grants (
    id                  uuid PRIMARY KEY DEFAULT uuidv7(),
    file_id             uuid NOT NULL REFERENCES files(id) ON DELETE CASCADE,
    link_id             uuid REFERENCES download_links(id) ON DELETE SET NULL,
    grant_token_hash    bytea NOT NULL UNIQUE CHECK (octet_length(grant_token_hash) = 32),
    source              text NOT NULL CHECK (source IN ('owner', 'private_link', 'public_link')),
    issued_to_user_id   uuid REFERENCES users(id),
    issued_ip_hash      text,
    state               text NOT NULL DEFAULT 'issued'
                        CHECK (state IN ('issued', 'redeemed', 'completed',
                                         'expired', 'failed')),
    reserved_slot       boolean NOT NULL DEFAULT false,
    expires_at          timestamptz NOT NULL,
    redeemed_at         timestamptz,
    completed_at        timestamptz,
    bytes_sent          bigint NOT NULL DEFAULT 0 CHECK (bytes_sent >= 0),
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_grants_link_source CHECK (
        (source = 'owner' AND link_id IS NULL) OR
        (source <> 'owner' AND link_id IS NOT NULL)
    )
);

CREATE INDEX ix_grants_open ON download_grants (expires_at)
    WHERE state IN ('issued', 'redeemed');
CREATE INDEX ix_grants_link ON download_grants (link_id);

CREATE TABLE download_key_attempts (
    id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    link_id         uuid NOT NULL REFERENCES download_links(id) ON DELETE CASCADE,
    ip_hash         text NOT NULL,
    outcome         text NOT NULL CHECK (outcome IN ('failed', 'locked_out')),
    attempted_at    timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_key_attempts_link_ip ON download_key_attempts (link_id, ip_hash, attempted_at DESC);
CREATE INDEX ix_key_attempts_link_time ON download_key_attempts (link_id, attempted_at DESC);

CREATE TABLE abuse_lockouts (
    id              uuid PRIMARY KEY DEFAULT uuidv7(),
    scope_type      text NOT NULL CHECK (scope_type IN ('link_ip', 'link')),
    scope_key       text NOT NULL,
    locked_until    timestamptz NOT NULL,
    reason          text,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_abuse_lockouts_scope UNIQUE (scope_type, scope_key)
);

-- ---------------------------------------------------------------------------
-- notifications
-- ---------------------------------------------------------------------------
CREATE TABLE notification_events (
    id                  uuid PRIMARY KEY DEFAULT uuidv7(),
    file_id             uuid NOT NULL REFERENCES files(id) ON DELETE CASCADE,
    policy_id           uuid NOT NULL REFERENCES notification_policies(id),
    interval_days       integer NOT NULL CHECK (interval_days > 0),
    expiration_at       timestamptz NOT NULL,
    recipient_email     citext,
    status              text NOT NULL DEFAULT 'scheduled'
                        CHECK (status IN ('scheduled', 'sending', 'accepted', 'delivered',
                                          'bounced', 'failed', 'suppressed', 'superseded')),
    attempts            integer NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    next_attempt_at     timestamptz,
    provider_message_id text,
    last_error          text,
    scheduled_at        timestamptz NOT NULL DEFAULT now(),
    sent_at             timestamptz,
    delivered_at        timestamptz,
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_notification_schedule UNIQUE (file_id, interval_days, expiration_at)
);

CREATE INDEX ix_notifications_pending ON notification_events (next_attempt_at)
    WHERE status IN ('scheduled', 'failed');
CREATE INDEX ix_notifications_file ON notification_events (file_id, created_at DESC);

CREATE TABLE notification_delivery_attempts (
    id                      bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    notification_event_id   uuid NOT NULL REFERENCES notification_events(id) ON DELETE CASCADE,
    attempt_number          integer NOT NULL CHECK (attempt_number > 0),
    provider                text NOT NULL DEFAULT 'graph',
    status                  text NOT NULL CHECK (status IN ('accepted', 'failed', 'bounced',
                                                           'delivered')),
    provider_message_id     text,
    error                   text,
    created_at              timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_notification_attempt UNIQUE (notification_event_id, attempt_number)
);

-- ---------------------------------------------------------------------------
-- audit
-- ---------------------------------------------------------------------------
CREATE TABLE file_audit_events (
    id              uuid PRIMARY KEY DEFAULT uuidv7(),
    file_id         uuid REFERENCES files(id) ON DELETE SET NULL,
    actor_type      text NOT NULL CHECK (actor_type IN ('user', 'integration', 'admin', 'system')),
    actor_id        uuid,
    action          text NOT NULL CHECK (length(action) BETWEEN 3 AND 80),
    result          text NOT NULL CHECK (result IN ('success', 'failure')),
    reason          text,
    metadata        jsonb NOT NULL DEFAULT '{}'::jsonb,
    ip_hash         text,
    trace_id        text,
    occurred_at     timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_file_audit_file ON file_audit_events (file_id, occurred_at DESC);
CREATE INDEX ix_file_audit_time ON file_audit_events (occurred_at DESC);
CREATE INDEX ix_file_audit_actor ON file_audit_events (actor_type, actor_id, occurred_at DESC);

CREATE TABLE download_events (
    id              uuid PRIMARY KEY DEFAULT uuidv7(),
    file_id         uuid REFERENCES files(id) ON DELETE SET NULL,
    link_id         uuid REFERENCES download_links(id) ON DELETE SET NULL,
    grant_id        uuid REFERENCES download_grants(id) ON DELETE SET NULL,
    actor_type      text NOT NULL CHECK (actor_type IN ('user', 'integration', 'anonymous', 'system')),
    actor_id        uuid,
    source          text NOT NULL CHECK (source IN ('owner', 'private_link', 'public_link',
                                                   'integration')),
    result          text NOT NULL CHECK (result IN ('authorized', 'denied', 'completed', 'failed')),
    reason_code     text,
    ip_hash         text,
    user_agent      text,
    bytes_sent      bigint NOT NULL DEFAULT 0 CHECK (bytes_sent >= 0),
    trace_id        text,
    occurred_at     timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_download_events_file ON download_events (file_id, occurred_at DESC);
CREATE INDEX ix_download_events_link ON download_events (link_id, occurred_at DESC);
CREATE INDEX ix_download_events_time ON download_events (occurred_at DESC);
CREATE INDEX ix_download_events_denied ON download_events (occurred_at DESC)
    WHERE result = 'denied';

CREATE TABLE admin_audit_events (
    id              uuid PRIMARY KEY DEFAULT uuidv7(),
    actor_user_id   uuid REFERENCES users(id) ON DELETE SET NULL,
    actor_role      text,
    action          text NOT NULL CHECK (length(action) BETWEEN 3 AND 80),
    target_type     text NOT NULL,
    target_id       uuid,
    result          text NOT NULL CHECK (result IN ('success', 'failure')),
    reason          text,
    metadata        jsonb NOT NULL DEFAULT '{}'::jsonb,
    ip_hash         text,
    trace_id        text,
    occurred_at     timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_admin_audit_time ON admin_audit_events (occurred_at DESC);
CREATE INDEX ix_admin_audit_target ON admin_audit_events (target_type, target_id);

-- ---------------------------------------------------------------------------
-- operational tables
-- ---------------------------------------------------------------------------
CREATE TABLE retention_migrations (
    id                  uuid PRIMARY KEY DEFAULT uuidv7(),
    status              text NOT NULL DEFAULT 'pending'
                        CHECK (status IN ('pending', 'running', 'completed', 'failed', 'cancelled')),
    target_policy_id    uuid NOT NULL REFERENCES retention_policies(id),
    scope               jsonb NOT NULL DEFAULT '{}'::jsonb,
    reason              text NOT NULL CHECK (length(btrim(reason)) >= 10),
    total_files         bigint NOT NULL DEFAULT 0 CHECK (total_files >= 0),
    processed_files     bigint NOT NULL DEFAULT 0 CHECK (processed_files >= 0),
    failed_files        bigint NOT NULL DEFAULT 0 CHECK (failed_files >= 0),
    last_error          text,
    requested_by        uuid NOT NULL REFERENCES users(id),
    created_at          timestamptz NOT NULL DEFAULT now(),
    started_at          timestamptz,
    completed_at        timestamptz,
    updated_at          timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE idempotency_records (
    id                  uuid PRIMARY KEY DEFAULT uuidv7(),
    scope_type          text NOT NULL CHECK (scope_type IN ('integration', 'user')),
    scope_id            uuid NOT NULL,
    idempotency_key     text NOT NULL CHECK (length(idempotency_key) BETWEEN 8 AND 128),
    request_hash        text NOT NULL CHECK (request_hash ~ '^[a-f0-9]{64}$'),
    state               text NOT NULL DEFAULT 'in_progress'
                        CHECK (state IN ('in_progress', 'completed')),
    response_status     integer,
    response_body       jsonb,
    response_headers    jsonb,
    resource_id         uuid,
    created_at          timestamptz NOT NULL DEFAULT now(),
    completed_at        timestamptz,
    expires_at          timestamptz NOT NULL,
    CONSTRAINT uq_idempotency_key UNIQUE (scope_type, scope_id, idempotency_key)
);

CREATE INDEX ix_idempotency_expiry ON idempotency_records (expires_at);

CREATE TABLE outbox_messages (
    id              uuid PRIMARY KEY DEFAULT uuidv7(),
    event_type      text NOT NULL,
    aggregate_type  text NOT NULL,
    aggregate_id    uuid NOT NULL,
    payload         jsonb NOT NULL,
    state           text NOT NULL DEFAULT 'pending'
                    CHECK (state IN ('pending', 'processing', 'processed', 'dead')),
    attempts        integer NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    available_at    timestamptz NOT NULL DEFAULT now(),
    claimed_at      timestamptz,
    processed_at    timestamptz,
    last_error      text,
    created_at      timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_outbox_pending ON outbox_messages (available_at)
    WHERE state IN ('pending', 'processing');
CREATE INDEX ix_outbox_created ON outbox_messages (created_at);

CREATE TABLE daily_usage (
    id              uuid PRIMARY KEY DEFAULT uuidv7(),
    scope_type      text NOT NULL CHECK (scope_type IN ('integration', 'global')),
    scope_id        uuid,
    usage_date      date NOT NULL,
    bytes_uploaded  bigint NOT NULL DEFAULT 0 CHECK (bytes_uploaded >= 0),
    files_created   bigint NOT NULL DEFAULT 0 CHECK (files_created >= 0),
    bytes_downloaded bigint NOT NULL DEFAULT 0 CHECK (bytes_downloaded >= 0),
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_daily_usage UNIQUE NULLS NOT DISTINCT (scope_type, scope_id, usage_date),
    CONSTRAINT ck_daily_usage_scope CHECK (
        (scope_type = 'integration' AND scope_id IS NOT NULL) OR
        (scope_type = 'global' AND scope_id IS NULL)
    )
);

CREATE TABLE platform_settings (
    key         text PRIMARY KEY CHECK (key ~ '^[a-z0-9._-]{3,80}$'),
    value       jsonb NOT NULL,
    description text,
    updated_by  uuid REFERENCES users(id),
    updated_at  timestamptz NOT NULL DEFAULT now()
);

-- ---------------------------------------------------------------------------
-- updated_at triggers
-- ---------------------------------------------------------------------------
DO $$
DECLARE
    t text;
BEGIN
    FOREACH t IN ARRAY ARRAY[
        'users', 'integrations', 'files', 'upload_sessions', 'download_links',
        'download_grants', 'abuse_lockouts', 'notification_events', 'email_templates',
        'daily_usage', 'retention_migrations', 'platform_settings'
    ]
    LOOP
        EXECUTE format(
            'CREATE TRIGGER trg_%1$s_updated_at BEFORE UPDATE ON %1$I
             FOR EACH ROW EXECUTE FUNCTION set_updated_at()',
            t
        );
    END LOOP;
END;
$$;

-- ---------------------------------------------------------------------------
-- Foreign key added after files exists
-- ---------------------------------------------------------------------------
ALTER TABLE integration_file_grants
    ADD CONSTRAINT fk_integration_file_grants_file
    FOREIGN KEY (file_id) REFERENCES files(id) ON DELETE CASCADE;

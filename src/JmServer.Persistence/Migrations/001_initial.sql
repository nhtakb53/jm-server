CREATE TABLE jm.accounts (
    account_id uuid PRIMARY KEY,
    username text NOT NULL,
    username_normalized text NOT NULL UNIQUE,
    created_at timestamptz NOT NULL
);

CREATE TABLE jm.devices (
    device_id uuid PRIMARY KEY,
    account_id uuid NOT NULL REFERENCES jm.accounts(account_id) ON DELETE CASCADE,
    display_name text NOT NULL,
    token_hash bytea NOT NULL,
    revoked_at timestamptz NULL,
    created_at timestamptz NOT NULL,
    last_authenticated_at timestamptz NULL
);

CREATE TABLE jm.characters (
    character_id uuid PRIMARY KEY,
    account_id uuid NOT NULL REFERENCES jm.accounts(account_id) ON DELETE CASCADE,
    name text NOT NULL,
    character_class text NOT NULL,
    save_data bytea NOT NULL,
    save_sha256 bytea NOT NULL,
    revision bigint NOT NULL DEFAULT 0 CHECK (revision >= 0),
    lease_id uuid NULL,
    leased_device_id uuid NULL REFERENCES jm.devices(device_id),
    lease_expires_at timestamptz NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    UNIQUE (account_id, name)
);

CREATE INDEX ix_characters_account_id ON jm.characters(account_id);
CREATE INDEX ix_characters_lease_expires_at ON jm.characters(lease_expires_at)
    WHERE lease_id IS NOT NULL;

CREATE TABLE jm.character_versions (
    character_version_id uuid PRIMARY KEY,
    character_id uuid NOT NULL REFERENCES jm.characters(character_id) ON DELETE CASCADE,
    revision bigint NOT NULL CHECK (revision >= 0),
    save_data bytea NOT NULL,
    save_sha256 bytea NOT NULL,
    created_by_device_id uuid NULL REFERENCES jm.devices(device_id),
    created_at timestamptz NOT NULL,
    UNIQUE (character_id, revision)
);

CREATE TABLE jm.audit_events (
    audit_event_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    account_id uuid NULL,
    device_id uuid NULL,
    character_id uuid NULL,
    event_type text NOT NULL,
    details jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL
);

CREATE INDEX ix_audit_events_character_id_created_at
    ON jm.audit_events(character_id, created_at DESC);

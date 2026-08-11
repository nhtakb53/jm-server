CREATE TABLE jm.account_vaults (
    account_id uuid PRIMARY KEY REFERENCES jm.accounts(account_id) ON DELETE CASCADE,
    revision bigint NOT NULL DEFAULT 0 CHECK (revision >= 0),
    lease_id uuid NULL,
    leased_device_id uuid NULL REFERENCES jm.devices(device_id),
    lease_expires_at timestamptz NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL
);

CREATE INDEX ix_account_vaults_lease_expires_at
    ON jm.account_vaults(lease_expires_at)
    WHERE lease_id IS NOT NULL;

CREATE TABLE jm.account_vault_files (
    account_id uuid NOT NULL REFERENCES jm.account_vaults(account_id) ON DELETE CASCADE,
    relative_path text NOT NULL,
    file_data bytea NOT NULL,
    file_sha256 bytea NOT NULL,
    updated_at timestamptz NOT NULL,
    PRIMARY KEY (account_id, relative_path),
    CHECK (relative_path <> '')
);

CREATE TABLE jm.account_vault_versions (
    vault_version_id uuid PRIMARY KEY,
    account_id uuid NOT NULL REFERENCES jm.account_vaults(account_id) ON DELETE CASCADE,
    revision bigint NOT NULL CHECK (revision >= 0),
    bundle_data bytea NOT NULL,
    bundle_sha256 bytea NOT NULL,
    created_by_device_id uuid NULL REFERENCES jm.devices(device_id),
    created_at timestamptz NOT NULL,
    UNIQUE (account_id, revision)
);

INSERT INTO jm.account_vaults (account_id, created_at, updated_at)
SELECT account_id, created_at, created_at
FROM jm.accounts;

INSERT INTO jm.account_vault_files (
    account_id, relative_path, file_data, file_sha256, updated_at)
SELECT account_id, name || '.d2s', save_data, save_sha256, updated_at
FROM jm.characters;

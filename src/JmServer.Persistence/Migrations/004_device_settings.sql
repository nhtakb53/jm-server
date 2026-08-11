CREATE TABLE jm.device_settings (
    device_id uuid PRIMARY KEY REFERENCES jm.devices(device_id) ON DELETE CASCADE,
    account_id uuid NOT NULL REFERENCES jm.accounts(account_id) ON DELETE CASCADE,
    revision bigint NOT NULL DEFAULT 1 CHECK (revision >= 1),
    settings_data bytea NOT NULL,
    settings_sha256 bytea NOT NULL,
    updated_at timestamptz NOT NULL,
    CHECK (octet_length(settings_data) BETWEEN 1 AND 262144),
    CHECK (octet_length(settings_sha256) = 32)
);

CREATE INDEX ix_device_settings_account_id
    ON jm.device_settings(account_id);

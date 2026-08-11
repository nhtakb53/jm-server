ALTER TABLE jm.characters
    ADD COLUMN deleted_at timestamptz NULL;

CREATE INDEX ix_characters_account_deleted_at
    ON jm.characters(account_id, deleted_at);

CREATE TABLE jm.character_trash_files (
    character_id uuid NOT NULL REFERENCES jm.characters(character_id) ON DELETE CASCADE,
    relative_path text NOT NULL,
    file_data bytea NOT NULL,
    file_sha256 bytea NOT NULL,
    deleted_at timestamptz NOT NULL,
    PRIMARY KEY (character_id, relative_path),
    CHECK (relative_path <> '')
);

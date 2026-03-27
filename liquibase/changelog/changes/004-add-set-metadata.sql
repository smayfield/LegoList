--liquibase formatted sql
--changeset legolist:004-add-set-metadata
CREATE TABLE set_metadata (
    set_number VARCHAR(50) NOT NULL PRIMARY KEY,
    image_url TEXT,
    piece_count INTEGER,
    fetched_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
--rollback DROP TABLE set_metadata;

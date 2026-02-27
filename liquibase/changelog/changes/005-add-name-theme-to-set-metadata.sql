--liquibase formatted sql
--changeset legolist:005-add-name-theme-to-set-metadata
ALTER TABLE set_metadata ADD COLUMN name VARCHAR(500);
ALTER TABLE set_metadata ADD COLUMN theme VARCHAR(255);
--rollback ALTER TABLE set_metadata DROP COLUMN theme;
--rollback ALTER TABLE set_metadata DROP COLUMN name;

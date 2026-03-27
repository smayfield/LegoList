--liquibase formatted sql
--changeset legolist:003-add-user-to-lists
DELETE FROM lego_sets;
DELETE FROM set_lists;
ALTER TABLE set_lists ADD COLUMN user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE;
CREATE INDEX ix_set_lists_user_id ON set_lists(user_id);
--rollback DROP INDEX ix_set_lists_user_id;
--rollback ALTER TABLE set_lists DROP COLUMN user_id;

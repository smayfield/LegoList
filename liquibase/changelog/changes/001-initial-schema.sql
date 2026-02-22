--liquibase formatted sql
--changeset legolist:001-initial-schema
CREATE TABLE set_lists (
    id SERIAL PRIMARY KEY,
    name VARCHAR(255) NOT NULL
);
CREATE TABLE lego_sets (
    id SERIAL PRIMARY KEY,
    set_number VARCHAR(50) NOT NULL,
    theme VARCHAR(100) NOT NULL,
    name VARCHAR(255) NOT NULL,
    quantity INTEGER NOT NULL DEFAULT 1,
    set_list_id INTEGER NOT NULL REFERENCES set_lists(id) ON DELETE CASCADE
);
CREATE INDEX ix_lego_sets_set_list_id ON lego_sets(set_list_id);
--rollback DROP INDEX ix_lego_sets_set_list_id;
--rollback DROP TABLE lego_sets;
--rollback DROP TABLE set_lists;

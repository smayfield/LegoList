--liquibase formatted sql
--changeset legolist:002-add-users
CREATE TABLE users (
    id SERIAL PRIMARY KEY,
    google_sub VARCHAR(255) NOT NULL UNIQUE,
    email VARCHAR(255) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX ix_users_google_sub ON users(google_sub);
--rollback DROP INDEX ix_users_google_sub;
--rollback DROP TABLE users;

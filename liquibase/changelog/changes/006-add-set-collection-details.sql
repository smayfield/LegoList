--liquibase formatted sql

--changeset legolist:006-add-set-collection-details
ALTER TABLE lego_sets ADD COLUMN built BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE lego_sets ADD COLUMN display_location VARCHAR(255);
ALTER TABLE lego_sets ADD COLUMN purchase_price NUMERIC(10,2);
ALTER TABLE lego_sets ADD COLUMN notes TEXT;
--rollback ALTER TABLE lego_sets DROP COLUMN notes;
--rollback ALTER TABLE lego_sets DROP COLUMN purchase_price;
--rollback ALTER TABLE lego_sets DROP COLUMN display_location;
--rollback ALTER TABLE lego_sets DROP COLUMN built;

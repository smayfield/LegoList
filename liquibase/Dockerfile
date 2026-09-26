# Liquibase with this repo's changelog baked in, so each app version ships with
# exactly the schema changes it expects. Build from the liquibase/ directory:
#   docker build -t legolist-migrate liquibase
FROM liquibase/liquibase:4.29.2
COPY changelog /liquibase/changelog

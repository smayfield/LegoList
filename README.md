# LegoList

Track LEGO sets in lists. Blazor Server UI + ASP.NET Core API + PostgreSQL, with the schema managed
by Liquibase. Sign-in is with Google; set details come from Rebrickable.

Production: **https://legolist.magicworld.com**

## Local development

See [CLAUDE.md](CLAUDE.md) for commands. In short:

```powershell
.\setup.ps1   # start PostgreSQL in Docker and apply Liquibase migrations
.\start.ps1   # build, test, and launch the UI
```

Local secrets (Google ClientId/ClientSecret, Rebrickable key) go in `dotnet user-secrets`.

## Changing the database schema

1. Add a SQL file in `liquibase/changelog/changes/` (e.g. `006-something.sql`) using Liquibase
   formatted SQL (`--liquibase formatted sql` / `--changeset legolist:006-something`).
2. Add an `<include>` for it in `liquibase/changelog/db.changelog-root.xml`.
3. Locally: `docker compose up liquibase`. In production, the deploy applies it automatically.

## Deployment

All changes go through a feature branch and a pull request. Merging to `main` runs
`.github/workflows/deploy.yml`, which:

1. runs the tests,
2. builds ARM64 images (`legolist-api`, `legolist-blazor`, and `legolist-migrate`, which is Liquibase
   plus this repo's changelog) and pushes them to ECR, tagged with the commit SHA,
3. uploads `deploy/` to the ops S3 bucket and runs `deploy/deploy.sh` on the server through SSM,
   which pulls the images, **applies Liquibase migrations**, and only then restarts the apps.
   If a migration fails, the previous version keeps running and the workflow fails.

### Production layout

| Piece | Where |
|---|---|
| Server | One EC2 `t4g.small` (Amazon Linux 2023, ARM) running `deploy/docker-compose.prod.yml` |
| HTTPS | Caddy with automatic Let's Encrypt certificates (`deploy/Caddyfile`) |
| Database | PostgreSQL 17 container; data on the server's EBS disk, never exposed publicly |
| Secrets | SSM Parameter Store: `/legolist/db-password`, `google-client-id`, `google-client-secret`, `rebrickable-api-key` |
| Backups | Nightly `pg_dump` to the ops bucket (`backups/`, kept 30 days) + daily EBS snapshots (kept 7) |
| Admin access | SSM Session Manager (no SSH port open): `aws ssm start-session --target <instance-id>` |
| Infrastructure | CloudFormation `infra/legolist.yml`, stack `legolist` in `us-east-1` |

GitHub Actions authenticates with the IAM role `legolist-github-deploy` through OIDC (no stored AWS keys).
Repo variables: `AWS_ROLE_ARN`, `ECR_REGISTRY`, `OPS_BUCKET`, `INSTANCE_ID`.

### Restoring a backup

```sh
aws s3 cp s3://<ops-bucket>/backups/<file>.dump /tmp/legolist.dump
cd /opt/legolist
docker compose -f docker-compose.prod.yml --env-file .env exec -T postgres \
  pg_restore -U legolist -d legolist --clean --if-exists < /tmp/legolist.dump
```

### Updating infrastructure

```sh
aws cloudformation deploy --region us-east-1 --stack-name legolist \
  --template-file infra/legolist.yml --capabilities CAPABILITY_NAMED_IAM
```

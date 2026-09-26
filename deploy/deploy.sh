#!/usr/bin/env bash
# Runs on the EC2 server (via SSM Run Command from GitHub Actions).
# Usage: deploy.sh <image-tag> <ecr-registry> <ops-bucket>
set -euo pipefail

TAG="$1"
REGISTRY="$2"
BUCKET="$3"
REGION="us-east-1"
DIR=/opt/legolist
cd "$DIR"

log() { echo "==> $*"; }

param() {
  aws ssm get-parameter --region "$REGION" --name "/legolist/$1" --with-decryption \
    --query Parameter.Value --output text
}

log "Writing .env from SSM Parameter Store"
umask 077
cat > .env <<EOF
REGISTRY=$REGISTRY
TAG=$TAG
DB_PASSWORD=$(param db-password)
GOOGLE_CLIENT_ID=$(param google-client-id)
GOOGLE_CLIENT_SECRET=$(param google-client-secret)
REBRICKABLE_API_KEY=$(param rebrickable-api-key)
EOF
umask 022

compose() { docker compose -f docker-compose.prod.yml --env-file .env "$@"; }

log "Logging in to ECR"
aws ecr get-login-password --region "$REGION" | docker login --username AWS --password-stdin "$REGISTRY"

log "Pulling images for $TAG"
compose --profile migrate pull

log "Starting PostgreSQL"
compose up -d --wait postgres

log "Applying Liquibase migrations"
compose --profile migrate run --rm migrate

log "Starting apps"
compose up -d --remove-orphans caddy api blazor

log "Installing nightly backup timer"
install -m 0755 "$DIR/backup.sh" /usr/local/bin/legolist-backup
cat > /etc/systemd/system/legolist-backup.service <<EOF
[Unit]
Description=Back up the LegoList database to S3
[Service]
Type=oneshot
ExecStart=/usr/local/bin/legolist-backup $BUCKET
EOF
cat > /etc/systemd/system/legolist-backup.timer <<'EOF'
[Unit]
Description=Nightly LegoList database backup
[Timer]
OnCalendar=*-*-* 07:15:00
Persistent=true
[Install]
WantedBy=timers.target
EOF
systemctl daemon-reload
systemctl enable --now legolist-backup.timer

log "Cleaning up old images"
docker image prune -af --filter "until=168h" >/dev/null || true

log "Deployed $TAG"
compose ps

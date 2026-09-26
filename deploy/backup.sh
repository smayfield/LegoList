#!/usr/bin/env bash
# Dumps the LegoList database and uploads it to S3 (kept 30 days by a bucket
# lifecycle rule). Installed as /usr/local/bin/legolist-backup by deploy.sh and
# run nightly by legolist-backup.timer.
# Usage: legolist-backup <ops-bucket>
set -euo pipefail

BUCKET="$1"
STAMP=$(date -u +%Y-%m-%dT%H%M%SZ)
cd /opt/legolist

docker compose -f docker-compose.prod.yml --env-file .env exec -T postgres \
  pg_dump -U legolist --format=custom legolist \
  | aws s3 cp - "s3://$BUCKET/backups/legolist-$STAMP.dump" --region us-east-1

echo "Backed up to s3://$BUCKET/backups/legolist-$STAMP.dump"

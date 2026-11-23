#!/usr/bin/env bash
# One-off full backup from the host: ./deploy/scripts/backup.sh
set -euo pipefail
cd "$(dirname "$0")/../.."
source deploy/.env
STAMP=$(date +%Y%m%d_%H%M%S)
docker compose -f deploy/docker-compose.yml exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$SA_PASSWORD" -Q \
  "BACKUP DATABASE [BookFlow] TO DISK = N'/var/opt/mssql/backup/BookFlow_full_${STAMP}.bak' WITH COMPRESSION, CHECKSUM, INIT, STATS = 10"
echo "Backup written inside volume bookflow_backups: BookFlow_full_${STAMP}.bak"

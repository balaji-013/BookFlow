#!/usr/bin/env bash
# Restore a full backup (and optionally all later log backups) into a database.
# Usage: ./deploy/scripts/restore.sh BookFlow_full_20261109_013000.bak [TargetDbName]
set -euo pipefail
cd "$(dirname "$0")/../.."
source deploy/.env
FULL=${1:?backup file name inside the bookflow_backups volume}
TARGET=${2:-BookFlow}
SQL="docker compose -f deploy/docker-compose.yml exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P $SA_PASSWORD -b -Q"
echo "Taking the API down so no writes happen during restore..."
docker compose -f deploy/docker-compose.yml stop api web
$SQL "IF DB_ID('$TARGET') IS NOT NULL ALTER DATABASE [$TARGET] SET SINGLE_USER WITH ROLLBACK IMMEDIATE"
$SQL "RESTORE DATABASE [$TARGET] FROM DISK = N'/var/opt/mssql/backup/$FULL' WITH REPLACE, NORECOVERY, CHECKSUM, STATS = 10"
# Apply log backups newer than the full backup, in order.
STAMP=${FULL#BookFlow_full_}; STAMP=${STAMP%.bak}
for LOG in $(docker compose -f deploy/docker-compose.yml exec -T sqlserver bash -c "ls /var/opt/mssql/backup/BookFlow_log_*.trn 2>/dev/null | sort"); do
  LSTAMP=$(basename "$LOG"); LSTAMP=${LSTAMP#BookFlow_log_}; LSTAMP=${LSTAMP%.trn}
  if [[ "$LSTAMP" > "$STAMP" ]]; then echo "applying $LOG"; $SQL "RESTORE LOG [$TARGET] FROM DISK = N'$LOG' WITH NORECOVERY"; fi
done
$SQL "RESTORE DATABASE [$TARGET] WITH RECOVERY; ALTER DATABASE [$TARGET] SET MULTI_USER"
docker compose -f deploy/docker-compose.yml start api web
echo "Restore of $FULL into [$TARGET] complete."

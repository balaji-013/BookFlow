#!/usr/bin/env bash
# Runs inside the 'backup' sidecar: full backup nightly at 01:30 IST, transaction-log backup every 15 min, prune old files.
set -uo pipefail
SQLCMD=/opt/mssql-tools18/bin/sqlcmd
DIR=/var/opt/mssql/backup
RET=${BACKUP_RETENTION_DAYS:-14}
run() { $SQLCMD -C -S sqlserver -U sa -P "$SA_PASSWORD" -b -Q "$1"; }
echo "backup loop started; retention ${RET} days"
# Ensure FULL recovery so log backups work (gives RPO ≈ 15 min).
run "IF DATABASEPROPERTYEX('BookFlow','Recovery') <> 'FULL' ALTER DATABASE [BookFlow] SET RECOVERY FULL" || true
LAST_FULL_DAY=""
while true; do
  NOW_IST=$(TZ=Asia/Kolkata date +%H%M); DAY=$(TZ=Asia/Kolkata date +%F); STAMP=$(date +%Y%m%d_%H%M%S)
  if [[ "$NOW_IST" > "0129" && "$NOW_IST" < "0200" && "$LAST_FULL_DAY" != "$DAY" ]]; then
    run "BACKUP DATABASE [BookFlow] TO DISK = N'${DIR}/BookFlow_full_${STAMP}.bak' WITH COMPRESSION, CHECKSUM, INIT" && LAST_FULL_DAY="$DAY" && echo "full backup ${STAMP}"
    find "$DIR" -name 'BookFlow_*' -mtime +"$RET" -delete
  elif ls "$DIR"/BookFlow_full_*.bak >/dev/null 2>&1; then
    run "BACKUP LOG [BookFlow] TO DISK = N'${DIR}/BookFlow_log_${STAMP}.trn' WITH COMPRESSION, CHECKSUM" && echo "log backup ${STAMP}"
  else
    run "BACKUP DATABASE [BookFlow] TO DISK = N'${DIR}/BookFlow_full_${STAMP}.bak' WITH COMPRESSION, CHECKSUM, INIT" && LAST_FULL_DAY="$DAY" && echo "initial full backup ${STAMP}"
  fi
  sleep 900
done

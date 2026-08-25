#!/usr/bin/env bash
# Postgres dump a VPS-en: /opt/mensi/backups/mensi-<név>.sql.gz
#
# Használat:
#   bash deploy/backup.sh [--skip-if-no-db] [név]
#
# A név alapból az aktuális időbélyeg; a deploy "predeploy-<sha>" néven hív, hogy a
# migráció előtti állapot visszaállítható legyen. A --skip-if-no-db az első, még üres
# hoszton engedi tovább a deployt (nincs mit menteni).
#
# Ez egészségügyi adat (GDPR 9. cikk): a backups mappa 700-as, a dump 600-as jogosultságú;
# a lemez-/kötettitkosítás a RUNBOOK 7. pontja szerint ajánlott.
set -euo pipefail

COMPOSE_FILE=${COMPOSE_FILE:-docker-compose.yml}
BACKUP_DIR=${BACKUP_DIR:-backups}
RETENTION_DAYS=${BACKUP_RETENTION_DAYS:-14}

SKIP_IF_NO_DB=0
if [ "${1:-}" = "--skip-if-no-db" ]; then
  SKIP_IF_NO_DB=1
  shift
fi
NAME=${1:-$(date +%Y%m%d-%H%M%S)}

compose() { docker compose -f "$COMPOSE_FILE" "$@"; }

# .env-ből a db elérés (a szkript a compose mappájából fut).
POSTGRES_USER=$(grep -E '^POSTGRES_USER=' .env 2>/dev/null | cut -d= -f2- || true)
POSTGRES_DB=$(grep -E '^POSTGRES_DB=' .env 2>/dev/null | cut -d= -f2- || true)
POSTGRES_USER=${POSTGRES_USER:-mensi}
POSTGRES_DB=${POSTGRES_DB:-mensi}

db_id=$(compose ps -q db 2>/dev/null || true)
if [ -z "$db_id" ]; then
  if [ "$SKIP_IF_NO_DB" = "1" ]; then
    echo "Nincs futó db konténer — mentés kihagyva (első deploy)."
    exit 0
  fi
  echo "Nincs futó db konténer." >&2
  exit 1
fi

umask 077
mkdir -p "$BACKUP_DIR"
target="$BACKUP_DIR/mensi-$NAME.sql.gz"

compose exec -T db pg_dump -U "$POSTGRES_USER" "$POSTGRES_DB" | gzip > "$target"

if [ ! -s "$target" ]; then
  echo "A dump üres lett: $target" >&2
  rm -f "$target"
  exit 1
fi
echo "Mentés kész: $target ($(du -h "$target" | cut -f1))"

# Retention: a megadott napnál régebbi dumpok törlése.
if [ "$RETENTION_DAYS" -gt 0 ]; then
  find "$BACKUP_DIR" -name 'mensi-*.sql.gz' -mtime +"$RETENTION_DAYS" -delete
fi

#!/usr/bin/env bash
# Postgres visszaállítás egy backup.sh által készített dumpból.
#
# Használat:
#   bash deploy/restore.sh backups/mensi-<név>.sql.gz
#
# FIGYELEM: a visszaállítás a jelenlegi adatbázist ELDOBJA és a dump állapotát tölti be.
# Rollbacknél a sorrend: app leállítás → restore → régi image visszaírása a .env-be
# (.env.previous) → up (részletek: RUNBOOK).
set -euo pipefail

COMPOSE_FILE=${COMPOSE_FILE:-docker-compose.yml}
DUMP=${1:?Használat: restore.sh <dump.sql.gz>}

[ -s "$DUMP" ] || { echo "Nincs ilyen dump: $DUMP" >&2; exit 1; }

compose() { docker compose -f "$COMPOSE_FILE" "$@"; }

POSTGRES_USER=$(grep -E '^POSTGRES_USER=' .env 2>/dev/null | cut -d= -f2- || true)
POSTGRES_DB=$(grep -E '^POSTGRES_DB=' .env 2>/dev/null | cut -d= -f2- || true)
POSTGRES_USER=${POSTGRES_USER:-mensi}
POSTGRES_DB=${POSTGRES_DB:-mensi}

read -r -p "Ez ELDOBJA a(z) '$POSTGRES_DB' adatbázis jelenlegi tartalmát. Írd be: restore > " confirm
[ "$confirm" = "restore" ] || { echo "Megszakítva."; exit 1; }

# Az app nem kapcsolódhat restore közben.
compose stop app

compose exec -T db psql -U "$POSTGRES_USER" -d postgres -v ON_ERROR_STOP=1 \
  -c "DROP DATABASE IF EXISTS \"$POSTGRES_DB\";" \
  -c "CREATE DATABASE \"$POSTGRES_DB\" OWNER \"$POSTGRES_USER\";"

gunzip -c "$DUMP" | compose exec -T db psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -q

compose start app
echo "Visszaállítva: $DUMP"

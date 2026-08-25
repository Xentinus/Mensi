#!/usr/bin/env bash
# Deploy utáni ellenőrzés a VPS-en.
#
# A `docker compose up -d` a konténerek indulásakor visszatér, ami semmit nem mond arról,
# életben maradtak-e: egy hiányzó env változóra a Program.cs szándékosan dob, és a deploy
# zölden érne véget egy fekvő oldal fölött. Ez a szkript mindkét konténer healthy állapotára
# vár, majd a /health endpointot hívja — bármelyik hibájánál nem nullával lép ki.
#
# A / (SPA) itt szándékosan NEM ellenőrizhető: a Cloudflare Access middleware érvényes
# assertion nélkül mindent 403-mal dob — élesben pont ez a helyes viselkedés.
set -uo pipefail

COMPOSE_FILE=${COMPOSE_FILE:-docker-compose.yml}
# 20s start_period van a konténereken, az első healthy jelentés kicsúszhat.
TIMEOUT=${SMOKE_TIMEOUT:-180}
APP_PORT=${APP_PORT:-8100}

compose() { docker compose -f "$COMPOSE_FILE" "$@"; }

wait_healthy() {
  local service=$1 waited=0 id status
  while true; do
    id=$(compose ps -q "$service" 2>/dev/null)
    if [ -n "$id" ]; then
      status=$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$id")
      if [ "$status" = "healthy" ]; then
        echo "$service: healthy"
        return 0
      fi
    else
      status="no container"
    fi
    if [ "$waited" -ge "$TIMEOUT" ]; then
      echo "$service nem lett healthy ${TIMEOUT}s alatt (utolsó állapot: $status)" >&2
      compose logs --tail=80 "$service" >&2
      return 1
    fi
    sleep 5
    waited=$((waited + 5))
  done
}

wait_healthy db || exit 1
wait_healthy app || exit 1

code=$(curl -s -o /dev/null -w '%{http_code}' -m 10 "http://127.0.0.1:${APP_PORT}/health" || true)
if [ "$code" != "200" ]; then
  echo "/health válasza: '$code' (elvárt: 200)" >&2
  compose logs --tail=80 app >&2
  exit 1
fi
echo "/health: 200 OK"

# Mensi — üzemeltetési runbook

## 1. Előfeltételek

- A célszerveren (VPS) telepítve van a Docker Engine a Compose pluginnal (`docker compose`,
  nem a régi `docker-compose`), és rajta már fut a `cloudflared` tunnel — ugyanaz a beállítás,
  mint a PortfolioCMS-nél, ez a runbook nem ismétli meg a tunnel telepítését, csak az ehhez a
  szolgáltatáshoz tartozó pluszlépéseket.
- Hozzáférés a Cloudflare Zero Trust dashboardhoz (a tunnel route és az Access alkalmazás
  beállításához), és a szerver `.env` fájljának szerkesztési joga.

## 2. Cloudflare Tunnel route

- A meglévő tunnel configjába fel kell venni egy új ingress szabályt, ami a `mensi.<domain>`
  hostnevet a konténer loopback-publikált portjára irányítja: `mensi.<domain>` →
  `http://localhost:8100`.
- Ezután a DNS route-ot is létre kell hozni, hogy a hostnév ténylegesen a tunnelen keresztül
  legyen elérhető:
  ```bash
  cloudflared tunnel route dns <tunnel> mensi.<domain>
  ```

## 3. Cloudflare Access alkalmazás

- Zero Trust → Access → Applications → Add self-hosted.
- Domain: `mensi.<domain>`; Session duration: 24h.
- Policy (Allow): Include → Emails: <saját email>, <feleség email>.
- Require → Authentication method: hardware key vagy WARP/biometric MFA — ugyanaz a minta,
  mint az `ssh.<domain>`-nél, tehát jelszó önmagában nem elég a beléptetéshez.
- Az alkalmazás Overview oldaláról ki kell másolni az Audience (AUD) taget — ez kerül a
  `.env` fájl `CF_ACCESS_AUD` kulcsába.
- A team domain (`https://<team>.cloudflareaccess.com`) a `.env` fájl `CF_ACCESS_TEAM_DOMAIN`
  kulcsába kerül.

## 4. Automatikus deploy (GitHub Actions)

Master-re pusholáskor a `.github/workflows/deploy.yml` fut (a PortfolioCMS mintája):
CI (backend + frontend tesztek, auditok) → image build+push a GHCR-be
(`ghcr.io/xentinus/mensi:latest` + `sha-<commit>`) → SSH-deploy a VPS-re
(`/opt/mensi`: compose+szkriptek másolása, `.env` írása a secretekből, pull,
migráció előtti dump, `up -d`, smoke-teszt). PR-oknál csak a CI fut, Trivy
image-szkenneléssel.

**Szükséges GitHub repository secretek** (Settings → Secrets and variables → Actions):

| Secret | Érték |
|---|---|
| `SSH_HOST`, `SSH_USER`, `SSH_KEY`, `SSH_PORT` | a VPS SSH elérése (mint a PortfolioCMS-nél) |
| `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD` | pl. `mensi` / `mensi` / erős jelszó |
| `CF_ACCESS_TEAM_DOMAIN` | `https://<team>.cloudflareaccess.com` |
| `CF_ACCESS_AUD` | a Mensi Access alkalmazás AUD tagje (3. szakasz) |
| `GHCR_USER`, `GHCR_TOKEN` | GHCR pull a VPS-en (read:packages PAT) |
| `AUDIT_RETENTION_DAYS`, `DISPLAY_TIMEZONE`, `BACKUP_RETENTION_DAYS` | opcionális (365 / Europe/Budapest / 14) |

Az első deploy magától felhúzza a `/opt/mensi` könyvtárat és az üres adatbázist — kézi
első indítás nem kell. Ellenőrzés a szerveren:
```bash
cd /opt/mensi && docker compose -f docker-compose.yml ps
curl -s http://127.0.0.1:8100/health
```
Böngészőből a `https://mensi.<domain>` cím → Cloudflare Access login → az alkalmazás
betöltődik. Visszalépés: az előző image referencia a `.env.previous`-ban, a migráció
előtti dump a `backups/predeploy-<sha>.sql.gz`-ben (visszaállítás: `deploy/restore.sh`).

### 4.1 Lokális teszt (fejlesztői gépen)

A teljes konténeres stack Access nélkül, kizárólag helyben:

```bash
CF_ACCESS_ENABLED=false POSTGRES_PASSWORD=devpass docker compose up -d
```

→ `http://127.0.0.1:8100` közvetlenül böngészhető. A `CF_ACCESS_ENABLED=false` a
Cloudflare Access ellenőrzést teljesen kikapcsolja (az app hangos warningot logol róla)
— **élesben soha ne kerüljön a `.env`-be false értékkel**. Jelszócserénél a régi
adat-volume-ot törölni kell (`docker compose down -v`), mert a Postgres a jelszót az
első inicializáláskor rögzíti.

## 5. Frissítés

> **FIGYELEM:** a szerveren SOHA ne fusson sima `docker compose up -d` (az override
> betöltődne és publikálná a db portot); mindig `docker compose -f docker-compose.yml up -d`.

- Normál út: **push a master-re** → a deploy workflow mindent elvégez (4. szakasz).
- Kézi frissítés (ha a CI nem elérhető): a `.env`-ben az `APP_IMAGE` átírása a kívánt
  `ghcr.io/xentinus/mensi:sha-<commit>` tagre, majd
  ```bash
  docker compose -f docker-compose.yml pull && docker compose -f docker-compose.yml up -d
  ```
- A frissítés csak az app konténert cseréli, a `db`-t és a `pgdata` volument nem érinti;
  a migráció az új kód indulásakor automatikusan lefut.

## 6. Mentés és visszaállítás

- Napi mentés `pg_dump`-pal, cron-ba téve:
  ```bash
  docker compose exec -T db pg_dump -U mensi mensi | gzip > /backup/mensi-$(date +%F).sql.gz
  ```
- A backup mappán retenciót kell tartani (pl. 30 nap), és időnként egy restore-próbát is
  érdemes futtatni, hogy a mentés valóban visszaállítható legyen:
  ```bash
  gunzip -c mensi-<date>.sql.gz | docker compose exec -T db psql -U mensi mensi
  ```

## 7. Adatvédelem

- Ez a stack a legérzékenyebb adatkategóriát kezeli (egészségügyi és szexuális adat, GDPR
  9. cikk szerinti különleges kategória): a VPS diszkjén ajánlott LUKS-szal vagy más
  kötettitkosítással védeni az adatokat, és a backup célja (pl. külső tárhely) is titkosított
  legyen.
- Az edge szintű audit (ki, mikor lépett be az alkalmazásba) a Zero Trust → Logs → Access
  nézetből érhető el; az alkalmazásszintű módosítás-audit (ki, mikor, mit módosított) a
  Postgres `audit_log` táblájában van, retenciója a `AUDIT_RETENTION_DAYS` env kulccsal
  állítható.

## 8. Logok

- Alkalmazás- és kérés-log:
  ```bash
  docker compose logs -f app
  ```
  — Serilog request logging + alkalmazáslog, a `x-logging` blokk miatt konténerenként max.
  3×10 MB-ra rotálva, hogy a json-file driver ne nőjön korlátlanul.
- Adatváltozások (ki, mikor, mit módosított) az `audit_log` táblában kereshetők vissza,
  megőrzési idejük a `.env` `AUDIT_RETENTION_DAYS` kulcsából jön.

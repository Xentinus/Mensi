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

## 4. Első indítás

- Repó klónozása a szerverre, majd a `.env` kitöltése a `.env.example` alapján (Postgres jelszó,
  a fenti két Cloudflare Access érték, illetve igény szerint a retention/időzóna felülbírálása).
- Build és indítás:
  ```bash
  docker compose -f docker-compose.yml build && docker compose -f docker-compose.yml up -d
  ```
- Az adatbázis-migráció a háttérben, automatikusan lefut a Mensi.Server indulásakor. Ellenőrzés:
  ```bash
  docker compose ps
  curl -s http://127.0.0.1:8100/health
  ```
  Elvárt eredmény: mindkét szolgáltatás `healthy`, a `curl` válasza `OK`.
- Böngészőből a `https://mensi.<domain>` cím megnyitása → Cloudflare Access login → az
  alkalmazás betöltődik.

## 5. Frissítés

> **FIGYELEM:** a szerveren SOHA ne fusson sima `docker compose up -d` (az override
> betöltődne és publikálná a db portot); mindig `docker compose -f docker-compose.yml up -d`.

- Új verzió kiadása a szerveren:
  ```bash
  git pull && docker compose -f docker-compose.yml build && docker compose -f docker-compose.yml up -d
  ```
- A `docker compose up -d` csak az app konténert cseréli újra (az image hash változott), a
  `db` szolgáltatást és a `pgdata` volument nem érinti; a migráció a frissített kód indulásakor
  fut le automatikusan.

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

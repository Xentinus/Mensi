# Mensi — ciklus- és fogamzáskövető app, design spec

Dátum: 2026-08-24
Státusz: jóváhagyásra vár

## 1. Cél és kontextus

Önálló, self-hosted ciklus- és fogamzáskövető webalkalmazás egy pár (2 fő) számára.
Egy nő napi tünetnaplójából (testhő, cervikális nyák, LH-teszt, görcs, folyás, együttlét,
hangulat) ovulációt és menstruációt jelez előre konfidencia-sávokkal, és fogamzási esélyt
számol az adott ciklusra.

Bemenetek:

- UI/UX referencia: a Claude Design-nal készült „Mensi Care" prototípus (a design 1:1
  irányadó a nézetekre, mezőkre, szövegekre, színekre).
- Matematikai referencia: `ovulacio-terhesseg-predikcio-referencia.md` (a repo
  `docs/` mappájába bemásolva).

Alapelv (funkcionális követelmény, nem stílus): a predikció **soha nem pontdátum**,
mindig tartomány/konfidencia-sáv. A terhesség-jelzés mindig „érdemes tesztelni" jellegű,
sosem állítás.

Egészségügyi és szexuális adat (GDPR 9. cikk különleges kategória): a hozzáférés-vezérlés
a Cloudflare edge-en történik, az app pedig maga is ellenőrzi az Access assertiont
(defense in depth, a PortfolioCMS admin mintájára).

## 2. Architektúra

Monorepo a `Mensi` repóban, a PortfolioCMS bevált mintáit követve, de egyetlen hosttal:

```
Mensi.slnx
Mensi.Core/       — domain: entitások, EF Core (Npgsql), predikciós motor,
                    CloudflareAccess middleware + keystore, audit writer
Mensi.Server/     — ASP.NET Core (.NET 10) host: /api endpointok + a Nuxt statikus
                    build kiszolgálása (wwwroot), Serilog, health
Mensi.Tests/      — xUnit tesztek (Core + Server)
mensi.client/     — Nuxt 4 SPA (ssr: false), TypeScript, Pinia; `nuxt generate`
                    statikus outputja kerül a Server wwwroot-jába
deploy/           — RUNBOOK.md (tunnel + Access + backup)
docker-compose.yml, docker-compose.override.yml, .env.example
```

- A Nuxt SSR itt semmit nem adna (privát app, nincs SEO), ezért SPA mód: nincs külön
  Node konténer, egy ASP.NET host szolgál ki mindent.
- Egyfelhasználós adatmodell: **nincs user tábla, nincs user_id** (referencia 11.1).
  A „ki csinálta" kérdésre az audit_log válaszol az Access JWT emailjéből.
- Írás után a ciklus-összegzők szinkron újraszámolódnak (az adatmennyiség évi ~365 sor,
  háttér-worker felesleges). A predikció olvasáskor számolódik, így a „ma" mindig friss.

## 3. Adatmodell (PostgreSQL 17, EF Core migrációkkal)

Enumok C#-ban definiálva, az oszlopok `smallint`-ként tárolva. Dátumok `date` típusú
naptári napok (nincs időpont-komponens); a „ma" a konfigurált időzónában
(`Display__TimeZone`, default `Europe/Budapest`) értendő.

### 3.1 `daily_log`

| Oszlop | Típus | Megjegyzés |
|---|---|---|
| date | date PK | naptári nap |
| bbt_celsius | numeric(4,2) null | 2 tizedes, görgős picker a UI-ban |
| cervical_mucus | smallint null | 0 száraz, 1 ragadós, 2 nedves, 3 nyúlós |
| lh_test | smallint null | 0 negatív, 1 pozitív, 2 csúcs |
| cramp_type | smallint null | 0 alhas, 1 derék, 2 mell (a design taxonómiája) |
| cramp_severity | smallint null | 0 nincs, 1 enyhe, 2 közepes, 3 erős |
| flow_intensity | smallint null | 0 nincs, 1 pecsételő, 2 enyhe, 3 közepes, 4 erős |
| period_start | boolean not null default false | „Ma kezdődött a menstruáció" — ez jelöli ki a ciklushatárt, kizárólag explicit |
| moods | smallint[] null | 0 vidám, 1 nyugodt, 2 ingerlékeny, 3 fáradt, 4 szomorú, 5 szorongó, 6 vágyakozó |
| created_at, updated_at | timestamptz | |
| updated_by | text | email az Access JWT-ből |

A `null` mindenhol „nincs rögzítve"-t jelent; a kitöltött 0 (pl. flow „nincs", görcs
„nincs", együttlét 0) rögzített adat. A napi mentés mezőnkénti (részleges upsert).

### 3.2 `intercourse_event`

| Oszlop | Típus | Megjegyzés |
|---|---|---|
| id | bigint identity PK | |
| date | date not null | FK jelleggel a naphoz (index) |
| protected | boolean null | „védekezéssel volt" kapcsoló, null = nincs megadva |
| created_at | timestamptz | |

Egy napon több esemény lehet (a design számlálót + eseményenkénti kapcsolót mutat).

### 3.3 `cycle` (napi logból levezetett, gépi táblázat)

| Oszlop | Típus | Megjegyzés |
|---|---|---|
| id | bigint identity PK | |
| start_date | date unique | period_start=true nap |
| length_days | int null | a következő start_date-ig; null = nyitott ciklus |
| ovulation_day_estimated | int null | Bayes-becslés (ciklusnap) |
| ovulation_day_confirmed | int null | BBT coverline-shift alapján |
| luteal_phase_length | int null | csak megerősített ovulációnál |
| anovulatory | boolean not null default false | nincs bifázisos BBT-mintázat |
| predicted_length_days | int null | a modell akkori predikciója a ciklus indulásakor — ebből épül a „késés" (delay) eloszlás |
| computed_at | timestamptz | |

Újraszámolás: bármely `daily_log`/`intercourse_event` írás után a teljes tábla
determinisztikusan újraépül a logokból (kivéve a `predicted_length_days`-t, ami
egyszer, a ciklus indulásakor íródik és utána nem változik).

### 3.4 `audit_log`

| Oszlop | Típus | Megjegyzés |
|---|---|---|
| id | bigint identity PK | |
| at | timestamptz | |
| email | text | Access JWT-ből |
| action | text | pl. `log.upsert`, `intercourse.set` |
| entry_date | date | melyik napi bejegyzést érintette |
| changes | jsonb | mező → {régi, új} diff |

Retention: `Audit__RetentionDays` env (default 365, 0 = örökre), hosted service törli
a lejártakat — a PortfolioCMS `AuditRetentionService` mintájára.

### 3.5 v1-ből tudatosan kihagyva

`notes`, `rhr_bpm` (wearable), `lh_value` (digitális monitor), kézi `bbt_excluded` flag
— az outlier-kizárás automatikus (ld. 4.4), a design is így mutatja. A séma bővíthető,
ha később kell.

## 4. Predikciós motor (Mensi.Core, pure, determinisztikus)

Minden számítás tiszta függvény: bemenet a naplósorok + a mai dátum, kimenet a
predikciós objektum. Nincs I/O, nincs óra-olvasás — teljesen unit-tesztelhető.

### 4.1 Ciklusstatisztika

Lezárt ciklusok hosszaiból (L₁…Lₙ):

- EWMA: `Ĺ_t = α·L_t + (1−α)·Ĺ_(t−1)`, α = 0,27; induló érték az első ciklus hossza
- szórás (n−1 osztóval), medián, min/max
- delay-eloszlás: `delay_i = L_i − predicted_length_days_i` a lezárt ciklusokból,
  P10/P50/P90 percentilis
- anovulatorikus ciklus: benne marad a hossz-statisztikában 0,5 súllyal (EWMA-nál a
  frissítés α·0,5-tel), a luteális statisztikából kizárva

### 4.2 Empirical Bayes shrinkage (kevés adat kezelése)

Populációs priorok: ciklushossz ~ Normal(28; 4²), luteális ~ Normal(14; 2²).
A személyes paraméter normál-normál konjugált frissítéssel:

```
μ_személyes = (n·x̄/s² + μ_pop/τ²) / (n/s² + 1/τ²)
σ²_személyes = 1 / (n/s² + 1/τ²) + s²_within
```

ahol n a lezárt (luteálisnál: megerősített) ciklusok száma, x̄ a saját átlag, s² a saját
variancia (n<2-nél a populációs variancia), τ² a populációs variancia. 0 lezárt ciklusnál
tisztán a populációs prior él, és az app **nem mutat predikciót** (empty state, ld. 6. fejezet).

### 4.3 Szekvenciális Bayes-szűrő az ovulációs napra (aktuális ciklus)

Diszkrét rács az ovulációs nap posteriorjára: o ∈ [6 … 40] ciklusnap.

**Prior:** Normal(μ_o; σ_o²) diszkretizálva, ahol
`μ_o = Ĺ_t(EWMA, shrinkage után) − μ_luteális`, `σ_o² = σ²_ciklus + σ²_luteális`.

**Likelihood-frissítés:** minden logolt nap minden jelére
`posterior(o) ∝ prior(o) · Π P(jel a d napon | ovuláció = o)`, a P értékek a d−o
relatív naptól függő rögzített táblázatok (likelihood-arányok a „semleges" 1,0-hoz
képest):

| Jel | d−o tartomány és szorzó |
|---|---|
| LH pozitív | −2…0: ×6; −3, +1: ×2; egyébként ×0,3 |
| LH csúcs | −1…0: ×12; −2, +1: ×2; egyébként ×0,15 |
| LH negatív | −1…+1: ×0,6; egyébként ×1,1 |
| nyúlós nyák | −3…0: ×3; −4, +1: ×1,5; egyébként ×0,5 |
| nedves nyák | −4…−1: ×1,8; egyébként ×0,8 |
| száraz/ragadós nyák | −2…+1: ×0,55; egyébként ×1,15 |
| BBT-shift megerősítve (4.4 szerint), ovulációs napja o* | o ∈ [o*−1, o*+1]: ×4; egyébként ×0,25 |
| alhasi görcs a ciklus 8. napja után (Mittelschmerz) | −1…+1: ×1,6; egyébként ×0,95 |

A szorzatok után normalizálás. A táblázat értékei a specifikáció részei — a golden
tesztek ezekre épülnek; hangolásuk későbbi iteráció.

**Kimenetek:**

- ovulációs ablak: a posterior [P15, P85] intervalluma (dátumra vetítve)
- következő menstruáció eloszlása: `P(period = t) = Σ_o posterior(o) · P_luteális(t−o)`,
  ahol P_luteális a személyes Normal(μ_l; σ_l²) [9, 18] napra vágva és diszkretizálva;
  megjelenítve [P15, P85] sáv
- termékeny ablak: [ovuláció_P50 − 5, ovuláció_P85 + 1]
- konfidencia-címke a posterior 70%-os intervallum-szélességéből: ≤4 nap „magas",
  ≤7 nap „közepes", fölötte „alacsony"; 3-nál kevesebb lezárt ciklusnál legfeljebb
  „közepes"

### 4.4 BBT coverline (retrospektív megerősítés)

- csak az aktuális/adott ciklus mérései; hiányzó nap kimarad (nincs interpoláció)
- outlier-kizárás automatikus: az az érték, ami a környező érvényes mérések (±3 napon
  belüli, legfeljebb 5 érték) mediánjától ≥0,3 °C-kal tér el, miközben a közvetlenül
  előtte és utána lévő érvényes mérés egyike sem tér el ugyanabba az irányba ≥0,15 °C-kal
  (tehát magányos kiugrás, nem trend része), kimarad a coverline-számításból
  (a UI jelzi: „1 kiugró érték kihagyva")
- coverline = max(az emelkedés előtti utolsó 6 érvényes érték)
- ovuláció megerősítve, ha 3 egymást követő érvényes mérés > coverline + 0,2 °C;
  az ovuláció napja a shift előtti utolsó alacsony nap
- megerősítéskor: `ovulation_day_confirmed` és `luteal_phase_length` írása a ciklusba
- bifázisos mintázat hiánya a ciklus zárásakor → `anovulatory = true`

### 4.5 Fogamzási esély és időzítés-minősítés (Wilcox)

Nap-relatív kernel (Wilcox 1995): p(−5)=0,10; p(−4)=0,16; p(−3)=0,14; p(−2)=0,27;
p(−1)=0,31; p(0)=0,33; máskor 0. (A −5 és 0 közötti értékek a publikált napi
valószínűségek.)

Ciklusszintű esély a posterior felett várható értékkel, csak a védekezés nélküli
(protected ≠ true) együttlét-napokra:

```
esély = Σ_o posterior(o) · [1 − Π_d (1 − p(d − o))]
```

ahol d a logolt együttlét-napok. Kimenet a UI-ban: **minősítés + százalék** —
„Jó / Közepes / Gyenge" címke és „becsült esély ebben a ciklusban: X%", mellette
konfidencia-megjegyzés. Címke-küszöbök: <8% gyenge, 8–16% közepes, >16% jó.
Lezárt ciklusokra ugyanez visszamenőleg számolódik (a trend-táblázat „Időzítés"
oszlopa), a megerősített (annak híján a becsült) ovulációs nap köré húzott
posteriorral.

Kontrafaktuális tipp (az Esély nézet „mit-ha" kártyaszövege): a motor kiszámolja,
hogy a következő 1–2 napra felvett egy-egy együttlét melyik címke-küszöböt lépné át,
és ebből ad mondatot („Ha ma vagy holnap van együttlét, a minősítés Jó lesz"); ha
nincs elérhető javulás, nincs tipp.

### 4.6 Terhesség-jelzés

Jelzés („érdemes hCG-tesztet végezni"), ha:

- a mai nap túl van a menstruáció-predikció P85 határán, ÉS
- a BBT az utolsó 3 érvényes mérésben a coverline fölött maradt, ÉS
- nincs `flow_intensity ≥ enyhe` bejegyzés a várt menstruáció óta

vagy: megerősített ovuláció után a BBT `μ_luteális + 3` napnál tovább emelkedett és
nincs vérzés. A kimenet mindig jelzés-szövegű, sosem állítás.

### 4.7 Ciklusfázis és nap-kategóriák (UI-hoz)

A design nap-sávjainak megfelelő, egymást nem fedő kategóriák (az 5.1 `category`
enumja ugyanez):

- `menstruation`: a ciklus elejétől az összefüggő vérzéses napok végéig
  (`flow_intensity ≥ enyhe`)
- `fertile`: [ovuláció_P50 − 5, ovulációs ablak kezdete − 1] — üres, ha a posterior
  olyan szűk, hogy a két határ átfedne
- `ovulation`: az ovuláció-posterior [P15, P85]
- `predictedPeriod`: a következő menstruáció [P15, P85]
- `luteal`: az ovulációs ablak vége és a predictedPeriod kezdete között
- `follicular`: minden egyéb nap a menstruáció után az ovulációs sávok előtt
- `preCycle` / `unknown`: első rögzített ciklus előtti, illetve predikció nélküli napok

Az aktuális fázis (a Ma nézet progressz-sávja) a mai nap kategóriájából jön; a
„hátralévő napok" pontsor a fázis kezdő- és zárónapjából számolódik. A design
címkéi: Menstruáció / Folliculáris szakasz / Termékeny ablak / Ovulációs ablak /
Luteális fázis.

## 5. API (ASP.NET Core, JSON, magyar hibaüzenetek)

Minden endpoint az Access middleware mögött, kivéve `GET /health` (a middleware előtt,
a compose healthcheckhez — a PortfolioCMS mintájára).

A válaszok a design prototípus render-logikájához igazodnak: minden nézet egy
endpointból kapja meg a teljes állapotát. Felelősség-határ: a backend **szemantikus
tényeket** ad (dátumok, enumok, számok, kategóriák) és azokat a mondatokat, amelyek
modell-logikát kódolnak (headline, esély-magyarázat, mit-ha tipp, terhesség-jelzés);
a **formázás** (vessző-tizedes, „aug. 23." dátumalak, színek, chip-szövegek,
„nincs rögzítve"/„Kihagyva" feliratok) a frontendé.

JSON konvenciók: camelCase kulcsok; enumok camelCase stringként
(`JsonStringEnumConverter`); dátum `yyyy-MM-dd`; szám ponttal (a vesszős megjelenítés
frontend dolga).

### 5.1 Közös DTO-k

`DailyLogDto` — egy nap teljes bejegyzése:

```jsonc
{
  "date": "2026-08-23",
  "bbtCelsius": 36.36,            // null = nincs mérés
  "bbtOutlier": false,            // a 4.4 automata kizárás jelzi (számított, nem tárolt)
  "cervicalMucus": "eggWhite",    // dry | sticky | creamy | eggWhite | null
  "lhTest": "positive",           // negative | positive | peak | null
  "crampType": "abdomen",         // abdomen | back | breast | null
  "crampSeverity": 2,             // 0–3 | null
  "flowIntensity": "medium",      // none | spotting | light | medium | heavy | null
  "periodStart": false,
  "moods": ["cheerful", "longing"], // cheerful|calm|irritable|tired|sad|anxious|longing
  "intercourse": [ { "id": 12, "protected": false } ],  // időrendben, max 6/nap
  "updatedAt": "2026-08-23T06:41:00Z",
  "updatedBy": "a@b.hu"
}
```

Enum ↔ UI címke megfeleltetés (frontend konstans): mucus Száraz/Ragadós/Nedves/Nyúlós,
LH Negatív/Pozitív/Csúcs, görcs Alhas/Derék/Mell + Nincs/Enyhe/Közepes/Erős, folyás
Nincs/Pecsételő/Enyhe/Közepes/Erős, hangulat Vidám/Nyugodt/Ingerlékeny/Fáradt/Szomorú/
Szorongó/Vágyakozó (emojival).

`day category` — naptári nap ciklus-kategóriája (a Ma-nézet 5 hetes sávja és a naptár
színezi): `preCycle` (első rögzített ciklus előtt) | `menstruation` (vérzéses napok a
ciklus elejétől) | `follicular` | `fertile` (termékeny sáv az ovulációs ablak előtt) |
`ovulation` (ovuláció-posterior [P15, P85]) | `luteal` | `predictedPeriod` (következő
menstruáció [P15, P85]) | `unknown` (predikció nélküli jövő/adathiány).

`timing` — időzítés-minősítés: `{ "label": "medium", "chancePercent": 12.4 }`
(label: weak | medium | good; UI: Gyenge/Közepes/Jó).

### 5.2 Endpointok

**`GET /api/overview`** — a Ma nézet teljes állapota:

```jsonc
{
  "today": "2026-08-23",
  "isEmpty": false,               // 0 lezárt ciklus → true, a többi mező null/üres
  "cycle": { "day": 14, "startDate": "2026-08-10" },
  "phase": {                      // 4.7 szerinti aktuális fázis + progressz
    "key": "ovulation", "label": "Ovulációs ablak",
    "totalDays": 5, "elapsedDays": 1, "remainingDays": 4
  },
  "headline": "Termékeny ablakban vagy — az ovuláció 4 napon belül várható",
  "ovulationWindow": { "from": "2026-08-23", "to": "2026-08-27" },
  "nextPeriodWindow": { "from": "2026-09-04", "to": "2026-09-08" },
  "confidence": "medium",         // low | medium | high (UI: alacsony/közepes/magas)
  "pregnancyHint": null,          // vagy { "message": "..." } (4.6)
  "strip": {                      // 5 hetes sáv: mai hét ±2 hét, hétfőtől
    "from": "2026-08-03", "to": "2026-09-06",
    "days": [ { "date": "2026-08-03", "cycleDay": null, "category": "preCycle",
                "isToday": false } ]
  },
  "timing": {                     // időzítés-kártya
    "label": "medium", "chancePercent": 12.4,
    "daysRemaining": 4,           // a termékeny ablakból hátralévő napok
    "intercourseTotal": 3,        // minden logolt együttlét az ablakban (megjelenítés;
                                  //   az esély-számítás a védetteket kihagyja, 4.5)
    "windowDays": [ { "date": "2026-08-18", "cycleDay": 9,
                      "intercourseCount": 0, "isOvulationWindow": false,
                      "isFuture": false } ]
                                  // windowDays = fertile ∪ ovulation kategóriájú napok
                                  //   (4.7), a design 10 napos sávja
  },
  "todayLog": { /* DailyLogDto vagy null */ },     // Mai bejegyzés lista + sheet prefill
  "yesterdayLog": { /* DailyLogDto vagy null */ }  // Tegnap chip-sor
}
```

**`GET /api/logs?from=&to=`** — `{ "days": [DailyLogDto] }`, csak a bejegyzéssel bíró
napok. A trendek bejegyzés-hőtérképét és a naptár-pöttyöket a kliens ebből építi.

**`GET /api/logs/{date}`** — DailyLogDto; ha nincs sor, minden mező null-lal tér vissza
(nem 404 — a szerkesztő-sheet üres állapota).

**`PUT /api/logs/{date}`** — mezőnkénti részleges upsert. A body csak a küldött mezőket
tartalmazza; jelen lévő kulcs `null` értékkel = mező törlése; hiányzó kulcs = érintetlen.
`periodStart` váltása ciklushatárt mozgat (recompute). Válasz: a frissített DailyLogDto
(az overview-t a kliens újratölti). Görcs-konzisztencia: `crampSeverity = 0` esetén
`crampType` törlődik.

**`PUT /api/logs/{date}/intercourse`** — `{ "events": [ { "protected": false } ] }`
(max 6 elem, a lista a nap teljes eseménysorát lecseréli). Válasz: DailyLogDto.

**`GET /api/trends`** — a Trendek nézet állapota:

```jsonc
{
  "stats": {                       // null, ha nincs lezárt ciklus
    "averageLength": 28.0, "minLength": 26, "maxLength": 31, "stdDev": 1.6,
    "averageLuteal": 13.2,
    "loggedPercent": 86             // a lezárt ciklusok (utolsó 6) napjainak hány
  },                                //   %-án van legalább egy rögzített mező
  "cycles": [ {                     // minden lezárt ciklus, legújabb elöl
    "startDate": "2026-07-14", "lengthDays": 27,
    "deviationFromAverage": -1,     // kerekített egész nap
    "lutealLength": 13,             // null, ha nincs megerősítve
    "anovulatory": false,
    "timing": { "label": "good", "chancePercent": 21.0 }
  } ],
  "bbt": {                          // aktuális ciklus BBT-táblázata
    "coverline": 36.44,             // null, amíg nem számolható
    "ovulationConfirmed": false,
    "confirmedOvulationDate": null,
    "excludedOutlierCount": 1, "missingDayCount": 2,
    "rows": [ {                     // ciklus 1. napjától máig, minden nap
      "date": "2026-08-10", "cycleDay": 1,
      "value": 36.38,               // null = nincs mérés
      "deltaFromCoverline": -0.06,  // null, ha nincs érték vagy coverline
      "isOutlier": false, "aboveCoverline": false,
      "marks": { "cervicalMucus": "dry", "lhTest": null }  // a Jelek oszlophoz
    } ]
  }
}
```

**`GET /api/calendar?year=&month=`** — a Bejegyzések (naptár) nézet egy hónapja:

```jsonc
{
  "month": "2026-08",
  "range": { "firstMonth": "2026-02", "lastMonth": "2026-09" }, // léptetés/legördülő
                                    // határai: első bejegyzés hónapja … aktuális+1
  "cycleDayOfToday": 14,            // null, ha a hónap nem tartalmazza a mai napot
  "hasData": true,                  // false → „Ehhez a hónaphoz még nincs adat"
  "days": [ {
    "date": "2026-08-01", "cycleDay": null, "category": "luteal",
    "hasBbt": false, "intercourseCount": 0, "hasAnyEntry": false,
    "isToday": false
  } ]                               // jövőbeli napokra a kategória a predikcióból
}
```

A kiválasztott nap paneljét a kliens `GET /api/logs/{date}`-ből tölti; jövőbeli napra
nem kínál szerkesztést.

**`GET /api/chance`** — az Esély nézet állapota:

```jsonc
{
  "isEmpty": false,
  "timing": { "label": "medium", "chancePercent": 12.4 },
  "explanation": "Három együttlét esik a termékeny ablakba, két külön napon — a becsült ovuláció előtt 3 és 1 nappal.",
  "confidenceNote": "A becslés a Wilcox-féle napi valószínűségeken és az ovuláció-posterioron alapul; szélessége a lezárt ciklusok számával csökken.",
  "fertileWindow": {
    "daysRemaining": 4,
    "ovulationWindowTotal": 5, "ovulationWindowElapsed": 1,
    "days": [ { "date": "2026-08-18", "cycleDay": 9, "intercourseCount": 1,
                "isFuture": false, "isToday": false } ]
  },
  "whatIfHint": "Ha ma vagy holnap van együttlét, a minősítés Jó lesz.",
                                    // kontrafaktuális: a motor kiszámolja, mely közeli
                                    // napokon lévő együttlét lépné át a következő
                                    // címke-küszöböt; null, ha nincs ilyen
  "history": {
    "goodCount": 2, "totalCount": 6,
    "cycles": [ { "startDate": "2026-07-14",
                  "timing": { "label": "good", "chancePercent": 21.0 } } ]
  }
}
```

A Módszertan blokk statikus frontend-szöveg, a %-döntéshez igazítva (az esély a
Wilcox-adatokon alapuló becslés, nem orvosi termékenységi vizsgálat; életkort,
spermaminőséget, gyógyszereket nem vesz figyelembe; hiányzó napot nem pótol).

**`GET /health`** — 200 OK, Access előtt.

### 5.3 Validáció és hibák

- BBT: 35,00–38,99 °C (a design görgős pickere is ezt a tartományt adja)
- dátum nem lehet jövőbeli írásnál (a konfigurált időzóna szerinti mai naphoz képest);
  múltbeli bármely nap szerkeszthető
- `crampSeverity` 0–3; enum-értékek tartományon belül; intercourse események száma ≤6/nap
- hibák: ProblemDetails, magyar `detail` szöveggel

Visszavonás (toast „Visszavonás"): kliensoldali — a kliens a mentés előtti értéket
küldi vissza ugyanarra az endpointra. Mindkét írás auditálódik.

## 6. Frontend (Nuxt 4 SPA, TypeScript, Pinia)

`ssr: false`, `nuxt generate` → statikus fájlok a Server wwwroot-jában. Ugyanaz az
origin, `/api` relatív hívások (`$fetch`). Betűtípus: Montserrat `@fontsource`-ból
self-hostolva (nincs Google CDN — a CSP és az offline miatt). Design tokenek a
prototípusból (elsődleges #5a5cd6, háttér #f6f7ff, tinta #21243d, stb.).

Nézetek (útvonalak) a design 1:1 követésével:

- `/` — **Ma**: hero-kártya (ciklusnap, fázis, ovuláció- és menstruáció-sáv,
  fázis-progressz), időzítés-kártya (10 napos termékeny sáv együttlét-számokkal),
  „Tegnap" chip-sor, „Mai bejegyzés" mezőlista + „Mind végigkérdezése"
- `/trendek` — ciklushossz-statisztika (átlag, sáv-vizualizáció, min/max/szórás/medián
  kártyák, történeti táblázat), BBT-táblázat coverline-nal és jelekkel,
  bejegyzés-hőtérkép (14 napos görgethető rács)
- `/bejegyzesek` — havi naptár (fázis-színek, bejegyzés-pöttyök, predikció-overlay),
  kiválasztott nap részletei, sorra koppintva egymezős szerkesztés
- `/esely` — időzítés-minősítés + becsült esély %, termékeny ablak napjai,
  hátralévő ablak kártya, korábbi ciklusok időzítés-sávjai, Módszertan blokk

Közös komponensek: napló-sheet (8 lépés: Testhő görgős pickerrel [35–38 egész +
tized + század] → Nyák → LH → Görcs [hely + intenzitás; „Nincs" intenzitásnál a
helyválasztó letiltva] → Folyás [+ „Ma kezdődött a menstruáció"] → Együttlét
[számláló, max 6, eseményenkénti „védekezéssel" kapcsoló] → Hangulat [multi-select
chipek] → Összegzés), minden lépés kihagyható; egymezős mód ugyanebből a sheetből;
toast mentés-visszajelzés Visszavonás gombbal és progress-csíkkal.

Navigáció: mobil alsó tabbar 3 elemmel (Ma, Trendek, Bejegyzések) — az Esély a Ma
nézet időzítés-kártyájáról nyílik, vissza-gombbal; a ≥1000px oldalsáv mind a 4
nézetet listázza. A naptár hónap-léptetése és legördülője az API `range` mezőjéből
épül (első bejegyzés hónapja … aktuális hónap + 1).

Empty state (0 lezárt ciklus): a design „Nincs még adat" képernyője — predikció nélkül,
3 lépéses magyarázattal, „Első bejegyzés rögzítése" gombbal.

UI szövegek magyarul, a design szövegei szó szerint átveendők, ahol léteznek.

## 7. Auth — Cloudflare Access

- `CloudflareAccessMiddleware` + `CloudflareAccessKeyStore` + options a PortfolioCMS
  Core-ból átemelve (JWT: issuer + audience + JWKS validáció, kulcsrotációnál egyszeri
  refresh+retry, `Cf-Access-Jwt-Assertion` header vagy `CF_Authorization` cookie,
  403 a válasz, az ok csak logba).
- A validált email a `ClaimsPrincipal`-ra kerül → audit_log.
- Konfiguráció: `CloudflareAccess__TeamDomain` + `CloudflareAccess__Audience` env.
  Élesben kötelező (nélküle a host el sem indul); Development-ben hiányzó config esetén
  a middleware kimarad, hangos warninggal + fix `dev@localhost` identitással az audithoz.
- Edge-oldal (RUNBOOK): cloudflared tunnel route a Mensi hostnévre, Access alkalmazás
  Include szabállyal 2 konkrét emailre, MFA kikényszerítéssel — ugyanaz a minta, mint
  a CMS adminnál és az SSH-nál.

## 8. Logging és audit

- **Request/app logok:** Serilog (`UseSerilogRequestLogging`), console sink; a Docker
  json-file driver rotál (3×10 MB) — a CMS compose mintája.
- **Adatváltozás-audit:** minden írás egy `audit_log` sort ír (email, action, nap,
  mező-diff jsonb-ben). Retention env-ből, hosted service takarít.
- **Edge audit:** a Cloudflare Access naplózza a belépéseket (ki, mikor, melyik appba)
  — a RUNBOOK hivatkozza, az appban nincs teendő.

## 9. Deploy

`docker-compose.yml` a CMS mintájára:

- `db`: postgres:17-alpine, `internal: true` backend hálózat, memória-limithez
  szabott beállítások, healthcheck, named volume; **soha nincs ports** bejegyzése
- `app`: multi-stage Dockerfile (Node stage: `nuxt generate` → .NET stage: publish,
  wwwroot-ba másolt kliens), `127.0.0.1:8100:8080` loopback port, healthcheck a
  `/health`-re bash /dev/tcp-vel, env-ből: connection string, CF Access, retention,
  TZ; erőforrás-limitek
- `docker-compose.override.yml` (csak dev): db port publikálás localhostra
- `.env.example` minden kötelező kulccsal
- `deploy/RUNBOOK.md`: tunnel route + Access app létrehozás lépésről lépésre,
  env kitöltés, első indítás, `pg_dump` backup (cron minta), restore, valamint
  megjegyzés a kötet-/lemeztitkosításról (ez a stack legérzékenyebb adatkategóriája)

## 10. Tesztstratégia

- **Predikciós motor (a súlypont):** golden unit-tesztek a referencia validált
  számaira — Wilcox-kernel értékek, coverline-megerősítés (3 nap > +0,2 °C),
  luteális visszaszámolás, EWMA, delay-percentilisek, shrinkage határesetek
  (0/1/6 ciklus), outlier-kizárás, anovulatorikus ciklus, terhesség-jelzés,
  posterior-ablak szűkülés LH-csúcs hatására; szintetikus többciklusos fixture-ök
- **Middleware:** a PortfolioCMS CloudflareAccess tesztjei átemelve/adaptálva
- **API integráció:** WebApplicationFactory + valós Postgres (Testcontainers);
  mezőnkénti upsert, ciklushatár-mozgatás period_start váltásra, audit-sor születés
- **Frontend:** `vue-tsc` type-check, eslint/oxlint, vitest a számító/formázó
  helperekre (dátum-sávok, rács-építés)

## 11. Nem cél (v1)

Wearable/eszköz-integrációk, adat-export, push/emlékeztető értesítések, több profil,
i18n (csak magyar), PWA/offline mód, Redis cache (nincs mit cache-elni ezen a méreten).

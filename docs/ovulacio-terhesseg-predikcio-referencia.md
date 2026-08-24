# Ovuláció- és menstruáció-predikció — matematikai modell referencia

## 1. Cél és alapelv

A cél napi szintű logolt adatokból (ciklus, BBT, nyák, LH, tünetek, közösülés) a lehető legpontosabb becslést adni:
- mikor várható a következő **ovuláció**
- mikor várható a következő **menstruáció**, és ehhez képest mennyi a szokásos "késés"
- mekkora a **fogamzás esélye** az adott ciklusban

**Alapelv: soha ne adjon ki egyetlen dátumot** — mindig konfidencia-sávot / valószínűségi eloszlást, mert minden bemenet (ciklushossz, ovuláció napja) eleve szórással rendelkezik.

Ez nem finomkodás, hanem gyakorlati tét. A Natural Cycles-ügy Svédországban (2018) jól mutatja: a stockholmi Södersjukhuset kórház bejelentése szerint 37 felhasználó esett teherbe és szakíttatta meg terhességét az app fogamzásgátlóként való használata mellett. A svéd gyógyszerhatóság (Läkemedelsverket) vizsgálata végül megállapította, hogy ez az arány összhangban volt a cég saját, typical-use hatékonysági adataival (~93%) — a modell matematikailag "helyesen" működött, a probléma az volt, hogy a felhasználók a piros/zöld napot bináris garanciaként kezelték, nem valószínűségi jelzésként. Ez a modell ugyanezt a hibát próbálja elkerülni a kimenet formátumával (ld. 6. fejezet).

Fontos: ez a dokumentum egy önálló, self-hosted személyes/családtervezési eszközhöz készül, nem helyettesíti a klinikai tesztet (LH-csík, hCG-teszt, orvosi ovulációkövetés) — csak jelzést ad, mikor érdemes azt elvégezni.

## 2. Adatmodell

### 2.1 Napi log (`daily_log`)

| Mező | Típus | Megjegyzés |
|---|---|---|
| date | date | elsődleges kulcs része |
| cycle_day | int | számított, nem input |
| bbt_celsius | float, nullable | reggeli, ébredés utáni, felkelés előtti mérés |
| bbt_excluded | bool | betegség, alkohol, kevés alvás, éjszakai aktivitás miatt kizárva |
| rhr_bpm | int, nullable | nyugalmi pulzus, opcionális kiegészítő jel (wearable) |
| cervical_mucus | enum(dry, sticky, creamy, egg_white) | nullable |
| lh_test | enum(negative, positive, peak) | nullable |
| lh_value | float, nullable | mIU/ml, ha digitális monitor van (pl. Mira, Inito) |
| flow_intensity | enum(none, spotting, light, medium, heavy) | |
| cramp_type | enum(none, menstrual, midcycle) | midcycle = Mittelschmerz |
| cramp_severity | int 0–3 | |
| cramp_laterality | enum(left, right, unknown) | nullable |
| intercourse | bool | |
| intercourse_protected | bool | nullable |
| notes | text | |

### 2.2 Ciklus-összegző tábla (`cycle_summary`) — a napi logból levezetve

| Mező | Leírás |
|---|---|
| cycle_start_date | menstruáció 1. napja |
| cycle_length_days | a következő cycle_start_date-ig eltelt napok |
| ovulation_day_estimated | naptári/Bayes-becslés |
| ovulation_day_confirmed | BBT coverline-shift alapján, ha van elég adat |
| luteal_phase_length | cycle_length − ovulation_day_confirmed, csak megerősített ciklusra |
| anovulatory | bool — nincs bifázisos BBT-mintázat |

### 2.3 Mérési eszközök és protokoll

Egyedül a **BBT igényel tényleges műszeres mérést** — minden más input megfigyelés vagy teszt-eszköz.

- **BBT:** dedikált bázishőmérő kell, 0,01°C felbontással — a szokásos lázmérő 0,1°C-os felbontása nem elég a ~0,3–0,5°C-os posztovulációs emelkedés megbízható detektálásához. Protokoll: minden nap azonos időben (±30 perc), minimum 3–4 óra folyamatos alvás után, kelés/mozgás előtt, szájban vagy hüvelyben mérve. Bluetooth-os okoshőmérők (Tempdrop, Femometer, OvuSense) automatikusan naplóznak, és mozgást/alvásminőséget is érzékelnek a megbízhatatlan mérések előzetes kiszűrésére — ha van API/exportjuk, közvetlenül tölthetők a `daily_log`-ba, kézi bevitel nélkül.
- **LH:** teszt csík (kvalitatív: negative/positive/peak) vagy digitális monitor (Mira, Inito, Clearblue Advanced), ami tényleges hormonkoncentrációt ad. Ha van ilyen eszköz, az `lh_value` mező (folytonos érték) sokkal jobb bemenet a Bayes-modellhez, mint a bináris `lh_test`.
- **Nyugalmi pulzus (RHR):** opcionális kiegészítő jel — szintén enyhén emelkedik ovuláció után, kevésbé érzékeny mint a BBT, de wearable-lel (Oura, Garmin, Apple Watch) éjszaka folyamatosan mérhető, ami zajtűrőbb jelet ad egyetlen reggeli pontmérésnél. Nem alapkövetelmény.
- **Cervikális nyák, görcs:** nincs eszköz, szubjektív napi bejegyzés.

## 3. Ciklushossz-statisztika és a "késés" becslése

Legyen a múltbeli ciklushosszak sorozata L₁, L₂, …, Lₙ (nap).

**Alapstatisztikák:**
- Átlag: L̄ = (ΣLᵢ) / n
- Szórás: s = √( Σ(Lᵢ−L̄)² / (n−1) )
- Medián + IQR — robusztusabb, mert egy anovulatorikus vagy stresszes ciklus nem húzza el annyira, mint az átlagot

**Recency-súlyozás (EWMA)** — a ciklus idővel driftelhet (stressz, életkor, testsúly):

```
Ĺ_t = α · L_t + (1−α) · Ĺ_(t−1)
ajánlott α = 0.25–0.3   (kb. az utolsó 4-5 ciklusnak ad nagy súlyt)
```

**"Késés" definíciója:** ne a naptári nap legyen a referencia, hanem a modell saját előző predikciója:

```
delay_i = actual_cycle_length_i − predicted_cycle_length_i   (az akkori modell szerint)
```

Ebből építs empirikus eloszlást (P10 / P50 / P90 percentilis) — ez adja meg ténylegesen, hogy "mennyit szokott késni" (pl. "80%-ban ±2 napon belül van, de volt már 6 nap késés is").

**Minimum adatigény:** 3–4 lezárt ciklus durva becsléshez, 6+ ciklus a személyre szabott konfidencia-intervallum stabilizálódásához. (A Natural Cycles is 1–3 ciklusos "tanulási" szakaszt használ, mielőtt szűkíti a predikciót.)

## 4. Ovuláció-becslés

### 4.1 A kulcs-inszájt: a luteális fázis a stabil, nem a follikuláris

A ciklus két szakasza eltérő varianciájú:
- **Follikuláris fázis** (menstruáció kezdete → ovuláció): ez variál cikluson belül, ez okozza a teljes ciklushossz ingadozását
- **Luteális fázis** (ovuláció → következő menstruáció): egyénenként jellemzően stabil, 11–17 nap közötti tartomány, átlagosan ~14 nap, egy adott nőnél tipikusan ±1–2 napos szórással

**Ebből következik a gyakorlati szabály:** ne előre számolj (ciklus kezdete + átlag/2 vagy "14. nap"), hanem **visszafelé**, a következő várható menstruációtól:

```
predicted_next_period   = last_period_start + Ĺ_t              (3. fejezet szerint)
predicted_ovulation_day = predicted_next_period − personal_luteal_length
```

`personal_luteal_length` = korábbi, BBT-vel megerősített ciklusok luteal_phase_length-jeinek átlaga. Ha nincs elég megerősített ciklus, populációs default: 14 nap, de jelöld alacsonyabb konfidenciával.

Ez lényegesen pontosabb, mint a legtöbb egyszerű naptár-app "14. napon ovulál" feltételezése, ami minden 28 naptól eltérő ciklushossznál rossz becslést ad.

### 4.2 BBT coverline / change-point algoritmus (retrospektív megerősítés)

Ez nem előre jelez, hanem utólag (1–3 nappal az esemény után) megerősíti, mikor történt ovuláció — ebből épül fel a `personal_luteal_length` historikus adatsor.

```
coverline = max(utolsó 6 alacsony BBT-érték a feltételezett emelkedés előtt)

ovuláció megerősítve, ha:
  3 egymást követő nap > coverline + 0.2°C
  (a teljes tipikus emelkedés ovuláció után ~0.3–0.5°C)
```

Robusztusabb változat: csúszóablakos t-próba (utolsó 3 nap átlaga vs. megelőző 6 nap átlaga), vagy CUSUM change-point detekció zajos/hiányos adatsorra.

### 4.3 Bayes-i személyre szabás (aktuális ciklusra)

- **Prior:** populációs eloszlás (cycle_length, luteal_length) párra — induló érték saját adat híján: cycle_length ~ Normal(28, 4 nap)
- **Frissítés:** minden lezárt saját ciklus szűkíti a posteriort (empirical Bayes / hierarchikus modell — a felhasználó saját paraméterei a populációs eloszlásból "húzott" random effect, ami egyre inkább a saját adatai felé tolódik)
- **Real-time jelek:** amint az aktuális ciklusban LH-pozitív, nyák-csúcs vagy BBT-emelkedés jön be, ez éles likelihood-frissítés, ami drasztikusan leszűkíti az ovuláció-napi posteriort a naptári becsléshez képest. Ez gyakorlatilag egy szekvenciális Bayes-szűrő, vagy HMM-ként modellezve: állapotok = menstruáció/follikuláris/ovulációs/luteális, megfigyelések = BBT, nyák, LH, görcs.

## 5. Menstruáció-predikció

```
predicted_next_period = last_period_start + Ĺ_t
konfidencia-sáv        = predicted_next_period ± 1×s   (kb. 68%-os sáv, s = 3. fejezet szórása)
```

Ha a tényleges menstruáció nem indul el a predikció + s felső határáig, és a BBT továbbra is emelkedett (ld. 8. fejezet) → terhesség-jelzés erősödik.

## 6. Kimenet formátuma — soha pontbecslés

Javasolt UI-kimenet:
- "Ovuláció: [nap−2, nap+1] között, ~70%-os konfidencia" — ne egyetlen dátum
- "Termékeny ablak: ovuláció becsült napja − 5 nap, + 1 nap" (6 napos ablak, ld. 7. fejezet)
- A konfidencia-sáv szélessége csökkenjen a lezárt ciklusok számával, és nőjön szabálytalan ciklusoknál

## 7. Fogamzási valószínűség közösülés-napokból

Wilcox et al. (1995, NEJM, 221 nő, vizeletalapú ovulációbecslés) empirikus adatai szerint a fogamzás kizárólag egy hat napos, az ovuláció napjával záruló ablakban történt közösülés esetén jött létre; a valószínűség 0,10 volt öt nappal ovuláció előtt, és 0,33 magán az ovuláció napján (a köztes napokra a valószínűség monoton nő eközött a két érték között).

Ebből egy nap-relatív kernel építhető (napi valószínűségi súlyok, ovuláció napjához képest −5-től 0-ig), amit a logolt `intercourse` napokra rá lehet vetíteni → "ebben a ciklusban becsült fogamzási esély: X%" kimenet.

Gyakorlati mellékhaszon:
- **BBT-konfound flag:** éjszakai aktivitás torzíthatja a reggeli mérést — hasonló kizárási logika, mint betegség/alkohol esetén (`bbt_excluded`)
- **Fogantatási dátum pontosítás:** pontos közösülési nap + megerősített ovuláció → pontosabb szülési határidő, mint az "utolsó menstruáció + 280 nap" (ami 14. napi ovulációt feltételez, szabálytalan ciklusnál rossz becslés)

## 8. Terhesség-jelző mintázatok (csak jelzés, nem diagnózis)

- **Trifázisos BBT-minta:** a luteális fázis végén (kb. ovuláció + 7–10. nap) egy harmadik, kisebb emelkedés a második plató fölé
- **Megnyúlt luteális fázis:** ha a BBT a `personal_luteal_length` + 3 nap fölött is emelkedett marad, és nincs vérzés → hCG-teszt javasolt
- A szoftver kimenete mindig "érdemes tesztelni" jellegű jelzés legyen, sose "terhes vagy" állítás

## 9. Edge case-ek és adatminőség

- **Anovulatorikus ciklus:** nincs bifázisos BBT-mintázat → kizárva a `luteal_phase_length` számításból, de bennmarad a `cycle_length` átlagban (esetleg lecsökkentett súllyal, outlierként)
- **Szabálytalan ciklusok** (PCOS, posztpartum, fogamzásgátlóról leszokás után): magas szórás esetén csökkentsd a naptár-komponens súlyát a Bayes-i modellben, támaszkodj inkább a biomarkerekre (BBT/LH/nyák), és mutass szélesebb konfidencia-sávot
- **Hiányzó napok:** ne interpoláld a BBT-t (félrevezető) — inkább hagyd ki az adott napot a coverline-számításból

## 10. Validált referenciaszámok

| Adat | Érték | Forrás |
|---|---|---|
| Symptothermal (Sensiplan) Pearl Index | 0,4 (perfect use) / 1,8 (typical use) | Sensiplan, klinikai irodalom |
| BBT tipikus emelkedés ovuláció után | ~0,3–0,5°C | klinikai standard |
| Luteális fázis hossza | 11–17 nap, átlag ~14 | reproduktív élettan |
| Termékeny ablak hossza | 6 nap, ovuláció napjával zárva | Wilcox et al. 1995, NEJM |
| Fogamzás valószínűsége (nap−5 / nap 0) | 0,10 / 0,33 | Wilcox et al. 1995, NEJM |
| Natural Cycles hatékonyság | 98% (perfect) / 93% (typical use) | gyártói dokumentáció |
| Natural Cycles tanulási szakasz | 1–3 ciklus | gyártói dokumentáció |

## 11. Implementációs megjegyzés

A `daily_log` és `cycle_summary` séma közvetlenül átvihető Postgres táblákba. A coverline-számítás és a Bayes-frissítés egy háttérszolgáltatásban futtatható ciklus lezárásakor (hasonló mintára, mint a `TorzsFreshnessWorker`) — a napi log inzertje triggereli a `cycle_summary` újraszámítását, ha `flow_intensity` új ciklus kezdetét jelzi.

### 11.1 Architektúra: egyfelhasználós, self-hosted, auth nélkül

Mivel az app egyszemélyes, nincs szükség `user` táblára, `user_id` foreign key-ekre sehol a sémában, sem munkamenet-/auth-logikára a backendben — ez érdemben egyszerűsíti a kódot.

Ugyanakkor egészségügyi és szexuális adatról van szó (GDPR 9. cikk szerinti különleges kategória) — az app-szintű login hiánya csak akkor biztonságos, ha a hálózati rétegen van védelem. Illeszkedve a meglévő self-hosted stackhez: Cloudflare Tunnel + Access policy elé rakva, ahol az Include szabály konkrét email-listára szól (2 cím: a tiéd + a feleségedé), hardware key/biometric MFA-val kikényszerítve (ugyanaz a minta, mint az `ssh.kellner.dev`-nél vagy a Dozzle-nál) — így az app maga tényleg nulla auth-kóddal mehet, a hozzáférés-vezérlés és az audit log (melyik email nézte/módosította mikor) teljesen az edge-en történik. Adatbázis-szinten érdemes disk-encryption vagy legalább a Postgres-kötet titkosítása, mivel ez a legérzékenyebb adatkategória, amit eddig ezen a stacken tároltál.

## 12. Claude Design prompt (UI terveztetéshez)

```
Tervezz egy mobile-first webalkalmazást egy párnak, önálló (self-hosted)
ciklus- és fogamzás-nyomkövetéshez. A háttérben egy nő napi tünetnaplója
(testhő, cervikális nyák, LH-teszt, görcs, közösülés) alapján ovulációt
és menstruációt jelez előre, illetve fogamzási esélyt számol az adott
ciklusra.

Kontextus: privát, otthoni self-hosted eszköz, kettejük (a pár) számára,
hálózati szinten már védett — nincs login/regisztrációs képernyő,
egyenesen az appba lép be a felhasználó.

Hero-nézet — Ma / gyors napló: ez a leggyakoribb interakció (minden
reggel, pár másodperc alatt kitöltve), ezért ez kapja a legtöbb
UX-figyelmet. Mezők: testhő (2 tizedesjegyű számbevitel), cervikális
nyák (4 kategória), LH-teszt (negatív/pozitív/csúcs), görcs (típus +
0-3 intenzitás), folyásintenzitás, közösülés (egyszerű kapcsoló). Nagy
érintési felületek, minimál gépelés, egy koppintásos mentés.

Ciklus áttekintő: hol tart a ciklusban, az ovuláció becsült ablaka és a
következő menstruáció becsült dátuma. Ezeket mindig tartományként/
konfidencia-sávként jelenítsd meg, SOHA egyetlen pontdátumként — ez
funkcionális követelmény, nem csak stílus, mert a becslés matematikailag
is bizonytalansággal jár.

Történet/trendek: BBT-görbe idővonalon (coverline-nal jelölve, mikor
erősödött meg az ovuláció), ciklushossz historikus oszlopdiagram.

Fogamzási esély: a logolt közösülési napokból számított esély az adott
ciklusra — lehet önálló nézet vagy kártya a ciklus-áttekintőn belül,
ahogy jobban illik a layouthoz.

Mobile-first, de asztali gépen is jól használható legyen szélesebb
elrendezéssel (pl. a történet nézet két hasábban, grafikonoknak több
hellyel). UI szövegek magyarul.

Ne generálj backendet, adatbázist vagy auth-logikát — ez csak a
vizuális/interakciós réteg, amit utána Claude Code-dal kötünk össze a
valós API-val.

Hangulat: nyugodt, privát, megbízható — se a tipikus pasztell-rózsaszín
"period app" giccs, se steril klinikai fehér-kék. Egyedi, saját
karaktert adj neki.
```

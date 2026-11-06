# Catalog de tipuri

`offense.json` (unități ofensive) și `defense.json` (sisteme defensive): câte un obiect per tip, în `{ "types": [ ... ] }`.

Un scenariu referă un tip în loc să repete valorile:

```json
"waves":    [ { "id": "W1", "unit": "shahed-136", "siteId": "L1", "targetId": "T1", "count": 300 } ]
"defenses": [ { "id": "S1", "type": "iris-t-slm", "latDeg": 44.43, "lonDeg": 26.10, "magazine": 16 } ]
```

* Câmpurile tipului se copiază în intrare; **ce scrie scenariul peste ele are prioritate** (ex. `magazine`, `rangeKm`, `name`).
* La încărcare scenariul e **rezolvat**: `scenario.json` din `runs/<id>/` conține valorile finale, nu doar id-ul tipului, deci o rulare veche rămâne reproductibilă chiar dacă editezi catalogul.
* `id`, `notes` și `source` sunt informative și nu se copiază. `source` arată de unde vine cifra (sau „estimare ilustrativă, neverificată”).
* Un val are `rangeKm` (raza de acțiune): scenariul e respins dacă ținta e mai departe.
* Defensivele: `kind` ∈ radar, sam, aaGun, abm, patrol, interceptorBase, airbase. `engages` (ce tipuri de ținte poate angaja), `pkByKind` (Pk pe tip de țintă), `minAltM`/`maxAltM`, `pkAltitude` (curbă opțională: puncte `{altM, factor}` crescătoare după altitudine, factor 0–1; Pk-ul fiecărei trageri se înmulțește cu factorul la altitudinea țintei; valorile din catalog sunt ilustrative).
* La tunuri, un „tir” = o rafală de 1 s (nu un proiectil).

**Atenție la date:** doar cifrele cu `source` către o pagină (Wikipedia etc.) au fost verificate, și ele sunt aproximative (intervale în sursă). Pk, încărcătoare, cadențe și razele efective ale apărării sunt estimări ilustrative.

## Doctrină de angajare (opțional, pe sistem defensiv)

Implicit un sistem trage în tot ce poate. Aceste câmpuri îi dau reguli:

| câmp | efect |
|---|---|
| `primaryKinds` | țintele „principale”: se angajează mereu, pot folosi și rezerva, primesc `maxRoundsPerTarget` tiruri. Implicit: ABM = balistice; restul = tot ce nu e dronă |
| `maxCostRatio` | o țintă de oportunitate se angajează doar dacă `shotCostUsd <= maxCostRatio * costul țintei` (0 = fără limită; costuri necunoscute = fără limită) |
| `leakerDistanceKm` | „scăpătoare”: o țintă prea ieftină se angajează totuși dacă mai are cel mult atâția km până la impact |
| `reserveRounds` | ultimele N tiruri rămân doar pentru țintele principale |
| `maxRoundsPerTarget` | tiruri pe rând într-o țintă principală, oprire la prima lovitură (trage-privește-trage) |
| `priority` | `imminent` (cea mai aproape de impact) sau `threat` (balistic > croazieră > avion > dronă, apoi cea mai aproape) |

La nivel de scenariu: `"cheapestFirst": true` = într-un ciclu trag întâi sistemele cu cost/tir mic.

## Vulnerabilitate (contra-jocul atacatorului)

Un impact distruge un sistem defensiv dacă cade la cel mult `sizeM` + raza letală (≈ 6·∛kg) de el, cu probabilitatea `1 − exp(−kg / hardnessKg)`.
Ținta unui val se pune pur și simplu pe coordonatele sistemului (un `target` nou). Implicit, după tip: radar 40 kg, SAM 80, tun 60, ABM 100,
bază de interceptoare 50, bază aeriană 5000 (mărime 600 m); avioanele de patrulă nu pot fi distruse. Un sistem distrus nu mai detectează / trage;
o bază aeriană distrusă lasă avioanele de patrulă fără loc de reîncărcare. Vezi `scenarios/example-counter.json`.

## Valuri cu mai multe lansări / ținte și căderi pe traseu

* `siteIds` / `targetIds` (în loc de `siteId` / `targetId`): fiecare unitate își alege la întâmplare un punct de lansare / o țintă din listă (reproductibil, din seed). Un id repetat are pondere mai mare, așa se fac mai multe puncte de impact într-un oraș sau lansări din mai multe direcții. Raza de acțiune se verifică pe cea mai îndepărtată pereche lansare → țintă.
* `failureRate` (0–1, în tipurile ofensive): probabilitatea ca unitatea să cadă pe traseu, într-un punct aleator între 5% și 95% din drum (eveniment `crashed`, nu `impact`; nu produce victime și nu distruge sisteme).
* Scenariile mari `ru-vs-ua.json` și `ua-vs-ru.json` sunt generate de `tools/make_ukraine_scenarios.py` (poziții aproximative, efective ilustrative; rulează scriptul ca să le regenerezi după ce schimbi catalogul).

## Război electronic (bruiaj)

`kind: "ew"` (cu `rangeKm` și `jamRatePerMinute`): orice dronă sau rachetă de croazieră aflată în rază își pierde ghidarea cu rata λ = `jamRatePerMinute` × sensibilitatea ei (probabilitate într-un minut = 1 − e^(−λ)); unitatea cade pe loc (eveniment `jammed`). Nu cere radar și nu are muniție. Sensibilitatea vine din `ewResistance` al tipului ofensiv (0 = complet sensibil, 1 = imun: Iskander, avioane; Kalibr / Kh-101 ~0,85; Geran-3 ~0,6; Shahed ~0,15). Sistemele de bruiaj pot fi distruse la impact ca restul (rezistență implicită 30 kg).

## Rețea de radare (`localTracking`)

Implicit (`localTracking: false`) detecția e globală și permanentă: orice radar care a văzut o țintă o arată tuturor, pentru totdeauna.
Cu `"localTracking": true` în scenariu:

- un sistem care are nevoie de pistă (SAM, ABM, bază de interceptoare, patrulă) trage doar dacă **un radar intact**, aflat la cel mult `linkRangeKm` (implicit 150) de el, vede ținta **în acest moment** (rază + orizont radar);
- `selfTrackKm` = raza radarului propriu al sistemului (Pantsir, Tor, grupuri mobile, Patriot...): în această rază își vede singur țintele, chiar dacă rețeaua a căzut;
- tunurile antiaeriene nu au nevoie de pistă (ochi proprii), ca până acum;
- distrugerea unui radar (contra-jocul atacatorului) lasă fără pistă sistemele care depindeau de el.

Evenimentul `Detected` rămâne unul singur per țintă (prima detecție).

## Rulări multiple (batch)

```
dotnet run --project src/Sim.Server -- batch scenario=ru-vs-ua runs=30 seed=1
```

Rulează seed-urile `seed .. seed+runs-1` în paralel (opțional `threads=4`, `out=<folder>`), afișează media, abaterea standard, min / P5 / mediana / P95 / max pe fiecare metrică și salvează `runs/batch-<scenariu>-<seed>-<runs>.json` și `.csv`. Același scenariu + seed + versiune de motor => același raport.

## Bruiaj: scădere cu distanța și baleiaj

- `jamFalloff` (0–1, pe sistemul `ew`): rata efectivă = `jamRatePerMinute × (1 − jamFalloff × distanță / rangeKm)`. 0 = constantă în toată raza; 1 = zero la margine. `ew-mobile` și `ew-fixed` au 0,5 (ratele au fost mărite cu ~1/3 ca media să rămână aproape la fel).
- Baleiaj de sensibilitate: `dotnet run --project src/Sim.Server -- batch scenario=ru-vs-ua runs=30 jam=0.5,1,1.5` înmulțește rata TUTUROR sistemelor `ew` cu fiecare factor, rulează seria pentru fiecare și scrie `runs/sweep-jam-<scenariu>-<seed>-<rulări>.csv`.

## Momeli, redirectionare, saturarea bruiajului

- **Momeli** (`decoy`, `perceivedCostUsd` pe val): apărarea nu știe ce e momeală. Doctrina de cost (`maxCostRatio`) folosește costul *perceput*, deci o Gerbera de 10 000 USD care „arată” ca un Shahed (35 000) atrage rachete scumpe. Se numără tirurile irosite pe momeli (`DecoyShots` în batch).
- **Redirectionare** (`retarget`, `retargetAtKm` = 40, `retargetRangeKm` = 150 pe val; `impactsToDestroy` pe țintă): o țintă cu `impactsToDestroy` > 0 e „distrusă” după atâtea impacturi. O unitate dintr-un val cu `retarget` care ajunge la `retargetAtKm` de o țintă distrusă alege la întâmplare altă țintă nedistrusă din lista valului, aflată la cel mult `retargetRangeKm`, și își continuă drumul de acolo (o singură dată; evenimentul `retargeted`). Rachetele balistice nu se redirecționează.
- **Saturare** (`jamCapacity` pe sistemul `ew`): peste atâtea unități simultan în rază, eficiența se împarte (capacitate / unități în rază), deci un bruior face cel mult `capacitate × rata` pierderi pe minut. 0 = fără limită. `ew-mobile` 6, `ew-fixed` 20.

## Diagnostice în `batch`

Pe lângă rezultate, seria raportează: costul tirurilor apărării (`shotCostUsd` însumat) și cât din el s-a dus pe momeli, costul unităților atacatorului, procentul de cicluri în care bruiajele cu `jamCapacity` au fost saturate și încărcarea maximă a unui bruior (unități în rază / capacitate; > 1 înseamnă saturare).

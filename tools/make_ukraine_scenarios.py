#!/usr/bin/env python3
"""
Generates the large Ukraine <-> Russia scenarios (scenarios/ru-vs-ua.json and scenarios/ua-vs-ru.json).
Only types from the catalog + coordinates; positions are approximate and force sizes are illustrative, not real data.
Deterministic: the same output on every run (fixed seed). Checks the operational range of the waves before writing.

  python3 tools/make_ukraine_scenarios.py
"""
import json, math, random, pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
R_KM = 6371.0088


def gc_km(a, b):
    la1, lo1, la2, lo2 = map(math.radians, (a[0], a[1], b[0], b[1]))
    h = math.sin((la2 - la1) / 2) ** 2 + math.cos(la1) * math.cos(la2) * math.sin((lo2 - lo1) / 2) ** 2
    return 2 * R_KM * math.asin(math.sqrt(h))


def dest(lat, lon, bearing_deg, dist_km):
    la, lo, br, d = math.radians(lat), math.radians(lon), math.radians(bearing_deg), dist_km / R_KM
    la2 = math.asin(math.sin(la) * math.cos(d) + math.cos(la) * math.sin(d) * math.cos(br))
    lo2 = lo + math.atan2(math.sin(br) * math.sin(d) * math.cos(la), math.cos(d) - math.sin(la) * math.sin(la2))
    return round(math.degrees(la2), 4), round(math.degrees(lo2), 4)


def load_catalog(name):
    return {t["id"]: t for t in json.load(open(ROOT / "data" / "catalog" / f"{name}.json", encoding="utf-8"))["types"]}


def map_codes(map_name):
    """The ISO2 codes of the given country map (europe / world)."""
    gj = json.load(open(ROOT / "data" / "countries" / f"{map_name}.geojson", encoding="utf-8"))
    return {f["properties"]["iso2"] for f in gj["features"]}


def nato_allies(map_name, exclude=(), extra=(), role="defenderAlly"):
    """Toate statele NATO prezente pe harta (minus `exclude`) + `extra` (ex. UA, MD), cu rolul dat."""
    alliances = json.load(open(ROOT / "data" / "alliances.json", encoding="utf-8"))["alliances"]
    members = next(a for a in alliances if a["id"] == "nato")["members"]
    present = map_codes(map_name)
    return {c: role for c in [*members, *extra] if c in present and c not in exclude}


OFFENSE = load_catalog("offense")
DEFENSE = load_catalog("defense")


class Builder:
    def __init__(self, seed):
        self.rng = random.Random(seed)
        self.sites = {}      # id -> (lat, lon, name)
        self.targets = {}    # id -> dict
        self.waves = []
        self.defenses = []
        self.counter = {}

    # ---- launches / targets ----
    def site(self, sid, name, lat, lon):
        self.sites[sid] = (lat, lon, name)

    def target(self, tid, name, lat, lon, kind="city", radius=0, destroy=None):
        t = {"id": tid, "name": name, "kind": kind, "latDeg": lat, "lonDeg": lon}
        if radius:
            t["radiusM"] = radius
        if kind == "military_base":
            t["impactsToDestroy"] = destroy if destroy is not None else 10
        self.targets[tid] = t

    # ---- waves ----
    def wave(self, wid, unit, sites, targets, count, start=0, interval=5, spread=200, arrive_at=None, **extra):
        """sites / targets: dict id -> weight (int). arrive_at: the time (s) at which they arrive on average; it determines the start."""
        site_list = [s for s, w in sites.items() for _ in range(w)]
        target_list = [t for t, w in targets.items() for _ in range(w)]
        spec = OFFENSE[unit]
        # operational range: ALL launch -> target pairs are checked
        worst = max(gc_km(self.sites[s][:2], (self.targets[t]["latDeg"], self.targets[t]["lonDeg"])) for s in sites for t in targets)
        if worst * 1.01 > spec["rangeKm"]:
            raise SystemExit(f"{wid}: {unit} has a range of {spec['rangeKm']} km, but one pair is {worst:.0f} km apart")
        if arrive_at is not None:
            mean = sum(gc_km(self.sites[s][:2], (self.targets[t]["latDeg"], self.targets[t]["lonDeg"]))
                       for s in site_list for t in target_list[:: max(1, len(target_list) // 20)]) \
                / (len(site_list) * len(target_list[:: max(1, len(target_list) // 20)]))
            flight = mean * 1000.0 / spec["speedMps"]
            start = max(0, round(arrive_at - flight - count * interval / 2))
        self.waves.append({
            "id": wid, "unit": unit, "siteIds": site_list, "targetIds": target_list, "count": count,
            "startTimeSeconds": start, "intervalSeconds": interval, "spreadMeters": spread, **extra})

    # ---- defense ----
    def defense(self, prefix, typ, name, lat, lon, **extra):
        n = self.counter.get(prefix, 0) + 1
        self.counter[prefix] = n
        d = {"id": f"{prefix}{n:02d}", "type": typ, "name": f"{name} {n:02d}" if extra.pop("numbered", False) else name,
             "latDeg": round(lat, 4), "lonDeg": round(lon, 4)}
        d.update(extra)
        self.defenses.append(d)

    def ring(self, prefix, typ, name, center, n, r_min, r_max, **extra):
        a0 = self.rng.uniform(0, 360)
        for i in range(n):
            lat, lon = dest(center[0], center[1], a0 + i * 360.0 / n + self.rng.uniform(-8, 8), self.rng.uniform(r_min, r_max))
            self.defense(prefix, typ, name, lat, lon, numbered=True, **extra)

    def retarget(self, *wave_ids):
        """The given waves may change target in flight if their target is already destroyed (see Target.impactsToDestroy)."""
        for w in self.waves:
            if w["id"] in wave_ids:
                w["retarget"] = True

    def strike_targets(self, defense_ids, prefix="X"):
        """Tinte (kind military_base) peste pozitiile sistemelor defensive date; intoarce {id_tinta: 1}."""
        by_id = {d["id"]: d for d in self.defenses}
        out = {}
        for i, did in enumerate(defense_ids, 1):
            d = by_id[did]
            tid = f"{prefix}{i:02d}"
            self.target(tid, f"Strike on: {d['name']}", d["latDeg"], d["lonDeg"], "military_base", 50, destroy=3)
            out[tid] = 1
        return out

    def estimate_ew(self, samples=150):
        """Rough estimate (straight lat/lon route): what share of the units is neutralized by failures + jamming, before the fire defense."""
        ew = [(d, DEFENSE[d["type"]]) for d in self.defenses if DEFENSE[d["type"]]["kind"] == "ew"]
        total = lost = 0.0
        for w in self.waves:
            spec = OFFENSE[w["unit"]]
            res = spec.get("ewResistance", 0.0)
            kind_hit = spec["kind"] in ("drone", "cruiseMissile")
            sum_lost = 0.0
            for _ in range(samples):
                site = self.sites[self.rng.choice(w["siteIds"])][:2]
                tg = self.targets[self.rng.choice(w["targetIds"])]
                tgt = (tg["latDeg"], tg["lonDeg"])
                dist = gc_km(site, tgt)
                n = max(10, int(dist / 3.0))
                lam = 0.0
                if kind_hit:
                    for d, t in ew:
                        r = d.get("rangeKm", t["rangeKm"])
                        rate = d.get("jamRatePerMinute", t["jamRatePerMinute"])
                        fall = d.get("jamFalloff", t.get("jamFalloff", 0.0))
                        inside = 0.0
                        for k in range(n):
                            f = (k + 0.5) / n
                            dd = gc_km((site[0] + (tgt[0] - site[0]) * f, site[1] + (tgt[1] - site[1]) * f), (d["latDeg"], d["lonDeg"]))
                            if dd <= r:
                                inside += 1.0 - fall * dd / r
                        lam += rate * (1.0 - res) * inside * (dist / n) * 1000.0 / spec["speedMps"] / 60.0
                survive = math.exp(-lam) * (1.0 - spec.get("failureRate", 0.0))
                sum_lost += 1.0 - survive
            frac = sum_lost / samples
            total += w["count"]
            lost += frac * w["count"]
            print(f"   {w['id']:4s} {w['unit']:14s} x{w['count']:<4d} neutralized by failures + jamming: {frac:5.0%}")
        print(f"   TOTAL: {lost / total:.0%} neutralized before the fire defense")

    def scenario(self, name, countries, duration, dt=0.5):
        return {
            "name": name, "dtSeconds": dt, "durationSeconds": duration, "defenseIntervalSeconds": 1.0, "cheapestFirst": True, "localTracking": True,
            "airspaceMap": "europe", "countries": countries,
            "launchSites": [{"id": k, "name": v[2], "latDeg": v[0], "lonDeg": v[1], "altM": 0} for k, v in self.sites.items()],
            "targets": list(self.targets.values()), "waves": self.waves, "defenses": self.defenses}


def save(builder, filename, **kw):
    sc = builder.scenario(**kw)
    path = ROOT / "scenarios" / filename
    path.write_text(json.dumps(sc, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
    drones = sum(w["count"] for w in sc["waves"])
    print(f"{filename}: {drones} units launched, {len(sc['targets'])} targets, {len(sc['launchSites'])} launch points, {len(sc['defenses'])} defense systems")


# 1) Russia -> Ukraine
# 1) Rusia -> Ucraina
# =====================================================================================================
def russia_vs_ukraine():
    b = Builder(seed=2026)

    for sid, name, lat, lon in [
        ("L01", "Primorsko-Akhtarsk", 46.05, 38.17), ("L02", "Cape Chauda (Crimea)", 45.00, 35.80),
        ("L03", "Millerovo", 48.95, 40.30), ("L04", "Navlya (Briansk)", 52.83, 34.50),
        ("L05", "Orel", 52.97, 36.07), ("L06", "Kursk", 51.70, 36.20), ("L07", "Shatalovo (Smolensk)", 54.20, 32.40),
        ("L08", "Black Sea (ships)", 43.80, 33.50), ("L09", "Launch area Kh-101 (Volga)", 51.80, 46.50),
        ("L10", "Briansk (Iskander)", 52.10, 33.20), ("L11", "Crimea (Iskander)", 45.40, 34.30), ("L12", "Belgorod (Iskander)", 50.60, 36.30),
        ("L13", "Airfield Belgorod", 50.64, 36.59),
    ]:
        b.site(sid, name, lat, lon)

    # --- targets ---
    kyiv = {  # id: (name, lat, lon, kind, radius, weight)
        "K01": ("Kyiv - government center", 50.4501, 30.5234, "city", 0, 3),
        "K02": ("Kyiv - Darnytsia", 50.4300, 30.6500, "city", 0, 2),
        "K03": ("Kyiv - Obolon", 50.5200, 30.5000, "city", 0, 2),
        "K04": ("Kyiv - Troieshchyna", 50.5200, 30.6100, "city", 0, 1),
        "K05": ("Kyiv - Sviatoshyn", 50.4550, 30.3700, "city", 0, 2),
        "K06": ("Kyiv - Lipki", 50.4250, 30.5400, "city", 0, 2),
        "K07": ("Kyiv - Podil", 50.4650, 30.5150, "city", 0, 1),
        "K08": ("Kyiv - Teremky", 50.3700, 30.4800, "city", 0, 1),
        "K09": ("CET-5 Kyiv", 50.4000, 30.6600, "military_base", 800, 2),
        "K10": ("CET Trypilska", 50.1000, 30.7500, "military_base", 800, 1),
        "K11": ("Airfield Vasylkiv", 50.2200, 30.3000, "military_base", 800, 1),
        "K12": ("Boryspil", 50.3450, 30.8940, "military_base", 800, 1),
    }
    others = {
        "H01": ("Kharkiv - center", 49.9935, 36.2304, "city", 0), "H02": ("Kharkiv - Saltivka", 50.0400, 36.3000, "city", 0),
        "D01": ("Dnipro", 48.4647, 35.0462, "city", 0), "D02": ("Dnipro - industrial area", 48.4000, 34.9500, "military_base", 800),
        "O01": ("Odesa - port", 46.4900, 30.7400, "military_base", 800), "O02": ("Odesa - center", 46.4825, 30.7233, "city", 0),
        "Z01": ("Zaporizhzhia", 47.8388, 35.1396, "city", 0), "Z02": ("Zaporizhzhia - hydroelectric plant", 47.8700, 35.0900, "military_base", 800),
        "V01": ("Lviv", 49.8397, 24.0297, "city", 0), "V02": ("CET Burshtyn", 49.2100, 24.6600, "military_base", 800),
        "P01": ("Poltava", 49.5883, 34.5514, "city", 0), "P02": ("Kremenchuk refinery", 49.0700, 33.4200, "military_base", 800),
        "M01": ("Mykolaiv", 46.9750, 31.9946, "city", 0),
        "S01": ("Sumy", 50.9077, 34.7981, "city", 0), "C01": ("Chernihiv", 51.4982, 31.2893, "city", 0),
    }
    for tid, (n, la, lo, kind, rad, _w) in kyiv.items():
        b.target(tid, n, la, lo, kind, rad)
    for tid, (n, la, lo, kind, rad) in others.items():
        b.target(tid, n, la, lo, kind, rad)

    kyiv_all = {t: kyiv[t][5] for t in kyiv}
    kyiv_city = {t: kyiv[t][5] for t in kyiv if kyiv[t][3] == "city"}
    kyiv_energy = {"K09": 3, "K10": 2, "K11": 1, "K12": 1}

    # --- waves (missile arrival ~ 4h, when the slow drones are already over their targets) ---
    ARRIVE = 15000
    b.wave("WS1", "shahed-136", {"L01": 2, "L02": 1, "L03": 1},
           {**{t: w * 3 for t, w in kyiv_all.items()}, "D01": 4, "Z01": 3, "O02": 3, "P01": 3, "D02": 1}, 420, start=0, interval=5)
    b.wave("WS2", "shahed-136", {"L04": 2, "L05": 1, "L06": 1, "L07": 1},
           {**{t: w * 3 for t, w in kyiv_all.items()}, "H01": 5, "H02": 3, "C01": 3, "S01": 2, "V01": 4}, 300, start=300, interval=5)
    b.wave("WS3", "shahed-136", {"L02": 2, "L01": 1},
           {"O01": 3, "O02": 3, "M01": 3, "Z01": 3, "Z02": 1, "D01": 3, **{t: 1 for t in kyiv_city}}, 180, start=600, interval=5)
    b.wave("WG2", "geran-2-90kg", {"L04": 1, "L05": 1, "L06": 1, "L07": 1},
           {**{t: w for t, w in kyiv_all.items()}, "H01": 3, "P01": 2, "S01": 1, "C01": 1}, 300, start=900, interval=4)
    b.wave("WD", "gerbera", {"L04": 1, "L05": 1, "L06": 1, "L07": 1},
           {**{t: w for t, w in kyiv_city.items()}, "H01": 3, "C01": 2, "S01": 1, "P01": 2}, 642, start=0, interval=3, spread=400)
    b.wave("WG3", "geran-3", {"L04": 1, "L06": 1, "L07": 1},
           {**{t: w for t, w in kyiv_all.items()}, "D01": 2, "V01": 2}, 60, start=0, interval=10, spread=100, arrive_at=ARRIVE - 1500)
    b.wave("WK", "kalibr-3m14", {"L08": 1},
           {"O01": 3, "O02": 1, "M01": 2, "D01": 2, "Z02": 1, **{t: 1 for t in ("K09", "K01", "K10")}}, 24, interval=12, spread=60, arrive_at=ARRIVE)
    b.wave("WH", "kh-101", {"L09": 1},
           {**kyiv_energy, "V02": 3, "P02": 2, "V01": 1, "K01": 1}, 40, interval=15, spread=60, arrive_at=ARRIVE + 600)
    b.wave("WI1", "iskander-m", {"L10": 1}, {**kyiv_energy, "K01": 2, "K06": 1, "C01": 1}, 10, interval=20, spread=80, arrive_at=ARRIVE + 900)
    b.wave("WI2", "iskander-m", {"L11": 1}, {"O01": 2, "M01": 1, "Z02": 1, "D02": 1}, 6, interval=20, spread=80, arrive_at=ARRIVE + 900)
    b.wave("WI3", "iskander-m", {"L12": 1}, {"H01": 2, "H02": 1, "P01": 1, "S01": 1}, 6, interval=20, spread=80, arrive_at=ARRIVE + 900)
    b.wave("WA", "su-34-fab500", {"L13": 1}, {"H01": 2, "H02": 2}, 12, interval=30, spread=200, arrive_at=ARRIVE + 300)

    # --- Ukrainian defense ---
    city_center = {"kyiv": (50.45, 30.52), "kharkiv": (49.99, 36.23), "odesa": (46.48, 30.73), "dnipro": (48.46, 35.05),
                   "zap": (47.84, 35.14), "lviv": (49.84, 24.03), "poltava": (49.59, 34.55), "mykolaiv": (46.97, 31.99),
                   "sumy": (50.91, 34.80), "chernihiv": (51.50, 31.29)}

    # radars: coverage around cities + an early-warning line
    for lat, lon, nm in [(50.45, 30.50, "Radar Kyiv"), (50.22, 30.31, "Radar Vasylkiv"), (51.30, 32.10, "Radar North-East"),
                         (50.60, 35.00, "Radar Sumy"), (49.95, 36.30, "Radar Kharkiv"), (47.80, 35.20, "Radar Zaporizhzhia"),
                         (46.50, 30.70, "Radar Odesa"), (49.80, 24.10, "Radar Lviv"), (48.40, 35.10, "Radar Dnipro"),
                         (49.60, 34.50, "Radar Poltava"), (47.00, 32.00, "Radar Mykolaiv"), (50.00, 28.00, "Radar Center-West"),
                         (48.90, 38.50, "Radar East"), (47.50, 37.50, "Radar South-East")]:
        b.defense("R", "radar-ew-long" if "North" in nm or "East" in nm or "West" in nm else "trml-4d", nm, lat, lon)
    b.defense("R", "an-mpq-65", "Radar Patriot Kyiv North", 50.56, 30.46)
    b.defense("R", "an-mpq-65", "Radar Patriot Kyiv South", 50.34, 30.56)

    b.defense("PT", "patriot-pac3-mse", "Patriot Kyiv North", 50.55, 30.45)
    b.defense("PT", "patriot-pac3-mse", "Patriot Kyiv South", 50.35, 30.55)
    b.defense("PT", "patriot-pac3-mse", "Patriot Odesa", 46.45, 30.75)
    for lat, lon, nm in [(50.50, 30.60, "NASAMS Kyiv"), (46.50, 30.65, "NASAMS Odesa"), (49.85, 24.05, "NASAMS Lviv"), (48.45, 35.05, "NASAMS Dnipro")]:
        b.defense("NS", "nasams", nm, lat, lon)
    for lat, lon, nm in [(50.40, 30.45, "IRIS-T Kyiv West"), (50.50, 30.40, "IRIS-T Kyiv North"), (50.00, 36.20, "IRIS-T Kharkiv"),
                         (47.85, 35.15, "IRIS-T Zaporizhzhia"), (46.97, 32.00, "IRIS-T Mykolaiv"), (49.60, 34.55, "IRIS-T Poltava"),
                         (46.50, 30.80, "IRIS-T Odesa")]:
        b.defense("IT", "iris-t-slm", nm, lat, lon)

    b.ring("GP", "gepard-35mm", "Gepard Kyiv", city_center["kyiv"], 8, 9, 15)
    b.ring("GP", "gepard-35mm", "Gepard Odesa", city_center["odesa"], 2, 5, 9)
    b.ring("GP", "gepard-35mm", "Gepard Kharkiv", city_center["kharkiv"], 2, 5, 9)

    for key, n, r0, r1 in [("kyiv", 30, 3, 16), ("kharkiv", 8, 3, 12), ("odesa", 8, 3, 10), ("dnipro", 6, 3, 10), ("zap", 6, 3, 10),
                           ("lviv", 6, 3, 10), ("poltava", 4, 3, 8), ("mykolaiv", 4, 3, 8), ("sumy", 3, 3, 7), ("chernihiv", 3, 3, 7)]:
        b.ring("MG", "mobile-fire-group", f"Mobile group {key.capitalize()}", city_center[key], n, r0, r1)

    for key, n in [("kyiv", 3), ("kharkiv", 1), ("odesa", 1), ("dnipro", 1), ("lviv", 1), ("poltava", 1), ("mykolaiv", 1)]:
        b.ring("ST", "sting-interceptor", f"Sting {key.capitalize()}", city_center[key], n, 6, 14, rangeKm=40)

    for lat, lon, nm in [(50.21, 30.31, "Base Vasylkiv"), (49.75, 27.27, "Base Starokostiantyniv"), (49.97, 33.62, "Base Myrhorod"),
                         (48.88, 24.69, "Base Ivano-Frankivsk"), (46.93, 32.00, "Base Kulbakino")]:
        b.defense("AB", "airbase", nm, lat, lon)
    for typ, nm, lat, lon in [("f-16-patrol", "F-16 Kyiv", 50.40, 30.50), ("f-16-patrol", "F-16 West", 49.90, 25.50),
                              ("f-16-patrol", "F-16 East", 49.90, 35.50), ("mig-29-patrol", "MiG-29 South", 46.70, 31.00),
                              ("mig-29-patrol", "MiG-29 Center", 49.50, 31.50), ("mig-29-patrol", "MiG-29 Zaporizhzhia", 47.90, 34.80)]:
        b.defense("PA", typ, nm, lat, lon)

    # --- electronic warfare (jamming) ---
    b.ring("EM", "ew-mobile", "Mobile jammer Kyiv (inner)", city_center["kyiv"], 10, 3, 14)
    b.ring("EM", "ew-mobile", "Mobile jammer Kyiv (near)", city_center["kyiv"], 14, 14, 50)
    for key, n, r0, r1 in [("kharkiv", 6, 5, 30), ("odesa", 6, 5, 30), ("dnipro", 5, 5, 25), ("zap", 5, 5, 25), ("lviv", 4, 5, 25),
                           ("poltava", 3, 5, 20), ("mykolaiv", 3, 5, 20), ("sumy", 2, 5, 15), ("chernihiv", 3, 5, 20)]:
        b.ring("EM", "ew-mobile", f"Mobile jammer {key.capitalize()}", city_center[key], n, r0, r1)
    for lat, lon, nm in [(51.50, 34.00, "Fixed jammer NE"), (50.50, 35.80, "Fixed jammer Kharkiv"), (49.00, 38.00, "Fixed jammer East"),
                         (46.80, 34.50, "Fixed jammer South-East"), (46.00, 32.50, "Fixed jammer South"), (49.00, 31.50, "Fixed jammer Center"),
                         (50.80, 29.00, "Fixed jammer West-Kyiv"), (48.00, 36.50, "Fixed jammer Donbas")]:
        b.defense("EF", "ew-fixed", nm, lat, lon)
    # --- attack on the defense (SEAD): they arrive before the missiles, when the cheap drones are already over their targets ---
    radars = [d["id"] for d in b.defenses if d["id"].startswith("R")]
    sead_core = b.strike_targets(["R01", "R02", "R03", "R05", "R12", "PT01", "PT02", "AB01", "NS01"], prefix="X")
    sead_wide = b.strike_targets([r for r in radars if r not in ("R01", "R02", "R03", "R05", "R12")][:7] + ["PT03", "AB03", "AB04"], prefix="Y")
    b.wave("WX1", "geran-2-90kg", {"L04": 1, "L06": 1}, sead_core, 90, interval=3, spread=80, arrive_at=ARRIVE - 3000)
    b.wave("WX2", "iskander-m", {"L10": 1}, sead_core, 12, interval=15, spread=50, arrive_at=ARRIVE - 1200)
    b.wave("WX3", "kalibr-3m14", {"L08": 1}, {k: 1 for k in list(sead_wide)[4:]}, 14, interval=12, spread=50, arrive_at=ARRIVE - 1800)
    b.retarget("WS1", "WS2", "WS3", "WG2", "WK", "WH", "WA")
    b.estimate_ew()

    save(b, "ru-vs-ua.json",
         name="Russia → Ukraine: massive raid (~2000 drones + missiles) from multiple directions, targets in Kyiv and other cities",
         countries={"RU": "attacker", "BY": "attackerAlly", "UA": "defender", **nato_allies("europe", exclude=("RU", "BY", "UA"), extra=("MD",))},
         duration=28800)


# =====================================================================================================
# 2) Ukraine -> Russia (Saint Petersburg)
# =====================================================================================================
def ukraine_vs_russia():
    b = Builder(seed=2027)

    for sid, name, lat, lon in [
        ("L01", "Chernihiv", 51.50, 31.30), ("L02", "Sumy", 50.90, 34.80), ("L03", "Kharkiv (region)", 49.90, 36.20),
        ("L04", "Poltava (region)", 49.60, 34.60), ("L05", "Kyiv (north)", 51.00, 30.70), ("L06", "Zhytomyr (region)", 50.30, 28.70),
    ]:
        b.site(sid, name, lat, lon)

    targets = {
        "S01": ("Saint Petersburg - center", 59.9343, 30.3351, "city", 0), "S02": ("Saint Petersburg - Vasilyevsky", 59.9400, 30.2500, "city", 0),
        "S03": ("Kolpino", 59.7500, 30.6000, "city", 0), "S04": ("Saint Petersburg oil terminal", 59.8900, 30.2200, "military_base", 800),
        "S05": ("Kirishi refinery", 59.4500, 32.0200, "military_base", 1000), "S06": ("Primorsk port", 60.3600, 28.6200, "military_base", 1000),
        "S07": ("Ust-Luga port", 59.6800, 28.3000, "military_base", 1000), "S08": ("Air base Pulkovo", 59.8000, 30.2700, "military_base", 800),
        "S09": ("Airfield Pskov", 57.7800, 28.4000, "military_base", 800), "S10": ("Lakhta", 59.9900, 30.1700, "city", 0),
    }
    for tid, (n, la, lo, kind, rad) in targets.items():
        b.target(tid, n, la, lo, kind, rad)

    sites_all = {"L01": 3, "L02": 3, "L03": 2, "L04": 2, "L05": 2, "L06": 2}
    pool_energy = {"S05": 4, "S06": 4, "S07": 4, "S04": 3, "S08": 1}
    pool_city = {"S01": 2, "S02": 2, "S03": 1, "S10": 1}
    b.wave("WU1", "liutyi", sites_all, {**pool_energy, **pool_city}, 800, start=0, interval=3, spread=250)
    b.wave("WU2", "fp-1", sites_all, {**pool_energy, **pool_city}, 1000, start=600, interval=3, spread=300)
    b.wave("WU3", "liutyi", {"L01": 1, "L02": 1, "L05": 1}, {"S09": 2, "S07": 2, "S05": 1}, 200, start=1200, interval=4, spread=250)

    # --- Russian defense ---
    spb = (59.93, 30.32)
    sectors = {"kirishi": (59.45, 32.02), "primorsk": (60.36, 28.62), "ustluga": (59.68, 28.30), "pskov": (57.78, 28.40), "pulkovo": (59.80, 30.27)}

    for lat, lon, nm in [(59.90, 30.30, "Radar Saint Petersburg"), (57.80, 28.40, "Radar Pskov"), (58.52, 31.27, "Radar Novgorod"),
                         (58.73, 29.85, "Radar Luga"), (60.70, 28.75, "Radar Vyborg"), (59.45, 32.00, "Radar Kirishi"),
                         (54.80, 32.00, "Radar Smolensk"), (53.30, 34.40, "Radar Briansk"), (56.30, 30.60, "Radar Velikie Luki"),
                         (52.60, 36.00, "Radar Orel")]:
        b.defense("R", "radar-ew-long", nm, lat, lon)

    b.ring("PN", "pantsir-s1", "Pantsir Saint Petersburg", spb, 8, 6, 22)
    b.ring("PN", "pantsir-s1", "Pantsir Kirishi", sectors["kirishi"], 4, 2, 7)
    b.ring("PN", "pantsir-s1", "Pantsir Primorsk", sectors["primorsk"], 4, 2, 7)
    b.ring("PN", "pantsir-s1", "Pantsir Ust-Luga", sectors["ustluga"], 4, 2, 7)
    b.ring("PN", "pantsir-s1", "Pantsir Pskov", sectors["pskov"], 3, 2, 8)
    b.ring("PN", "pantsir-s1", "Pantsir Pulkovo", sectors["pulkovo"], 2, 2, 6)
    b.ring("PN", "pantsir-s1", "Pantsir border", (53.40, 34.30), 3, 5, 25)
    b.ring("TR", "tor-m2", "Tor Saint Petersburg", spb, 4, 4, 14)
    b.ring("TR", "tor-m2", "Tor Kirishi", sectors["kirishi"], 2, 2, 6)
    b.ring("TR", "tor-m2", "Tor Primorsk", sectors["primorsk"], 2, 2, 6)
    b.defense("BK", "buk-m3", "Buk Saint Petersburg South", 59.78, 30.40)
    b.defense("BK", "buk-m3", "Buk Saint Petersburg North", 60.05, 30.25)
    b.defense("BK", "buk-m3", "Buk Primorsk", 60.30, 28.70)
    b.defense("BK", "buk-m3", "Buk Ust-Luga", 59.62, 28.40)
    b.defense("S4", "s-400", "S-400 Saint Petersburg", 59.85, 30.60)
    b.defense("S4", "s-400", "S-400 Vyborg", 60.55, 29.00)
    b.ring("MG", "mobile-fire-group", "Mobile group Saint Petersburg", spb, 10, 3, 20)
    b.ring("MG", "mobile-fire-group", "Mobile group Kirishi", sectors["kirishi"], 3, 2, 6)
    b.ring("MG", "mobile-fire-group", "Mobile group Primorsk", sectors["primorsk"], 3, 2, 6)

    for lat, lon, nm in [(59.36, 30.07, "Base Siversky"), (57.78, 28.40, "Base Pskov-Kresty"), (60.05, 30.20, "Base Levashovo")]:
        b.defense("AB", "airbase", nm, lat, lon)
    for lat, lon, nm in [(59.70, 30.20, "Su-30SM South"), (60.20, 29.50, "Su-30SM North"), (58.50, 29.80, "Su-30SM West"), (58.20, 31.50, "Su-30SM East")]:
        b.defense("PA", "su-30sm-patrol", nm, lat, lon)

    # --- electronic warfare (jamming) ---
    b.ring("EF", "ew-fixed", "Fixed jammer Saint Petersburg", spb, 5, 35, 75)
    b.ring("EF", "ew-fixed", "Fixed jammer Kirishi", sectors["kirishi"], 2, 8, 20)
    b.ring("EF", "ew-fixed", "Fixed jammer Primorsk", sectors["primorsk"], 2, 8, 20)
    b.ring("EF", "ew-fixed", "Fixed jammer Ust-Luga", sectors["ustluga"], 2, 8, 20)
    b.ring("EF", "ew-fixed", "Fixed jammer border", (53.60, 34.00), 2, 10, 80)
    b.ring("EF", "ew-fixed", "Fixed jammer Pskov", sectors["pskov"], 2, 8, 25)
    b.ring("EM", "ew-mobile", "Mobile jammer Saint Petersburg", spb, 24, 4, 40)
    b.ring("EM", "ew-mobile", "Mobile jammer Kirishi", sectors["kirishi"], 3, 2, 8)
    b.ring("EM", "ew-mobile", "Mobile jammer Primorsk", sectors["primorsk"], 3, 2, 8)
    b.ring("EM", "ew-mobile", "Mobile jammer Ust-Luga", sectors["ustluga"], 3, 2, 8)
    b.retarget("WU1", "WU2", "WU3")
    b.estimate_ew()

    save(b, "ua-vs-ru.json",
         name="Ukraine → Russia: raid of ~2000 long-range drones toward the Saint Petersburg region (violating Belarusian airspace)",
         countries={"UA": "attacker", "MD": "attackerAlly", "RU": "defender", "BY": "defenderAlly"},
         duration=43200)


if __name__ == "__main__":
    russia_vs_ukraine()
    ukraine_vs_russia()

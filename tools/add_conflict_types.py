#!/usr/bin/env python3
"""
Adds to the catalog the types used by the Korea / Taiwan / Israel-Iran scenarios (idempotent).
The figures are rounded, ILLUSTRATIVE ESTIMATES from open sources, not verified data.

  python3 tools/add_conflict_types.py
"""
import json, pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
SRC = "illustrative estimate, unverified"


def off(id, name, kind, speed, rng, kg, cost, fail, ew, apogee=None, alt=None, notes=""):
    t = {"id": id, "name": name, "kind": kind, "speedMps": speed}
    if apogee: t["apogeeM"] = apogee
    if alt is not None: t["altitudeM"] = alt
    t.update({"rangeKm": rng, "warheadKg": kg, "costUsd": cost, "failureRate": fail, "ewResistance": ew,
              "source": SRC, "notes": (notes + " " if notes else "") + "All figures are rounded, illustrative estimates."})
    return t


OFFENSE = [
    off("kn-23", "KN-23 (Hwasong-11)", "ballisticMissile", 1500, 690, 500, 1500000, 0.10, 1.0, apogee=50000),
    off("kn-25", "KN-25 (600 mm launcher)", "ballisticMissile", 1100, 380, 400, 400000, 0.12, 1.0, apogee=50000),
    off("hwasal-2", "Hwasal-2 (cruise missile)", "cruiseMissile", 240, 1500, 300, 600000, 0.12, 0.6, alt=50),
    off("df-15b", "DF-15B", "ballisticMissile", 1800, 800, 500, 1000000, 0.04, 1.0, apogee=100000),
    off("df-16", "DF-16", "ballisticMissile", 2200, 1000, 500, 1200000, 0.04, 1.0, apogee=150000),
    off("cj-10", "CJ-10 (cruise missile)", "cruiseMissile", 250, 1500, 500, 1000000, 0.04, 0.8, alt=50),
    off("ghadr-110", "Ghadr-110", "ballisticMissile", 2500, 1950, 700, 1000000, 0.08, 1.0, apogee=200000),
    off("khorramshahr-4", "Khorramshahr-4", "ballisticMissile", 3000, 2000, 1500, 2000000, 0.08, 1.0, apogee=250000),
    off("paveh", "Paveh (cruise missile)", "cruiseMissile", 250, 1650, 450, 500000, 0.10, 0.7, alt=50),
    off("fateh-110", "Fateh-110", "ballisticMissile", 1100, 300, 450, 300000, 0.10, 1.0, apogee=40000),
    off("fajr-5", "Fajr-5", "ballisticMissile", 700, 75, 90, 15000, 0.08, 1.0, apogee=20000),
    off("rocket-122", "122 mm unguided rocket (Grad type)", "ballisticMissile", 400, 40, 18, 1000, 0.05, 1.0, apogee=9000),
    off("houthi-ballistic", "Houthi ballistic missile (Palestine-2 class)", "ballisticMissile", 2200, 2000, 500, 1000000, 0.12, 1.0, apogee=250000),
    off("samad-3", "Samad-3 drone", "drone", 50, 1800, 18, 20000, 0.10, 0.15, alt=300),
]


def sam(id, name, kind, rng, alt, mag, pk, reload, shot, cost, engages=None, pk_kind=None, track=None, **extra):
    t = {"id": id, "name": name, "kind": kind, "rangeKm": rng, "maxAltM": alt, "magazine": mag, "pk": pk, "reloadSeconds": reload}
    if engages: t["engages"] = engages
    if pk_kind: t["pkByKind"] = pk_kind
    t.update({"shotCostUsd": shot, "source": SRC, "notes": "All figures are rounded, illustrative estimates."})
    t.update(extra)
    if track: t["selfTrackKm"] = track
    t["costUsd"] = cost
    return t


ALL = ["drone", "cruiseMissile", "aircraft", "ballisticMissile"]
DEFENSE = [
    sam("cheongung-m-sam", "Cheongung II (KM-SAM)", "sam", 40, 20000, 8, 0.8, 3, 1500000, 200000000, ALL, {"ballisticMissile": 0.6}, 40,
        priority="threat", maxCostRatio=10, reserveRounds=2),
    sam("tien-kung-3", "Tien Kung III", "sam", 150, 45000, 16, 0.75, 3, 1000000, 300000000, ALL, {"ballisticMissile": 0.55}, 120,
        priority="threat", maxRoundsPerTarget=2, maxCostRatio=3),
    sam("iron-dome", "Iron Dome", "sam", 70, 10000, 20, 0.9, 1, 50000, 50000000,
        ["drone", "cruiseMissile", "ballisticMissile"], None, 70, priority="imminent"),
    sam("davids-sling", "David's Sling", "sam", 200, 40000, 12, 0.8, 3, 1000000, 300000000, ALL, {"ballisticMissile": 0.6}, 150,
        priority="threat", maxRoundsPerTarget=2, maxCostRatio=3),
    sam("arrow-2", "Arrow 2", "abm", 150, 50000, 24, 0.75, 4, 2000000, 400000000, None, None, 150, maxRoundsPerTarget=2),
    sam("arrow-3", "Arrow 3", "abm", 300, 200000, 24, 0.8, 5, 3000000, 800000000, None, None, 300, maxRoundsPerTarget=2),
]

# --- round 2: Israel -> Iran, South Korea -> North Korea, India <-> Pakistan ---
OFFENSE += [
    off("f-15i-jdam", "F-15I with guided bombs (JDAM)", "aircraft", 250, 1800, 430, 25000, 0.0, 1.0, alt=9000,
        notes="Incarcatura = explozibil estimat al unei bombe de ~900 kg; cost = bomba; avionul nu e consumat in simulare."),
    off("jericho-2", "Jericho-2", "ballisticMissile", 2500, 1800, 1000, 3000000, 0.08, 1.0, apogee=300000),
    off("harop", "IAI Harop (loitering munition)", "drone", 100, 1000, 23, 100000, 0.08, 0.2, alt=500),
    off("hyunmoo-2b", "Hyunmoo-2B", "ballisticMissile", 1500, 500, 500, 1500000, 0.04, 1.0, apogee=50000),
    off("hyunmoo-3", "Hyunmoo-3 (cruise missile)", "cruiseMissile", 250, 1000, 500, 1000000, 0.04, 0.8, alt=50),
    off("hyunmoo-4", "Hyunmoo-4", "ballisticMissile", 2000, 800, 2000, 3000000, 0.04, 1.0, apogee=100000),
    off("brahmos", "BrahMos", "cruiseMissile", 900, 450, 200, 2700000, 0.03, 0.9, alt=15),
    off("pralay", "Pralay", "ballisticMissile", 1500, 500, 500, 1000000, 0.05, 1.0, apogee=40000),
    off("scalp-eg", "SCALP-EG (air-launched)", "cruiseMissile", 270, 560, 450, 1000000, 0.04, 0.8, alt=50),
    off("fatah-2", "Fatah-II (guided rocket)", "ballisticMissile", 800, 400, 200, 300000, 0.08, 1.0, apogee=40000),
    off("babur", "Babur (cruise missile)", "cruiseMissile", 250, 700, 450, 500000, 0.08, 0.7, alt=50),
    off("shaheen-1a", "Shaheen-IA", "ballisticMissile", 2000, 900, 1000, 1000000, 0.08, 1.0, apogee=150000),
]
DEFENSE += [
    sam("s-300pmu2", "S-300PMU2", "sam", 150, 27000, 32, 0.7, 3, 1500000, 400000000, ALL, {"ballisticMissile": 0.35}, 120,
        priority="threat", maxRoundsPerTarget=2, maxCostRatio=1),
    sam("bavar-373", "Bavar-373", "sam", 150, 27000, 24, 0.65, 3, 1000000, 300000000, ALL, {"ballisticMissile": 0.3}, 120,
        priority="threat", maxRoundsPerTarget=2, maxCostRatio=1),
    sam("kn-06", "KN-06 (Pongae-5)", "sam", 100, 20000, 12, 0.5, 4, 500000, 60000000, ALL, {"ballisticMissile": 0.15}, 80,
        priority="threat", maxCostRatio=3),
    sam("hq-9", "HQ-9", "sam", 125, 27000, 32, 0.7, 3, 1000000, 300000000, ALL, {"ballisticMissile": 0.3}, 100,
        priority="threat", maxRoundsPerTarget=2, maxCostRatio=1),
    sam("hq-16", "HQ-16", "sam", 40, 15000, 8, 0.7, 3, 300000, 80000000, ["drone", "cruiseMissile", "aircraft"], None, 40,
        priority="threat", maxCostRatio=10, reserveRounds=2),
    sam("akash", "Akash", "sam", 30, 18000, 12, 0.7, 3, 150000, 60000000, ["drone", "cruiseMissile", "aircraft"], None, 30,
        priority="threat", maxCostRatio=10, reserveRounds=2),
    sam("mrsam", "MRSAM (Barak-8)", "sam", 70, 16000, 16, 0.8, 3, 1000000, 150000000, ALL, {"ballisticMissile": 0.4}, 70,
        priority="threat", maxCostRatio=10, reserveRounds=2),
]


def merge(fname, items):
    path = ROOT / "data" / "catalog" / fname
    data = json.loads(path.read_text(encoding="utf-8"))
    have = {t["id"] for t in data["types"]}
    added = [t["id"] for t in items if t["id"] not in have]
    data["types"] += [t for t in items if t["id"] not in have]
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(fname, "added:", added or "nothing (already there)")


merge("offense.json", OFFENSE)
merge("defense.json", DEFENSE)

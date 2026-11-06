#!/usr/bin/env python3
"""
Generates the scenarios: Korea <-> Korea, China -> Taiwan, Iran <-> Israel (without and with proxies), India <-> Pakistan, Russia -> NATO (Baltic).
Only types from the catalog (see tools/add_conflict_types.py); positions are approximate, force sizes are ILLUSTRATIVE, not real data.
Deterministic; checks the operational range of the waves before writing.

  python3 tools/add_conflict_types.py && python3 tools/make_conflict_scenarios.py
"""
import json, pathlib, sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from make_ukraine_scenarios import Builder, ROOT, OFFENSE, nato_allies  # noqa: E402


def save(b, filename, name, countries, duration):
    sc = b.scenario(name=name, countries=countries, duration=duration)
    sc["airspaceMap"] = "world"
    (ROOT / "scenarios" / filename).write_text(json.dumps(sc, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
    n = sum(w["count"] for w in sc["waves"])
    print(f"{filename}: {n} units launched, {len(sc['targets'])} targets, {len(sc['launchSites'])} launch sites, {len(sc['defenses'])} defense systems")


def rt(b, *wave_ids):
    """In-flight retargeting only for drones / cruise missiles: the engine rejects scenarios with retargetable ballistic missiles."""
    b.retarget(*[w["id"] for w in b.waves if w["id"] in wave_ids and OFFENSE[w["unit"]]["kind"] != "ballisticMissile"])


def add_sites(b, rows):
    for sid, name, lat, lon in rows:
        b.site(sid, name, lat, lon)


def add_targets(b, rows):
    for tid, name, lat, lon, kind in rows:
        b.target(tid, name, lat, lon, kind, radius=50 if kind == "military_base" else 0, destroy=10 if kind == "military_base" else None)


def units(sites, targets):
    return {k: 1 for k in sites}, {k: 1 for k in targets}


# =====================================================================================================
# 1) North Korea -> South Korea
# =====================================================================================================
def kp_vs_kr():
    b = Builder(seed=3001)
    add_sites(b, [("L01", "Kaesong", 37.97, 126.55), ("L02", "Sunan (Pyongyang)", 39.20, 125.67), ("L03", "Wonsan", 39.15, 127.45),
                  ("L04", "Hamhung", 39.90, 127.50), ("L05", "Sinpo", 40.03, 128.18), ("L06", "Nampo", 38.73, 125.40)])
    add_targets(b, [
        ("T01", "Seoul - center", 37.5665, 126.9780, "city"), ("T02", "Seoul - Gangnam", 37.5172, 127.0473, "city"),
        ("T03", "Incheon", 37.4563, 126.7052, "city"), ("T04", "Camp Humphreys (Pyeongtaek)", 36.96, 127.03, "military_base"),
        ("T05", "Air base Osan", 37.09, 127.03, "military_base"), ("T06", "Air base Seoul (Seongnam)", 37.44, 127.11, "military_base"),
        ("T07", "Air base Gunsan", 35.90, 126.62, "military_base"), ("T08", "Daegu", 35.87, 128.60, "city"),
        ("T09", "Busan", 35.18, 129.07, "city"), ("T10", "Naval base Jinhae", 35.14, 128.66, "military_base"),
        ("T11", "Camp Casey (Dongducheon)", 37.90, 127.06, "military_base"), ("T12", "Daejeon (command)", 36.35, 127.38, "military_base"),
    ])
    north = ["T01", "T02", "T03", "T04", "T05", "T06", "T11"]
    all_t = [f"T{i:02d}" for i in range(1, 13)]
    s, t = units(["L01", "L02", "L06"], north)
    b.wave("W1", "kn-25", s, t, 120, interval=3, spread=250, arrive_at=1800)
    s, t = units(["L02", "L03", "L04", "L05"], all_t)
    b.wave("W2", "kn-23", s, t, 60, interval=8, spread=300, arrive_at=1800)
    s, t = units(["L03", "L05", "L06"], ["T01", "T05", "T07", "T09", "T10", "T04"])
    b.wave("W3", "hwasal-2", s, t, 40, interval=12, spread=300, arrive_at=2400)
    rt(b, "W1", "W2", "W3")

    seoul = (37.55, 127.00)
    for lat, lon, nm in [(37.60, 126.90, "Radar Seoul North"), (37.30, 127.10, "Radar Seoul South"), (37.80, 127.60, "Radar Chuncheon"),
                         (36.40, 127.40, "Radar Daejeon"), (35.90, 128.50, "Radar Daegu"), (35.20, 129.00, "Radar Busan"), (35.90, 126.60, "Radar Gunsan")]:
        b.defense("R", "radar-ew-long", nm, lat, lon)
    b.ring("PT", "patriot-pac3-mse", "Patriot Seoul", seoul, 8, 8, 28)
    b.defense("PT", "patriot-pac3-mse", "Patriot Osan", 37.12, 127.00)
    b.defense("PT", "patriot-pac3-mse", "Patriot Camp Humphreys", 36.97, 127.05)
    b.defense("PT", "patriot-pac3-mse", "Patriot Gunsan", 35.93, 126.65)
    b.defense("PT", "patriot-pac3-mse", "Patriot Daegu", 35.88, 128.60)
    b.defense("PT", "patriot-pac3-mse", "Patriot Busan", 35.20, 129.05)
    b.ring("KM", "cheongung-m-sam", "Cheongung Seoul", seoul, 8, 5, 25)
    b.defense("KM", "cheongung-m-sam", "Cheongung Daejeon", 36.37, 127.35)
    b.defense("KM", "cheongung-m-sam", "Cheongung Busan", 35.15, 129.00)
    b.defense("KM", "cheongung-m-sam", "Cheongung Daegu", 35.85, 128.55)
    b.defense("TH", "thaad", "THAAD Seongju (USA)", 35.99, 128.45)
    b.ring("MG", "mobile-fire-group", "Mobile group Seoul", seoul, 8, 3, 20)
    for lat, lon, nm in [(37.09, 127.03, "Base Osan"), (35.90, 126.62, "Base Gunsan"), (35.90, 128.66, "Base Daegu"), (37.24, 127.00, "Base Suwon")]:
        b.defense("AB", "airbase", nm, lat, lon)
    for lat, lon, nm in [(37.20, 126.90, "KF-16 Osan"), (36.10, 126.70, "KF-16 Gunsan"), (35.95, 128.50, "KF-16 Daegu"), (37.55, 127.30, "KF-16 Seoul")]:
        b.defense("PA", "f-16-patrol", nm, lat, lon)

    save(b, "kp-vs-kr.json", "North Korea → South Korea: ballistic and cruise missile salvo toward the Seoul area",
         {"KP": "attacker", "KR": "defender", "US": "defenderAlly", "JP": "defenderAlly"}, 21600)


# =====================================================================================================
# 2) China -> Taiwan
# =====================================================================================================
def cn_vs_tw():
    b = Builder(seed=3002)
    add_sites(b, [("L01", "Fuding", 27.32, 120.20), ("L02", "Fuzhou", 26.07, 119.30), ("L03", "Zhangzhou", 24.50, 117.70),
                  ("L04", "Shantou", 23.40, 116.70), ("L05", "Leping (Jiangxi)", 28.97, 117.10), ("L06", "Ganzhou", 25.80, 114.90)])
    add_targets(b, [
        ("T01", "Taipei - center", 25.033, 121.565, "city"), ("T02", "Taoyuan", 25.00, 121.20, "city"),
        ("T03", "Hsinchu (science park)", 24.78, 121.00, "city"), ("T04", "Taichung", 24.147, 120.67, "city"),
        ("T05", "Tainan", 22.99, 120.20, "city"), ("T06", "Kaohsiung", 22.627, 120.30, "city"),
        ("T07", "Air base Songshan", 25.07, 121.55, "military_base"), ("T08", "Air base Taoyuan", 25.07, 121.23, "military_base"),
        ("T09", "Air base Ching Chuan Kang", 24.26, 120.62, "military_base"), ("T10", "Air base Chiayi", 23.46, 120.39, "military_base"),
        ("T11", "Air base Tainan", 22.95, 120.21, "military_base"), ("T12", "Air base Hualien", 24.02, 121.62, "military_base"),
        ("T13", "Air base Taitung", 22.75, 121.10, "military_base"), ("T14", "Naval base Zuoying", 22.68, 120.27, "military_base"),
        ("T15", "Naval base Suao", 24.60, 121.86, "military_base"),
    ])
    for lat, lon, nm in [(24.70, 121.10, "Radar Leshan"), (25.20, 121.60, "Radar Taipei North"), (24.20, 120.80, "Radar Taichung"),
                         (22.90, 120.40, "Radar Kaohsiung"), (23.95, 121.50, "Radar Hualien"), (22.75, 121.10, "Radar Taitung")]:
        b.defense("R", "radar-ew-long", nm, lat, lon)
    taipei = (25.03, 121.55)
    b.ring("PT", "patriot-pac3-mse", "Patriot Taipei", taipei, 6, 6, 22)
    b.defense("PT", "patriot-pac3-mse", "Patriot Hsinchu", 24.80, 121.00)
    b.defense("PT", "patriot-pac3-mse", "Patriot Taichung", 24.15, 120.68)
    b.defense("PT", "patriot-pac3-mse", "Patriot Kaohsiung", 22.65, 120.32)
    b.defense("PT", "patriot-pac3-mse", "Patriot Tainan", 22.98, 120.22)
    b.ring("TK", "tien-kung-3", "Tien Kung Taipei", taipei, 6, 8, 30)
    b.defense("TK", "tien-kung-3", "Tien Kung Hsinchu", 24.76, 121.05)
    b.defense("TK", "tien-kung-3", "Tien Kung Taichung", 24.12, 120.70)
    b.defense("TK", "tien-kung-3", "Tien Kung Kaohsiung", 22.60, 120.33)
    b.defense("TK", "tien-kung-3", "Tien Kung Hualien", 24.00, 121.60)
    b.defense("TK", "tien-kung-3", "Tien Kung Taitung", 22.77, 121.12)
    b.ring("NS", "nasams", "NASAMS Taiwan", (23.8, 120.9), 4, 40, 110)
    b.ring("MG", "mobile-fire-group", "Mobile group Taipei", taipei, 6, 3, 18)
    for lat, lon, nm in [(23.46, 120.39, "Base Chiayi"), (24.02, 121.62, "Base Hualien"), (22.75, 121.10, "Base Taitung")]:
        b.defense("AB", "airbase", nm, lat, lon)
    for lat, lon, nm in [(23.60, 120.60, "F-16V Chiayi"), (24.20, 121.40, "F-16V Hualien"), (22.90, 121.00, "F-16V Taitung")]:
        b.defense("PA", "f-16-patrol", nm, lat, lon)

    # first strike: the radars and batteries (the attacker's counterplay)
    strike = b.strike_targets(["R01", "R02", "R03", "R04", "R05", "R06", "PT01", "PT02", "TK01", "TK02"], prefix="X")
    s_a = {"L01": 1, "L02": 2, "L03": 2, "L04": 1}
    s_b = {"L02": 1, "L03": 1, "L04": 1, "L05": 1, "L06": 1}
    b.wave("W1", "df-15b", s_a, strike, 60, interval=3, spread=150, arrive_at=1200)
    cities = {f"T{i:02d}": 1 for i in (1, 2, 3, 4, 5, 6)}
    bases = {f"T{i:02d}": 1 for i in range(7, 16)}
    b.wave("W2", "df-15b", s_a, {**cities, **bases}, 120, interval=3, spread=250, arrive_at=1800)
    b.wave("W3", "df-16", s_b, {**cities, **bases}, 80, interval=4, spread=250, arrive_at=2100)
    b.wave("W4", "cj-10", s_b, {**bases, "T01": 1, "T06": 1, "T03": 1}, 60, interval=8, spread=300, arrive_at=2400)
    rt(b, "W1", "W2", "W3", "W4")

    save(b, "cn-vs-tw.json", "China → Taiwan: ballistic and cruise missile salvo, with an opening strike on radars and batteries",
         {"CN": "attacker", "TW": "defender", "US": "defenderAlly", "JP": "defenderAlly", "PH": "defenderAlly"}, 21600)


# =====================================================================================================
# 3) / 4) Iran -> Israel (without and with proxies)
# =====================================================================================================
IL_TARGETS = [
    ("T01", "Tel Aviv", 32.0853, 34.7818, "city"), ("T02", "Jerusalem", 31.7683, 35.2137, "city"), ("T03", "Haifa", 32.794, 34.990, "city"),
    ("T04", "Beersheba", 31.253, 34.791, "city"), ("T05", "Eilat", 29.558, 34.952, "city"),
    ("T06", "Air base Nevatim", 31.208, 35.012, "military_base"), ("T07", "Air base Ramon", 30.776, 34.667, "military_base"),
    ("T08", "Air base Tel Nof", 31.839, 34.820, "military_base"), ("T09", "Air base Hatzerim", 31.234, 34.660, "military_base"),
    ("T10", "Air base Ramat David", 32.665, 35.180, "military_base"), ("T11", "Dimona (nuclear center)", 31.000, 35.150, "military_base"),
    ("T12", "Kirya (Tel Aviv, command)", 32.070, 34.790, "military_base"), ("T13", "Haifa refinery", 32.800, 35.050, "field"),
]
IL_PROXY_TARGETS = [
    ("T14", "Kiryat Shmona", 33.207, 35.571, "city"), ("T15", "Nahariya", 33.010, 35.095, "city"), ("T16", "Safed", 32.960, 35.500, "city"),
    ("T17", "Sderot", 31.525, 34.596, "city"), ("T18", "Ashkelon", 31.669, 34.574, "city"), ("T19", "Ashdod", 31.804, 34.655, "city"),
]


def israel_defense(b, proxies):
    for lat, lon, nm in [(32.00, 34.85, "Radar Tel Aviv"), (33.00, 35.40, "Radar Mount Meron"), (30.60, 34.80, "Radar Mitzpe Ramon"),
                         (29.60, 34.95, "Radar Eilat"), (31.30, 34.80, "Radar Negev"), (32.70, 35.00, "Radar Haifa")]:
        b.defense("R", "radar-ew-long", nm, lat, lon)
    for lat, lon, nm in [(32.05, 34.80, "Iron Dome Tel Aviv"), (32.78, 34.99, "Iron Dome Haifa"), (31.25, 34.79, "Iron Dome Beersheba"),
                         (29.56, 34.95, "Iron Dome Eilat"), (31.77, 35.20, "Iron Dome Jerusalem"), (31.80, 34.66, "Iron Dome Ashdod"),
                         (31.21, 35.00, "Iron Dome Nevatim"), (30.78, 34.67, "Iron Dome Ramon")]:
        b.defense("ID", "iron-dome", nm, lat, lon)
    for lat, lon, nm in [(32.00, 34.90, "David's Sling center"), (32.75, 35.00, "David's Sling north"), (31.30, 34.85, "David's Sling south")]:
        b.defense("DS", "davids-sling", nm, lat, lon)
    for lat, lon, nm in [(31.90, 34.85, "Arrow 3 center"), (32.70, 35.10, "Arrow 3 north"), (31.10, 34.80, "Arrow 3 Negev")]:
        b.defense("A3", "arrow-3", nm, lat, lon)
    for lat, lon, nm in [(32.10, 34.90, "Arrow 2 center"), (31.60, 35.00, "Arrow 2 Jerusalem"), (32.60, 35.20, "Arrow 2 north")]:
        b.defense("A2", "arrow-2", nm, lat, lon)
    b.defense("PT", "patriot-pac3-mse", "Patriot Tel Aviv", 32.10, 34.75)
    b.defense("PT", "patriot-pac3-mse", "Patriot Haifa", 32.80, 35.00)
    b.defense("PT", "patriot-pac3-mse", "Patriot Negev", 31.30, 34.90)
    b.defense("TH", "thaad", "THAAD (USA, Negev)", 31.05, 34.85)
    for lat, lon, nm in [(31.839, 34.820, "Base Tel Nof"), (31.208, 35.012, "Base Nevatim"), (30.776, 34.667, "Base Ramon"), (32.665, 35.180, "Base Ramat David")]:
        b.defense("AB", "airbase", nm, lat, lon)
    for lat, lon, nm in [(31.80, 34.90, "Patrol F-16/F-15 center"), (31.20, 35.10, "Patrol F-35I Negev"), (32.50, 35.20, "Patrol F-16 north"), (30.60, 34.80, "Patrol F-35I south")]:
        b.defense("PA", "f-16-patrol", nm, lat, lon)
    if proxies:
        for lat, lon, nm in [(33.20, 35.57, "Iron Dome Kiryat Shmona"), (33.00, 35.10, "Iron Dome Nahariya"), (32.96, 35.50, "Iron Dome Safed"),
                             (32.90, 35.30, "Iron Dome Karmiel"), (31.52, 34.60, "Iron Dome Sderot"), (31.67, 34.57, "Iron Dome Ashkelon"),
                             (32.70, 35.00, "Iron Dome Haifa South"), (31.95, 34.80, "Iron Dome Tel Aviv South")]:
            b.defense("ID", "iron-dome", nm, lat, lon)


def iran_waves(b, targets_iran):
    """Salva iraniana comuna (coordonata: ajunge in jur de ora 10)."""
    s = {"I1": 2, "I2": 2, "I3": 2, "I4": 2, "I5": 1}
    t_all = {t[0]: 1 for t in targets_iran}
    t_ballistic = {f"T{i:02d}": 1 for i in (1, 2, 3, 4, 6, 7, 8, 9, 10, 11, 12)}
    b.wave("WI1", "shahed-136", s, t_all, 150, interval=10, spread=400, arrive_at=36000)
    b.wave("WI2", "paveh", s, {k: 1 for k in ("T01", "T03", "T06", "T08", "T10", "T12")}, 30, interval=30, spread=300, arrive_at=36000)
    b.wave("WI3", "ghadr-110", s, t_ballistic, 60, interval=15, spread=300, arrive_at=36000)
    b.wave("WI4", "khorramshahr-4", s, {k: 1 for k in ("T01", "T03", "T06", "T07", "T11", "T12")}, 30, interval=20, spread=300, arrive_at=36100)
    rt(b, "WI1", "WI2", "WI3", "WI4")


IRAN_SITES = [("I1", "Kermanshah", 34.31, 47.07), ("I2", "Tabriz", 38.08, 46.29), ("I3", "Ilam", 33.63, 46.42),
              ("I4", "Khorramabad", 33.49, 48.35), ("I5", "Isfahan", 32.65, 51.67)]


def ir_vs_il():
    b = Builder(seed=3003)
    add_sites(b, IRAN_SITES)
    add_targets(b, IL_TARGETS)
    israel_defense(b, proxies=False)
    iran_waves(b, IL_TARGETS)
    save(b, "ir-vs-il.json", "Iran → Israel (without proxies): coordinated salvo of drones, cruise and ballistic missiles",
         {"IR": "attacker", "IL": "defender", "US": "defenderAlly", "GB": "defenderAlly", "FR": "defenderAlly", "JO": "neutral", "IQ": "neutral", "SY": "neutral"}, 43200)


def ir_vs_il_proxies():
    b = Builder(seed=3004)
    add_sites(b, IRAN_SITES + [
        ("P1", "Southern Lebanon", 33.15, 35.35), ("P2", "Bekaa Valley", 33.85, 36.00), ("P3", "Gaza", 31.50, 34.45),
        ("P4", "Saada (Yemen)", 16.94, 43.76), ("P5", "Hodeidah (Yemen)", 14.80, 42.95), ("P6", "Sanaa (Yemen)", 15.35, 44.20),
        ("P7", "Western Iraq", 33.00, 41.50), ("P8", "Southern Iraq", 31.30, 46.30)])
    add_targets(b, IL_TARGETS + IL_PROXY_TARGETS)
    israel_defense(b, proxies=True)
    iran_waves(b, IL_TARGETS)
    north = {f"T{i}": 1 for i in (14, 15, 16)}
    haifa = {"T03": 2, "T10": 1, **north}
    b.wave("WP1", "rocket-122", {"P1": 1}, north, 200, interval=4, spread=500, arrive_at=35800)
    b.wave("WP2", "fajr-5", {"P1": 1}, {**haifa, "T13": 1}, 80, interval=10, spread=400, arrive_at=35800)
    b.wave("WP3", "fateh-110", {"P1": 1, "P2": 1}, {"T01": 1, "T03": 1, "T10": 1, "T12": 1}, 30, interval=20, spread=300, arrive_at=35900)
    b.wave("WP4", "rocket-122", {"P3": 1}, {k: 1 for k in ("T17", "T18", "T19")}, 150, interval=5, spread=500, arrive_at=35800)
    b.wave("WP5", "fajr-5", {"P3": 1}, {"T01": 1, "T19": 1, "T08": 1}, 40, interval=12, spread=400, arrive_at=35900)
    # Houthis: only Eilat and Ramon are in range (1800 km drone: only from Saada toward Eilat)
    b.wave("WP6", "samad-3", {"P4": 1}, {"T05": 1}, 40, interval=20, spread=400, arrive_at=36000)
    b.wave("WP7", "houthi-ballistic", {"P4": 1, "P5": 1}, {"T05": 2, "T07": 1}, 20, interval=40, spread=300, arrive_at=36000)
    b.wave("WP8", "shahed-136", {"P7": 1, "P8": 1}, {"T03": 1, "T10": 1, "T13": 1, "T14": 1}, 60, interval=15, spread=400, arrive_at=36000)
    rt(b, "WP1", "WP2", "WP3", "WP4", "WP5", "WP6", "WP7", "WP8")
    save(b, "ir-vs-il-proxies.json",
         "Iran → Israel (with proxies): salvo coordinated with Hezbollah (Lebanon), Gaza, the Houthis (Yemen) and militias in Iraq",
         {"IR": "attacker", "LB": "attackerAlly", "PS": "attackerAlly", "YE": "attackerAlly", "IQ": "attackerAlly",
          "IL": "defender", "US": "defenderAlly", "GB": "defenderAlly", "FR": "defenderAlly", "JO": "neutral", "SY": "neutral"}, 43200)


# =====================================================================================================
# 5) Israel -> Iran
# =====================================================================================================
def il_vs_ir():
    b = Builder(seed=3005)
    add_sites(b, [("L01", "Base Tel Nof", 31.839, 34.820), ("L02", "Base Nevatim", 31.208, 35.012), ("L03", "Base Ramon", 30.776, 34.667),
                  ("L04", "Base Ramat David", 32.665, 35.180), ("L05", "Negev (Jericho)", 31.40, 35.00),
                  ("D1", "Launch from inside Iran (Zagros)", 34.30, 47.50), ("D2", "Launch from inside Iran (center)", 33.90, 50.40),
                  ("D3", "Launch from inside Iran (north-west)", 37.40, 46.60)])
    add_targets(b, [
        ("T01", "Tehran", 35.690, 51.390, "city"), ("T02", "Natanz (nuclear center)", 33.720, 51.730, "military_base"),
        ("T03", "Fordow (nuclear center)", 34.880, 50.990, "military_base"), ("T04", "Isfahan (nuclear center)", 32.620, 51.730, "military_base"),
        ("T05", "Arak (heavy water)", 34.370, 49.240, "military_base"), ("T06", "Parchin", 35.520, 51.770, "military_base"),
        ("T07", "Missile base Kermanshah", 34.350, 47.100, "military_base"), ("T08", "Missile base Tabriz", 38.080, 46.290, "military_base"),
        ("T09", "Bushehr (nuclear plant)", 28.830, 50.890, "military_base"), ("T10", "Air base Hamadan", 35.210, 48.650, "military_base"),
        ("T11", "Air base Isfahan", 32.750, 51.860, "military_base"), ("T12", "Air base Mehrabad", 35.690, 51.310, "military_base"),
    ])
    for lat, lon, nm in [(35.75, 51.20, "Radar Tehran"), (33.60, 51.60, "Radar Natanz"), (32.70, 51.70, "Radar Isfahan"),
                         (34.30, 47.10, "Radar Kermanshah"), (38.00, 46.40, "Radar Tabriz"), (35.20, 48.60, "Radar Hamadan"),
                         (29.50, 52.50, "Radar Shiraz"), (28.90, 50.90, "Radar Bushehr")]:
        b.defense("R", "radar-ew-long", nm, lat, lon)
    b.defense("S3", "s-300pmu2", "S-300 Tehran North", 35.80, 51.40)
    b.defense("S3", "s-300pmu2", "S-300 Tehran South", 35.55, 51.35)
    b.defense("S3", "s-300pmu2", "S-300 Natanz", 33.70, 51.75)
    b.defense("S3", "s-300pmu2", "S-300 Fordow", 34.86, 51.02)
    b.defense("S3", "s-300pmu2", "S-300 Isfahan", 32.60, 51.70)
    b.defense("S3", "s-300pmu2", "S-300 Bushehr", 28.85, 50.85)
    b.defense("BV", "bavar-373", "Bavar-373 Tehran", 35.65, 51.50)
    b.defense("BV", "bavar-373", "Bavar-373 Isfahan", 32.65, 51.60)
    b.defense("BV", "bavar-373", "Bavar-373 Tabriz", 38.05, 46.35)
    b.ring("TR", "tor-m2", "Tor Tehran", (35.69, 51.39), 4, 5, 20)
    b.ring("TR", "tor-m2", "Tor Natanz", (33.72, 51.73), 2, 2, 8)
    b.ring("MG", "mobile-fire-group", "Mobile group Tehran", (35.69, 51.39), 8, 3, 20)
    b.ring("MG", "mobile-fire-group", "Mobile group Isfahan", (32.65, 51.70), 4, 2, 10)
    for lat, lon, nm in [(35.21, 48.65, "Base Hamadan"), (32.75, 51.86, "Base Isfahan"), (35.69, 51.31, "Base Mehrabad"), (29.54, 52.59, "Base Shiraz")]:
        b.defense("AB", "airbase", nm, lat, lon)
    for lat, lon, nm in [(35.40, 50.00, "MiG-29 / F-14 Tehran"), (34.50, 48.00, "MiG-29 / F-14 Hamadan"), (32.90, 52.00, "MiG-29 / F-14 Isfahan")]:
        b.defense("PA", "mig-29-patrol", nm, lat, lon)

    # first phase: drones loitering over the radars and SAMs (SEAD)
    sead = b.strike_targets(["R01", "R02", "R03", "R04", "R05", "R06", "S301", "S302", "S303", "S304"], prefix="X")
    b.wave("WS", "harop", {"D1": 1, "D2": 1, "D3": 1}, sead, 40, interval=4, spread=120, arrive_at=1800)
    s = {"L01": 1, "L02": 1, "L03": 1, "L04": 1}
    t = {f"T{i:02d}": 1 for i in (2, 3, 4, 5, 6, 7, 8, 10, 11, 12)}
    b.wave("WA", "f-15i-jdam", s, t, 80, start=3000, interval=10, spread=100)
    b.wave("WJ", "jericho-2", {"L05": 1}, {k: 1 for k in ("T01", "T02", "T03", "T04", "T06", "T07")}, 20, interval=30, spread=200, arrive_at=4000)
    rt(b, "WS", "WA")
    save(b, "il-vs-ir.json", "Israel → Iran: strike on air defenses and on nuclear and missile objectives",
         {"IL": "attacker", "IR": "defender", "JO": "neutral", "IQ": "neutral", "SY": "neutral", "SA": "neutral"}, 14400)


# =====================================================================================================
# 6) South Korea -> North Korea
# =====================================================================================================
def kr_vs_kp():
    b = Builder(seed=3006)
    add_sites(b, [("L01", "Gangwon (Hyunmoo)", 37.75, 128.00), ("L02", "Gyeonggi North", 37.85, 127.15), ("L03", "Paju", 37.80, 126.80),
                  ("L04", "Chungju", 36.95, 127.90), ("L05", "Air base Seosan", 36.70, 126.50)])
    add_targets(b, [
        ("T01", "Pyongyang", 39.039, 125.762, "city"), ("T02", "Yongbyon (nuclear center)", 39.800, 125.750, "military_base"),
        ("T03", "Air base Sunan", 39.200, 125.670, "military_base"), ("T04", "Wonsan (base)", 39.150, 127.450, "military_base"),
        ("T05", "Punggye-ri", 41.280, 129.080, "military_base"), ("T06", "Sohae (launch)", 39.660, 124.700, "military_base"),
        ("T07", "Kaesong artillery", 37.950, 126.650, "military_base"), ("T08", "Submarine base Sinpo", 40.030, 128.180, "military_base"),
        ("T09", "Hamhung", 39.920, 127.540, "city"), ("T10", "Nampo", 38.730, 125.400, "military_base"),
    ])
    for lat, lon, nm in [(39.00, 125.80, "Radar Pyongyang"), (38.40, 126.50, "Radar Sariwon"), (39.15, 127.45, "Radar Wonsan"), (40.00, 128.20, "Radar Sinpo")]:
        b.defense("R", "radar-ew-long", nm, lat, lon)
    b.ring("KN", "kn-06", "KN-06 Pyongyang", (39.04, 125.76), 6, 5, 25)
    b.defense("KN", "kn-06", "KN-06 Yongbyon", 39.80, 125.72)
    b.defense("KN", "kn-06", "KN-06 Wonsan", 39.15, 127.40)
    b.defense("KN", "kn-06", "KN-06 Sinpo", 40.00, 128.15)
    b.ring("MG", "mobile-fire-group", "Mobile group Pyongyang", (39.04, 125.76), 10, 3, 20)
    b.ring("MG", "mobile-fire-group", "Mobile group border", (38.30, 126.60), 6, 5, 30)
    for lat, lon, nm in [(39.20, 125.67, "Base Sunan"), (39.17, 127.48, "Base Kalma"), (38.40, 125.90, "Base Sondok")]:
        b.defense("AB", "airbase", nm, lat, lon)
    for lat, lon, nm in [(39.40, 125.80, "MiG-29 Pyongyang"), (39.40, 127.40, "MiG-29 Wonsan")]:
        b.defense("PA", "mig-29-patrol", nm, lat, lon)

    near = {f"T{i:02d}": 1 for i in (1, 2, 3, 4, 7, 10)}
    far = {f"T{i:02d}": 1 for i in (1, 2, 3, 4, 5, 6, 8, 9, 10, 7)}
    s = {"L01": 1, "L02": 1, "L03": 1, "L04": 1}
    b.wave("W1", "hyunmoo-2b", s, near, 80, interval=4, spread=150, arrive_at=1500)
    b.wave("W2", "hyunmoo-4", {"L01": 2, "L04": 1}, {k: 1 for k in ("T02", "T05", "T08", "T06")}, 30, interval=10, spread=150, arrive_at=1500)
    b.wave("W3", "hyunmoo-3", {"L01": 1, "L02": 1, "L05": 1}, far, 40, interval=8, spread=200, arrive_at=1800)
    rt(b, "W1", "W2", "W3")
    save(b, "kr-vs-kp.json", "South Korea → North Korea: retaliatory strike on Pyongyang and strategic objectives",
         {"KR": "attacker", "US": "attackerAlly", "KP": "defender", "CN": "defenderAlly", "RU": "defenderAlly"}, 14400)


# =====================================================================================================
# 7) / 8) India <-> Pakistan
# =====================================================================================================
def in_vs_pk():
    b = Builder(seed=3007)
    add_sites(b, [("L01", "Pathankot", 32.23, 75.63), ("L02", "Ambala", 30.37, 76.78), ("L03", "Jaisalmer", 26.90, 70.90),
                  ("L04", "Bhuj", 23.25, 69.67), ("L05", "Jodhpur", 26.25, 73.05), ("L06", "Srinagar", 34.08, 74.80), ("L07", "Sirsa", 29.55, 75.00)])
    add_targets(b, [
        ("T01", "Islamabad", 33.69, 73.06, "city"), ("T02", "Lahore", 31.55, 74.34, "city"), ("T03", "Karachi", 24.86, 67.00, "city"),
        ("T04", "Rawalpindi (command)", 33.60, 73.05, "military_base"), ("T05", "Air base Nur Khan", 33.62, 73.10, "military_base"),
        ("T06", "Air base Sargodha (Mushaf)", 32.05, 72.67, "military_base"), ("T07", "Air base Rafiqui", 30.76, 72.28, "military_base"),
        ("T08", "Air base Bholari", 25.00, 68.40, "military_base"), ("T09", "Air base Jacobabad", 28.28, 68.45, "military_base"),
        ("T11", "Kahuta", 33.59, 73.39, "military_base"),
    ])
    pk_defense(b)
    near = {f"T{i:02d}": 1 for i in (1, 2, 4, 5, 6, 7, 11)}
    b.wave("W1", "brahmos", {"L01": 2, "L06": 1}, near, 60, interval=6, spread=100, arrive_at=1800)
    b.wave("W2", "pralay", {"L01": 1, "L07": 1}, {k: 1 for k in ("T02", "T04", "T05", "T06", "T07", "T11")}, 24, interval=15, spread=150, arrive_at=1800)
    b.wave("W2S", "pralay", {"L03": 1}, {"T07": 1, "T08": 1, "T09": 1}, 8, interval=20, spread=150, arrive_at=1800)
    b.wave("W3", "scalp-eg", {"L01": 1, "L02": 1, "L07": 1}, {k: 1 for k in ("T04", "T05", "T06", "T07")}, 30, interval=10, spread=150, arrive_at=2100)
    b.wave("W3S", "scalp-eg", {"L03": 1, "L05": 1}, {"T07": 1, "T08": 1, "T09": 1}, 12, interval=15, spread=150, arrive_at=2100)
    sead = b.strike_targets(["R01", "R02", "R04", "HQ01", "HQ02", "HQ04"], prefix="X")
    b.wave("WS", "harop", {"L01": 1, "L02": 1, "L07": 1}, sead, 40, interval=5, spread=100, arrive_at=1500)
    rt(b, "W1", "W2", "W2S", "W3", "W3S", "WS")
    save(b, "in-vs-pk.json", "India → Pakistan: strike on air bases and air defenses",
         {"IN": "attacker", "PK": "defender", "CN": "defenderAlly", "AF": "neutral", "IR": "neutral"}, 14400)


def pk_defense(b):
    for lat, lon, nm in [(33.70, 73.00, "Radar Islamabad"), (31.55, 74.30, "Radar Lahore"), (24.90, 67.10, "Radar Karachi"),
                         (32.05, 72.70, "Radar Sargodha"), (30.20, 71.50, "Radar Multan"), (28.30, 68.45, "Radar Jacobabad")]:
        b.defense("R", "radar-ew-long", nm, lat, lon)
    b.defense("HQ", "hq-9", "HQ-9 Islamabad", 33.70, 73.10)
    b.defense("HQ", "hq-9", "HQ-9 Lahore", 31.50, 74.40)
    b.defense("HQ", "hq-9", "HQ-9 Karachi", 24.90, 67.10)
    b.defense("HQ", "hq-9", "HQ-9 Sargodha", 32.10, 72.70)
    b.ring("H6", "hq-16", "HQ-16 Islamabad", (33.69, 73.06), 4, 4, 18)
    b.ring("H6", "hq-16", "HQ-16 Lahore", (31.55, 74.34), 4, 4, 18)
    b.ring("H6", "hq-16", "HQ-16 Karachi", (24.86, 67.00), 3, 4, 18)
    b.ring("MG", "mobile-fire-group", "Mobile group Pakistan", (32.0, 72.5), 10, 20, 280)
    for lat, lon, nm in [(33.62, 73.10, "Base Nur Khan"), (32.05, 72.67, "Base Sargodha"), (30.76, 72.28, "Base Rafiqui"), (25.00, 68.40, "Base Bholari")]:
        b.defense("AB", "airbase", nm, lat, lon)
    for lat, lon, nm in [(33.30, 72.80, "JF-17 / F-16 Islamabad"), (31.70, 73.70, "JF-17 / F-16 Lahore"), (32.30, 72.20, "JF-17 / F-16 Sargodha"), (25.20, 68.00, "JF-17 / F-16 South")]:
        b.defense("PA", "f-16-patrol", nm, lat, lon)


def pk_vs_in():
    b = Builder(seed=3008)
    add_sites(b, [("L01", "Sialkot", 32.50, 74.50), ("L02", "Multan", 30.20, 71.50), ("L03", "Sargodha", 32.00, 72.70),
                  ("L04", "Karachi", 24.90, 67.00), ("L05", "Rahim Yar Khan", 28.40, 70.30), ("L06", "Peshawar", 34.00, 71.50)])
    add_targets(b, [
        ("T01", "Delhi", 28.61, 77.21, "city"), ("T02", "Amritsar", 31.63, 74.87, "city"), ("T03", "Jammu", 32.73, 74.86, "city"),
        ("T04", "Air base Pathankot", 32.23, 75.63, "military_base"), ("T05", "Air base Adampur", 31.43, 75.76, "military_base"),
        ("T06", "Air base Ambala", 30.37, 76.82, "military_base"), ("T07", "Air base Bhuj", 23.29, 69.67, "military_base"),
        ("T08", "Air base Jaisalmer", 26.89, 70.87, "military_base"), ("T09", "Air base Sirsa", 29.55, 75.00, "military_base"),
        ("T10", "Mumbai", 19.08, 72.88, "city"), ("T11", "Jamnagar refinery", 22.47, 70.07, "field"), ("T12", "Chandigarh", 30.73, 76.78, "city"),
    ])
    for lat, lon, nm in [(28.60, 77.20, "Radar Delhi"), (31.60, 74.90, "Radar Amritsar"), (30.40, 76.80, "Radar Ambala"),
                         (26.90, 70.90, "Radar Jaisalmer"), (23.30, 69.70, "Radar Bhuj"), (19.10, 72.90, "Radar Mumbai")]:
        b.defense("R", "radar-ew-long", nm, lat, lon)
    b.defense("S4", "s-400", "S-400 Delhi", 28.70, 77.10)
    b.defense("S4", "s-400", "S-400 Ambala", 30.40, 76.70)
    b.defense("S4", "s-400", "S-400 Pathankot", 32.25, 75.55)
    b.defense("S4", "s-400", "S-400 Jodhpur", 26.30, 73.00)
    b.ring("AK", "akash", "Akash Delhi", (28.61, 77.21), 4, 5, 25)
    b.ring("AK", "akash", "Akash Amritsar", (31.63, 74.87), 3, 3, 15)
    b.ring("AK", "akash", "Akash Jaisalmer", (26.89, 70.87), 2, 3, 12)
    b.ring("MR", "mrsam", "MRSAM Delhi", (28.61, 77.21), 3, 8, 25)
    b.ring("MR", "mrsam", "MRSAM Mumbai", (19.08, 72.88), 2, 5, 20)
    b.ring("MR", "mrsam", "MRSAM Pathankot", (32.23, 75.63), 2, 4, 12)
    b.ring("MG", "mobile-fire-group", "Mobile group India", (30.5, 75.5), 10, 15, 250)
    for lat, lon, nm in [(32.23, 75.63, "Base Pathankot"), (30.37, 76.82, "Base Ambala"), (26.89, 70.87, "Base Jaisalmer"), (23.29, 69.67, "Base Bhuj")]:
        b.defense("AB", "airbase", nm, lat, lon)
    for lat, lon, nm in [(32.00, 75.60, "Su-30MKI / Rafale North"), (30.30, 76.50, "Su-30MKI / Rafale Ambala"), (26.80, 71.50, "Su-30MKI West"), (23.50, 70.00, "Su-30MKI South")]:
        b.defense("PA", "su-30sm-patrol", nm, lat, lon)
    b.wave("W1", "fatah-2", {"L01": 2, "L03": 1}, {k: 1 for k in ("T02", "T03", "T04", "T05", "T09")}, 60, interval=6, spread=200, arrive_at=1800)
    b.wave("W2", "babur", {"L01": 1, "L02": 1, "L03": 1, "L05": 1}, {k: 1 for k in ("T01", "T02", "T04", "T06", "T09")}, 50, interval=12, spread=250, arrive_at=2400)
    b.wave("W3", "shaheen-1a", {"L02": 1, "L05": 1}, {k: 1 for k in ("T01", "T07", "T08", "T09", "T11")}, 14, interval=25, spread=250, arrive_at=2100)
    b.wave("W3S", "shaheen-1a", {"L04": 1}, {k: 1 for k in ("T07", "T08", "T11")}, 8, interval=25, spread=250, arrive_at=2100)
    rt(b, "W1", "W2", "W3", "W3S")
    save(b, "pk-vs-in.json", "Pakistan → India: ballistic and cruise missile salvo on air bases and cities in the north",
         {"PK": "attacker", "IN": "defender", "AF": "neutral", "CN": "neutral"}, 14400)


# =====================================================================================================
# 9) Russia -> NATO (Baltic Sea / Poland)
# =====================================================================================================
def ru_vs_baltic():
    b = Builder(seed=3009)
    add_sites(b, [("L01", "Kaliningrad", 54.70, 20.50), ("L02", "Baltiysk (ships)", 54.65, 19.90), ("L03", "Grodno (Belarus)", 53.70, 23.80),
                  ("L04", "Brest (Belarus)", 52.10, 23.70), ("L05", "Pskov", 57.80, 28.40), ("L06", "Mazyr (Belarus)", 52.05, 29.25),
                  ("L07", "Baltic Sea (Kh-101)", 57.00, 19.50), ("L08", "Gomel (Belarus)", 52.40, 31.00)])
    add_targets(b, [
        ("T01", "Vilnius", 54.69, 25.28, "city"), ("T02", "Riga", 56.95, 24.10, "city"), ("T03", "Tallinn", 59.44, 24.75, "city"),
        ("T04", "Warsaw", 52.23, 21.01, "city"), ("T05", "Gdańsk", 54.35, 18.65, "city"), ("T06", "Rzeszów (logistics hub)", 50.11, 22.02, "military_base"),
        ("T07", "Air base Šiauliai", 55.89, 23.39, "military_base"), ("T08", "Air base Ämari", 59.26, 24.20, "military_base"),
        ("T09", "Air base Lielvārde", 56.77, 24.85, "military_base"), ("T10", "Air base Łask", 51.55, 19.18, "military_base"),
        ("T11", "Base Orzysz (NATO)", 53.81, 21.95, "military_base"), ("T12", "Naval base Gdynia", 54.53, 18.55, "military_base"),
        ("T13", "Suwałki Gap (road hub)", 54.10, 22.93, "field"),
    ])
    for lat, lon, nm in [(52.30, 21.00, "Radar Warsaw"), (54.60, 25.20, "Radar Vilnius"), (56.90, 24.10, "Radar Riga"), (59.40, 24.80, "Radar Tallinn"),
                         (54.40, 18.60, "Radar Gdańsk"), (50.10, 22.00, "Radar Rzeszów"), (53.80, 22.00, "Radar Orzysz"), (54.10, 22.90, "Radar Suwałki")]:
        b.defense("R", "radar-ew-long", nm, lat, lon)
    b.ring("PT", "patriot-pac3-mse", "Patriot Warsaw", (52.23, 21.01), 4, 6, 24)
    b.ring("PT", "patriot-pac3-mse", "Patriot Rzeszów", (50.11, 22.02), 2, 4, 14)
    b.defense("PT", "patriot-pac3-mse", "Patriot Gdańsk", 54.38, 18.70)
    b.defense("PT", "patriot-pac3-mse", "Patriot Vilnius", 54.70, 25.30)
    b.ring("NS", "nasams", "NASAMS Lithuania", (55.0, 24.0), 3, 20, 80)
    b.ring("NS", "nasams", "NASAMS Poland", (53.0, 21.5), 3, 20, 80)
    b.ring("IR", "iris-t-slm", "IRIS-T Riga", (56.95, 24.10), 3, 4, 18)
    b.ring("IR", "iris-t-slm", "IRIS-T Tallinn", (59.44, 24.75), 3, 4, 18)
    b.ring("IR", "iris-t-slm", "IRIS-T Vilnius", (54.69, 25.28), 2, 4, 18)
    b.defense("SP", "samp-t", "SAMP/T Warsaw", 52.20, 21.20)
    b.ring("SK", "skynex-35mm", "Skynex Vilnius", (54.69, 25.28), 4, 3, 12)
    b.ring("SK", "skynex-35mm", "Skynex Riga", (56.95, 24.10), 4, 3, 12)
    b.ring("SK", "skynex-35mm", "Skynex Tallinn", (59.44, 24.75), 4, 3, 12)
    b.ring("MG", "mobile-fire-group", "Mobile group Poland", (53.0, 21.5), 10, 10, 120)
    for lat, lon, nm in [(51.55, 19.18, "Base Łask"), (52.33, 16.97, "Base Krzesiny"), (55.89, 23.39, "Base Šiauliai"), (59.26, 24.20, "Base Ämari"), (56.77, 24.85, "Base Lielvārde")]:
        b.defense("AB", "airbase", nm, lat, lon)
    for lat, lon, nm in [(52.40, 19.50, "F-16 Poland center"), (53.80, 22.00, "F-16 Poland east"), (55.50, 23.80, "Patrol NATO Lithuania"), (58.50, 24.50, "Patrol NATO Estonia")]:
        b.defense("PA", "f-16-patrol", nm, lat, lon)

    sead = b.strike_targets(["R01", "R02", "R03", "R04", "R05", "R07"], prefix="X")
    b.wave("W1", "iskander-m", {"L01": 2, "L03": 1}, {k: 1 for k in ("X01", "X02", "X03", "X05", "X06")}, 30, interval=10, spread=150, arrive_at=1800)
    b.wave("W1B", "iskander-m", {"L05": 1}, {"X02": 1, "X03": 1, "X04": 1}, 10, interval=10, spread=150, arrive_at=1800)
    b.wave("W2", "iskander-m", {"L01": 1, "L03": 1}, {k: 1 for k in ("T01", "T02", "T04", "T07", "T09", "T11", "T13")}, 24, interval=10, spread=200, arrive_at=2100)
    b.wave("W2B", "iskander-m", {"L05": 1}, {k: 1 for k in ("T01", "T02", "T03", "T08", "T09")}, 10, interval=10, spread=200, arrive_at=2100)
    b.wave("W2C", "iskander-m", {"L04": 1}, {k: 1 for k in ("T04", "T06", "T11", "T13")}, 10, interval=10, spread=200, arrive_at=2100)
    b.wave("W3", "kalibr-3m14", {"L02": 1, "L07": 1}, {k: 1 for k in ("T04", "T05", "T12", "T02", "T03", "T01", "T06")}, 40, interval=20, spread=250, arrive_at=2400)
    b.wave("W4", "kh-101", {"L07": 1}, {k: 1 for k in ("T04", "T06", "T10", "T01", "T02")}, 30, interval=25, spread=250, arrive_at=2700)
    cities = {f"T{i:02d}": 1 for i in range(1, 14)}
    b.wave("W5", "shahed-136", {"L03": 1, "L04": 1, "L06": 1, "L08": 1}, cities, 200, interval=8, spread=400, arrive_at=14400)
    rt(b, "W1", "W1B", "W2", "W2B", "W2C", "W3", "W4", "W5")
    save(b, "ru-vs-baltic.json", "Russia → NATO East (Baltic and Poland): strikes on air defenses, bases and logistics hubs",
         {"RU": "attacker", "BY": "attackerAlly", "PL": "defender", "LT": "defender", "LV": "defender", "EE": "defender",
          **nato_allies("world", exclude=("PL", "LT", "LV", "EE"), extra=("UA", "MD"))}, 28800)


# =====================================================================================================
# 10) Russia -> NATO South-East (Black Sea: Romania, Bulgaria, Greece, Turkey)
# =====================================================================================================
def ru_vs_natose():
    b = Builder(seed=3010)
    add_sites(b, [("L01", "Western Crimea (Iskander)", 45.35, 33.00), ("L02", "Black Sea Fleet (west)", 43.80, 31.00),
                  ("L03", "Black Sea Fleet (east)", 43.90, 34.50), ("L04", "Crimea", 45.30, 34.00), ("L05", "Kherson", 46.60, 32.60),
                  ("L06", "Launch area Kh-101 (Black Sea)", 44.50, 36.50)])
    add_targets(b, [
        ("T01", "Constanta (port)", 44.18, 28.65, "city"), ("T02", "Air base Mihail Kogălniceanu", 44.36, 28.49, "military_base"),
        ("T03", "Deveselu (anti-ballistic defense)", 44.08, 24.40, "military_base"), ("T04", "Bucharest", 44.43, 26.10, "city"),
        ("T05", "Burgas", 42.50, 27.47, "city"), ("T06", "Naval base Varna", 43.20, 27.90, "military_base"),
        ("T07", "Air base Graf Ignatievo", 42.29, 24.71, "military_base"), ("T08", "Sofia", 42.70, 23.30, "city"),
        ("T09", "Istanbul", 41.00, 28.97, "city"), ("T10", "Alexandroupoli (logistics hub)", 40.85, 25.87, "city"),
        ("T11", "Thessaloniki", 40.64, 22.94, "city"), ("T12", "Air base Çorlu", 41.14, 27.92, "military_base"),
    ])
    for lat, lon, nm in [(44.20, 28.60, "Radar Constanta"), (44.45, 26.10, "Radar Bucharest"), (44.10, 24.40, "Radar Deveselu"),
                         (42.50, 27.45, "Radar Burgas"), (42.70, 23.30, "Radar Sofia"), (41.05, 28.95, "Radar Istanbul"),
                         (40.63, 22.95, "Radar Thessaloniki"), (40.85, 25.90, "Radar Alexandroupoli")]:
        b.defense("R", "radar-ew-long", nm, lat, lon)
    b.ring("PT", "patriot-pac3-mse", "Patriot Bucharest", (44.43, 26.10), 3, 6, 22)
    b.defense("PT", "patriot-pac3-mse", "Patriot Constanta", 44.20, 28.65)
    b.defense("PT", "patriot-pac3-mse", "Patriot Deveselu", 44.10, 24.45)
    b.defense("PT", "patriot-pac3-mse", "Patriot Thessaloniki", 40.65, 22.98)
    b.defense("PT", "patriot-pac3-mse", "Patriot Alexandroupoli", 40.87, 25.90)
    b.defense("NS", "nasams", "NASAMS Mihail Kogălniceanu", 44.38, 28.50)
    b.defense("NS", "nasams", "NASAMS Bucharest", 44.50, 26.05)
    b.defense("S3", "s-300pmu2", "S-300 Sofia", 42.72, 23.35)
    b.defense("S3", "s-300pmu2", "S-300 Burgas", 42.52, 27.50)
    b.defense("S4", "s-400", "S-400 Istanbul", 41.10, 29.00)
    b.defense("S4", "s-400", "S-400 Çorlu", 41.15, 27.95)
    b.ring("IR", "iris-t-slm", "IRIS-T Constanta", (44.18, 28.65), 2, 3, 12)
    b.ring("MG", "mobile-fire-group", "Mobile group Bucharest", (44.43, 26.10), 6, 3, 20)
    b.ring("MG", "mobile-fire-group", "Mobile group Constanta", (44.18, 28.65), 4, 3, 15)
    b.ring("MG", "mobile-fire-group", "Mobile group Bulgaria", (42.9, 25.5), 4, 10, 120)
    b.ring("MG", "mobile-fire-group", "Mobile group Istanbul", (41.0, 28.97), 4, 4, 20)
    for lat, lon, nm in [(44.36, 28.49, "Base Mihail Kogălniceanu"), (44.40, 27.72, "Base Fetești"), (42.29, 24.71, "Base Graf Ignatievo"),
                         (41.14, 27.92, "Base Çorlu"), (40.86, 25.96, "Base Alexandroupoli")]:
        b.defense("AB", "airbase", nm, lat, lon)
    for lat, lon, nm in [(44.40, 27.70, "F-16 Romania"), (43.00, 25.00, "F-16 / MiG-29 Bulgaria"), (41.30, 27.50, "F-16 Turkey"), (40.80, 25.50, "F-16 Greece")]:
        b.defense("PA", "f-16-patrol", nm, lat, lon)

    sead = b.strike_targets(["R01", "PT04", "NS01"], prefix="X")
    b.wave("W1", "iskander-m", {"L01": 1}, sead, 30, interval=10, spread=150, arrive_at=1800)
    b.wave("W2", "iskander-m", {"L01": 1}, {"T01": 2, "T02": 1, "T06": 1}, 20, interval=10, spread=200, arrive_at=2100)
    b.wave("W3", "kalibr-3m14", {"L02": 1, "L03": 1}, {k: 1 for k in ("T01", "T02", "T04", "T05", "T06", "T09", "T12", "T10")}, 50, interval=15, spread=250, arrive_at=2400)
    b.wave("W4", "kh-101", {"L06": 1}, {k: 1 for k in ("T04", "T07", "T08", "T09", "T10", "T11", "T03")}, 30, interval=25, spread=250, arrive_at=2700)
    b.wave("W5", "shahed-136", {"L01": 1, "L04": 1, "L05": 1}, {f"T{i:02d}": 1 for i in range(1, 13)}, 250, interval=6, spread=400, arrive_at=14400)
    rt(b, "W1", "W2", "W3", "W4", "W5")
    save(b, "ru-vs-natose.json", "Russia → NATO South-East (Black Sea): strikes on Romania, Bulgaria, Greece and Turkey",
         {"RU": "attacker", "RO": "defender", "BG": "defender", "GR": "defender", "TR": "defender",
          **nato_allies("world", exclude=("RO", "BG", "GR", "TR"), extra=("UA", "MD"))}, 28800)


if __name__ == "__main__":
    kp_vs_kr()
    cn_vs_tw()
    ir_vs_il()
    ir_vs_il_proxies()
    il_vs_ir()
    kr_vs_kp()
    in_vs_pk()
    pk_vs_in()
    ru_vs_baltic()
    ru_vs_natose()

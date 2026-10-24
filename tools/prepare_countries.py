#!/usr/bin/env python3
"""
Pregateste granitele de tari pentru simulator din Natural Earth (domeniu public).

  python tools/prepare_countries.py --out data/countries/europe.geojson

- descarca ne_50m_admin_0_countries.geojson (sau foloseste --input daca ai deja fisierul)
- pastreaza doar tarile care intersecteaza regiunea (--bbox west south east north)
- taie geometriile la regiune, le simplifica (--tolerance, in grade) si rotunjeste coordonatele
- scrie un FeatureCollection cu proprietatile: iso2, name

Dependinte: pip install shapely
Sursa datelor: https://github.com/nvkelso/natural-earth-vector (Natural Earth, public domain)
"""
import argparse
import json
import sys
import urllib.request

from shapely.geometry import MultiPolygon, box, mapping, shape
from shapely.geometry.polygon import orient

SOURCE_URL = (
    "https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/"
    "geojson/ne_50m_admin_0_countries.geojson"
)

# Natural Earth da ISO_A2 = -99 pentru cateva tari; ISO_A2_EH le corecteaza (FR, NO, XK).
# Pentru restul, un cod propriu (nu exista cod ISO oficial).
FALLBACK_CODES = {"N. Cyprus": "XN"}


def iso2_of(props):
    code = props.get("ISO_A2_EH")
    if code and code != "-99":
        return code
    code = props.get("ISO_A2")
    if code and code != "-99":
        return code
    return FALLBACK_CODES.get(props.get("NAME", ""))


def polygons_of(geom):
    if geom.is_empty:
        return []
    if geom.geom_type == "Polygon":
        return [geom]
    if geom.geom_type in ("MultiPolygon", "GeometryCollection"):
        return [p for sub in geom.geoms for p in polygons_of(sub)]
    return []


def round_coords(obj, digits):
    if isinstance(obj, (list, tuple)):
        if obj and isinstance(obj[0], (int, float)):
            return [round(v, digits) for v in obj]
        return [round_coords(o, digits) for o in obj]
    return obj


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--input", help="ne_50m_admin_0_countries.geojson local (implicit: se descarca)")
    ap.add_argument("--out", required=True)
    ap.add_argument("--bbox", nargs=4, type=float, default=[-25, 25, 70, 75], metavar=("W", "S", "E", "N"))
    ap.add_argument("--tolerance", type=float, default=0.01, help="simplificare in grade (0.01 ~ 1 km)")
    ap.add_argument("--digits", type=int, default=3)
    args = ap.parse_args()

    if args.input:
        with open(args.input, encoding="utf-8") as f:
            data = json.load(f)
    else:
        with urllib.request.urlopen(SOURCE_URL, timeout=120) as resp:
            data = json.load(resp)

    region = box(*args.bbox)
    features, seen = [], set()

    for feat in data["features"]:
        props = feat["properties"]
        code = iso2_of(props)
        if not code:
            continue

        geom = shape(feat["geometry"])
        if not geom.intersects(region):
            continue

        # intersectia cu regiunea poate produce si linii/puncte la margine: pastram doar poligoanele
        polygons = [
            p
            for p in polygons_of(geom.intersection(region).simplify(args.tolerance, preserve_topology=True))
            if p.area > 0
        ]
        if not polygons:
            continue
        polygons = [orient(p, 1.0) for p in polygons]
        clipped = polygons[0] if len(polygons) == 1 else MultiPolygon(polygons)

        if code in seen:
            print(f"avertisment: cod duplicat {code} ({props.get('NAME')}), ignorat", file=sys.stderr)
            continue
        seen.add(code)

        geometry = mapping(clipped)
        geometry = {"type": geometry["type"], "coordinates": round_coords(geometry["coordinates"], args.digits)}
        features.append(
            {
                "type": "Feature",
                "properties": {"iso2": code, "name": props.get("NAME_EN") or props.get("NAME")},
                "geometry": geometry,
            }
        )

    features.sort(key=lambda f: f["properties"]["iso2"])
    out = {"type": "FeatureCollection", "features": features}
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, separators=(",", ":"))

    print(f"{len(features)} tari scrise in {args.out}")


if __name__ == "__main__":
    main()

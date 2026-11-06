"""
Prepares the country borders for the simulator from Natural Earth (public domain).

  python tools/prepare_countries.py --out data/countries/europe.geojson

- downloads ne_50m_admin_0_countries.geojson (or uses --input if you already have the file)
- keeps only the countries that intersect the region (--bbox west south east north)
- clips the geometries to the region, simplifies them (--tolerance, in degrees) and rounds the coordinates
- writes a FeatureCollection with the properties: iso2, name

Dependencies: pip install shapely
Data source: https://github.com/nvkelso/natural-earth-vector (Natural Earth, public domain)
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

# Natural Earth gives ISO_A2 = -99 for a few countries; ISO_A2_EH corrects them (FR, NO, XK).
# For the rest, an own code (there is no official ISO code).
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
    ap.add_argument("--input", help="local ne_50m_admin_0_countries.geojson (default: downloaded)")
    ap.add_argument("--out", required=True)
    ap.add_argument("--bbox", nargs=4, type=float, default=[-25, 25, 70, 75], metavar=("W", "S", "E", "N"))
    ap.add_argument("--tolerance", type=float, default=0.01, help="simplification in degrees (0.01 ~ 1 km)")
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

        # the intersection with the region may also produce lines/points at the edge: keep only the polygons
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
            print(f"warning: duplicate code {code} ({props.get('NAME')}), ignored", file=sys.stderr)
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

    print(f"{len(features)} countries written to {args.out}")


if __name__ == "__main__":
    main()

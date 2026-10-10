#!/usr/bin/env python3
"""Convert the Tycho-2 catalogue (CDS I/259) into a compact binary star file.

Input  (raw/):  tyc2.dat.00.gz .. tyc2.dat.19.gz, suppl_1.dat.gz
Output:         AstroTracker/Assets/Resources/StarCatalog/tycho2.bytes (a Unity TextAsset)

Positions are moved to the target epoch with the catalogue proper motions and
sorted into 1-degree declination bands (south to north), by RA inside a band.

File layout, little-endian:
    char[4]  magic      "ATSC"
    uint32   version    1
    uint32   starCount
    float32  epoch      Julian year the positions refer to
    float32  maxMag     magnitude cut used
    uint32   bandCount  180
    uint32   bandStart[bandCount + 1]   index of the first star of each band
    Star     stars[starCount]

Star, 12 bytes:
    uint32   ra     degrees = ra  * 360 / 2^32
    int32    dec    degrees = dec * 90  / (2^31 - 1)
    int16    mag    V magnitude * 1000
    uint16   prox   distance to the nearest catalogue star, 0.1 arcsec (999 = far)

Usage:
    python convert_tycho2.py [--max-mag 11.5] [--epoch 2026.8]
    python convert_tycho2.py --cone 148.97 69.68 1.0     # query the output file
"""

import argparse
import datetime
import glob
import gzip
import math
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_OUT = os.path.normpath(os.path.join(
    HERE, "..", "..", "AstroTracker", "Assets", "Resources", "StarCatalog", "tycho2.bytes"))
MAGIC = b"ATSC"
VERSION = 1
BAND_COUNT = 180
HEADER = struct.Struct("<4sIIffI")
STAR = struct.Struct("<IihH")
RA_SCALE = 2 ** 32 / 360.0
DEC_SCALE = (2 ** 31 - 1) / 90.0
MAS_TO_RAD = math.radians(1.0 / 3.6e6)


def to_float(text):
    text = text.strip()
    return float(text) if text else None


def v_magnitude(bt, vt):
    """Approximate Johnson V from Tycho BT/VT (ReadMe, note 7)."""
    if vt is None:
        return bt
    if bt is None:
        return vt
    return vt - 0.090 * (bt - vt)


def propagate(ra, dec, pm_ra, pm_dec, years):
    """Move a position along its proper motion; safe near the poles."""
    if not years or (not pm_ra and not pm_dec):
        return ra, dec
    a, d = math.radians(ra), math.radians(dec)
    sa, ca, sd, cd = math.sin(a), math.cos(a), math.sin(d), math.cos(d)
    east = pm_ra * MAS_TO_RAD * years
    north = pm_dec * MAS_TO_RAD * years
    x = cd * ca - sa * east - sd * ca * north
    y = cd * sa + ca * east - sd * sa * north
    z = sd + cd * north
    return (math.degrees(math.atan2(y, x)) % 360.0,
            math.degrees(math.atan2(z, math.hypot(x, y))))


def read_main(path):
    """Yield (ra, dec, pmRA, pmDE, epoch, mag, prox) from a tyc2.dat part."""
    with gzip.open(path, "rt", encoding="ascii") as f:
        for line in f:
            c = line.split("|")
            mag = v_magnitude(to_float(c[17]), to_float(c[19]))
            if c[1] == "X":
                # No mean position: fall back to the observed one, no proper motion.
                epoch = 1990.0 + (float(c[26]) + float(c[27])) / 2
                yield float(c[24]), float(c[25]), 0.0, 0.0, epoch, mag, int(c[21])
            else:
                yield (float(c[2]), float(c[3]), to_float(c[4]) or 0.0,
                       to_float(c[5]) or 0.0, 2000.0, mag, int(c[21]))


def read_supplement(path):
    """Yield the same tuple from suppl_1.dat (positions at J1991.25)."""
    with gzip.open(path, "rt", encoding="ascii") as f:
        for line in f:
            c = line.split("|")
            mag = v_magnitude(to_float(c[11]), to_float(c[13]))
            yield (float(c[2]), float(c[3]), to_float(c[4]) or 0.0,
                   to_float(c[5]) or 0.0, 1991.25, mag, int(c[15]))


def convert(raw_dir, out_path, max_mag, epoch):
    parts = sorted(glob.glob(os.path.join(raw_dir, "tyc2.dat.*.gz")))
    supplement = os.path.join(raw_dir, "suppl_1.dat.gz")
    if len(parts) != 20 or not os.path.exists(supplement):
        sys.exit(f"Expected 20 tyc2.dat parts and suppl_1.dat.gz in {raw_dir}")

    bands = [[] for _ in range(BAND_COUNT)]
    total = kept = 0
    sources = [read_main(p) for p in parts] + [read_supplement(supplement)]
    for source in sources:
        for ra, dec, pm_ra, pm_dec, star_epoch, mag, prox in source:
            total += 1
            if mag is None or mag > max_mag:
                continue
            ra, dec = propagate(ra, dec, pm_ra, pm_dec, epoch - star_epoch)
            band = min(BAND_COUNT - 1, int(dec + 90.0))
            bands[band].append((ra, dec, mag, prox))
            kept += 1

    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    with open(out_path, "wb") as out:
        out.write(HEADER.pack(MAGIC, VERSION, kept, epoch, max_mag, BAND_COUNT))
        start = 0
        for band in bands:
            out.write(struct.pack("<I", start))
            start += len(band)
        out.write(struct.pack("<I", start))
        for band in bands:
            band.sort()
            for ra, dec, mag, prox in band:
                out.write(STAR.pack(min(2 ** 32 - 1, int(ra * RA_SCALE)),
                                    round(dec * DEC_SCALE),
                                    round(mag * 1000), prox))

    size_mb = os.path.getsize(out_path) / 1e6
    print(f"read {total} stars, kept {kept} (V <= {max_mag}), epoch J{epoch:.2f}")
    print(f"mean density {kept / 41252.96:.1f} stars/sq.deg")
    print(f"wrote {out_path} ({size_mb:.1f} MB)")


def cone(path, ra0, dec0, radius):
    """Print the stars within `radius` degrees of (ra0, dec0), brightest first."""
    with open(path, "rb") as f:
        magic, version, count, epoch, max_mag, band_count = HEADER.unpack(f.read(HEADER.size))
        if magic != MAGIC or version != VERSION:
            sys.exit("Not a version 1 ATSC file")
        band_start = struct.unpack(f"<{band_count + 1}I", f.read(4 * (band_count + 1)))
        data_offset = f.tell()

        first = max(0, int(dec0 - radius + 90.0))
        last = min(band_count - 1, int(dec0 + radius + 90.0))
        f.seek(data_offset + band_start[first] * STAR.size)
        blob = f.read((band_start[last + 1] - band_start[first]) * STAR.size)

    a0, d0 = math.radians(ra0), math.radians(dec0)
    found = []
    for ra_i, dec_i, mag_i, prox in STAR.iter_unpack(blob):
        ra, dec = ra_i / RA_SCALE, dec_i / DEC_SCALE
        a, d = math.radians(ra), math.radians(dec)
        cos_sep = math.sin(d0) * math.sin(d) + math.cos(d0) * math.cos(d) * math.cos(a - a0)
        sep = math.degrees(math.acos(max(-1.0, min(1.0, cos_sep))))
        if sep <= radius:
            found.append((mag_i / 1000.0, ra, dec, sep, prox))

    found.sort()
    print(f"{count} stars in file, epoch J{epoch:.2f}, V <= {max_mag:.2f}")
    print(f"{len(found)} stars within {radius} deg of ({ra0}, {dec0})")
    print("   V         RA         Dec     sep   prox")
    for mag, ra, dec, sep, prox in found[:30]:
        print(f"{mag:6.3f} {ra:11.6f} {dec:+11.6f} {sep:6.3f} {prox:5d}")


def main():
    today = datetime.date.today()
    now = today.year + (today.timetuple().tm_yday - 1) / 365.25

    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--raw", default=os.path.join(HERE, "raw"))
    parser.add_argument("--out", default=DEFAULT_OUT)
    parser.add_argument("--max-mag", type=float, default=11.5)
    parser.add_argument("--epoch", type=float, default=round(now, 2))
    parser.add_argument("--cone", type=float, nargs=3, metavar=("RA", "DEC", "RADIUS"),
                        help="query the output file instead of converting (degrees)")
    args = parser.parse_args()

    if args.cone:
        cone(args.out, *args.cone)
    else:
        convert(args.raw, args.out, args.max_mag, args.epoch)


if __name__ == "__main__":
    main()

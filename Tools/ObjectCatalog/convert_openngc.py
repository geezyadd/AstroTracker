#!/usr/bin/env python3
"""Convert the OpenNGC database into the compact target list used by the app.

Source: OpenNGC by Mattia Verga, https://github.com/mattiaverga/OpenNGC
        (CC-BY-SA-4.0; the generated file is a derivative and stays under that license).

Input  (raw/):  NGC.csv, addendum.csv from the repository's database_files folder
Output:         AstroTracker/Assets/Resources/ObjectCatalog/objects.txt (a Unity TextAsset)

One object per line, tab-separated, coordinates J2000:
    designations   "M 82|NGC 3034", the first one is shown as the object's name
    names          "Cigar Galaxy", several separated by "|", may be empty
    type           OpenNGC type code: G, OCl, GCl, PN, HII, ...
    ra             degrees
    dec            degrees
    magnitude      V, or B when V is unknown; empty when neither is
    size           major axis in arcminutes, may be empty

Usage:
    python convert_openngc.py
"""

import csv
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.join(HERE, "raw")
OUT = os.path.normpath(os.path.join(
    HERE, "..", "..", "AstroTracker", "Assets", "Resources", "ObjectCatalog", "objects.txt"))
SKIPPED_TYPES = {"Dup", "NonEx"}


def designation(name):
    """'NGC3034' -> 'NGC 3034', 'IC0434' -> 'IC 434', 'NGC0055A' -> 'NGC 55A'."""
    match = re.match(r"^([A-Za-z]+)0*(\d.*)$", name.strip())
    return f"{match.group(1)} {match.group(2)}" if match else name.strip()


def sexagesimal(text):
    sign = -1.0 if text.startswith("-") else 1.0
    parts = [float(p) for p in text.lstrip("+-").split(":")]
    return sign * (parts[0] + parts[1] / 60 + parts[2] / 3600)


def add(items, value):
    if value and value not in items:
        items.append(value)


def read(path):
    with open(path, encoding="utf-8", newline="") as f:
        return list(csv.DictReader(f, delimiter=";"))


def main():
    rows = read(os.path.join(RAW, "NGC.csv")) + read(os.path.join(RAW, "addendum.csv"))

    # A duplicate entry is another designation of the object named in its NGC, IC or M column.
    duplicates = {}
    for row in rows:
        if row["Type"] != "Dup":
            continue
        target = ("NGC" + row["NGC"] if row["NGC"] else "IC" + row["IC"] if row["IC"]
                  else "M" + row["M"] if row["M"] else None)
        if target:
            duplicates.setdefault(target, []).append(designation(row["Name"]))

    lines = []
    for row in rows:
        if row["Type"] in SKIPPED_TYPES or not row["RA"] or not row["Dec"]:
            continue

        ids = []
        if row["M"]:
            add(ids, f"M {int(row['M'])}")
        add(ids, designation(row["Name"]))
        if row["NGC"]:
            add(ids, designation("NGC" + row["NGC"]))
        if row["IC"]:
            add(ids, designation("IC" + row["IC"]))
        for other in row["Identifiers"].split(","):
            if re.match(r"^C \d+$", other.strip()):  # Caldwell number
                add(ids, designation(other.replace(" ", "")))
        for other in duplicates.get(row["Name"], []) + duplicates.get("M" + row["M"], []):
            add(ids, other)
        ids.sort(key=lambda i: not i.startswith("M "))  # a Messier number is the name people know

        names = []
        for name in row["Common names"].split(","):
            add(names, name.strip())

        lines.append("\t".join([
            "|".join(ids),
            "|".join(names),
            row["Type"],
            f"{sexagesimal(row['RA']) * 15:.5f}",
            f"{sexagesimal(row['Dec']):.5f}",
            row["V-Mag"] or row["B-Mag"],
            row["MajAx"],
        ]))

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    print(f"read {len(rows)} entries, wrote {len(lines)} objects to {OUT} ({os.path.getsize(OUT) / 1e3:.0f} kB)")


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
"""Convert the IAU Catalog of Star Names into a target list of named stars for the app.

Source: IAU Division C Working Group on Star Names, https://www.iau.org/public/themes/naming_stars/
        (Creative Commons Attribution; the list is mirrored at
        https://www.pas.rochester.edu/~emamajek/WGSN/IAU-CSN.txt).

Input  (raw/):  IAU-CSN.txt
Output:         AstroTracker/Assets/Resources/ObjectCatalog/stars.txt (a Unity TextAsset)

Same layout as objects.txt (see convert_openngc.py), coordinates J2000:
    designations   "Capella|Alpha Aur|HIP 24608|HR 1708", the first one is shown as the star's name
    names          the Bayer designation with its Greek letter, "α Aur"; may be empty
    type           "*"
    ra, dec        degrees
    magnitude      as listed, mostly V
    size           empty

Usage:
    python convert_stars.py
"""

import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SOURCE = os.path.join(HERE, "raw", "IAU-CSN.txt")
OUT = os.path.normpath(os.path.join(
    HERE, "..", "..", "AstroTracker", "Assets", "Resources", "ObjectCatalog", "stars.txt"))

GREEK = {
    "alf": "Alpha", "bet": "Beta", "gam": "Gamma", "del": "Delta", "eps": "Epsilon", "zet": "Zeta",
    "eta": "Eta", "tet": "Theta", "iot": "Iota", "kap": "Kappa", "lam": "Lambda", "mu": "Mu", "nu": "Nu",
    "ksi": "Xi", "omi": "Omicron", "pi": "Pi", "rho": "Rho", "sig": "Sigma", "tau": "Tau", "ups": "Upsilon",
    "phi": "Phi", "chi": "Chi", "psi": "Psi", "ome": "Omega",
}
# Columns counted from the end of a row; the name and the designation in front may contain spaces.
TAIL = ["id", "id_greek", "constellation", "component", "wds", "magnitude", "band", "hip", "hd", "ra", "dec", "date"]
NAME_WIDTH = 18


def bayer(code, constellation):
    """'alf' -> 'Alpha Aur', 'tet01' -> 'Theta1 Eri'; None when the code is not a Greek letter."""
    match = re.match(r"^([a-z]+)0*(\d*)$", code)
    if not match or match.group(1) not in GREEK:
        return None
    return f"{GREEK[match.group(1)]}{match.group(2)} {constellation}"


def main():
    lines = []
    with open(SOURCE, encoding="utf-8") as f:
        for row in f:
            if row.startswith(("#", "$")) or not row.strip():
                continue
            name = row[:NAME_WIDTH].strip()
            tokens = row[2 * NAME_WIDTH:].split()
            if tokens[-1] == "*":
                tokens.pop()
            if len(tokens) < len(TAIL):
                continue
            star = dict(zip(TAIL, tokens[-len(TAIL):]))
            designation = " ".join(tokens[:-len(TAIL)])
            if star["ra"] == "_" or star["dec"] == "_":
                continue

            ids = [name]
            greek_name = ""
            spelled = bayer(star["id"], star["constellation"])
            if spelled:
                ids.append(spelled)
                greek_name = f"{star['id_greek']} {star['constellation']}"
            if star["hip"] != "_":
                ids.append(f"HIP {star['hip']}")
            if designation and designation not in ids:
                ids.append(designation)

            lines.append("\t".join([
                "|".join(ids),
                greek_name,
                "*",
                f"{float(star['ra']):.5f}",
                f"{float(star['dec']):.5f}",
                "" if star["magnitude"] == "_" else star["magnitude"],
                "",
            ]))

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    print(f"wrote {len(lines)} stars to {OUT} ({os.path.getsize(OUT) / 1e3:.0f} kB)")


if __name__ == "__main__":
    sys.exit(main())

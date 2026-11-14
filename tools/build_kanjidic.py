#!/usr/bin/env python3
"""Build ScribeVG/Data/kanjidic.json from KANJIDIC2.

Download kanjidic2.xml.gz from https://www.edrdg.org/kanjidic/kanjidic2.xml.gz
then run:  python3 tools/build_kanjidic.py kanjidic2.xml.gz ScribeVG/Data/kanjidic.json

Keeps only what the app shows: English meanings, on/kun readings, nanori,
school grade, JLPT level (old 4-level scale), stroke count and frequency rank.
KANJIDIC2 is (c) the Electronic Dictionary Research and Development Group,
licensed CC BY-SA 4.0: https://www.edrdg.org/edrdg/licence.html
"""
import gzip, json, sys, xml.etree.ElementTree as ET

src, out = sys.argv[1], sys.argv[2]
opener = gzip.open if src.endswith(".gz") else open
with opener(src, "rb") as f:
    root = ET.parse(f).getroot()

header = root.find("header")
entries = {}
for ch in root.iter("character"):
    literal = ch.findtext("literal")
    misc = ch.find("misc")
    rm = ch.find("reading_meaning")
    meanings, on, kun = [], [], []
    if rm is not None:
        for group in rm.iter("rmgroup"):
            for m in group.findall("meaning"):
                if "m_lang" not in m.attrib:
                    meanings.append(m.text)
            for r in group.findall("reading"):
                if r.get("r_type") == "ja_on": on.append(r.text)
                elif r.get("r_type") == "ja_kun": kun.append(r.text)
    nanori = [n.text for n in ch.iter("nanori")]
    e = {"m": meanings, "on": on, "kun": kun}
    if nanori: e["nanori"] = nanori
    def num(tag):
        v = misc.findtext(tag) if misc is not None else None
        return int(v) if v else None
    for key, tag in (("grade", "grade"), ("jlpt", "jlpt"), ("freq", "freq")):
        v = num(tag)
        if v is not None: e[key] = v
    sc = misc.findall("stroke_count") if misc is not None else []
    if sc: e["strokes"] = int(sc[0].text)
    entries[literal] = e

doc = {
    "source": "KANJIDIC2, (c) EDRDG, CC BY-SA 4.0, https://www.edrdg.org/wiki/index.php/KANJIDIC_Project",
    "version": header.findtext("database_version"),
    "date": header.findtext("date_of_creation"),
    "kanji": entries,
}
with open(out, "w", encoding="utf-8") as f:
    json.dump(doc, f, ensure_ascii=False, separators=(",", ":"))
print(f"{len(entries)} kanji -> {out}")

#!/usr/bin/env python3
"""Bundle KanjiVG stroke files for ScribeVG and build Data/components.json.

Download the KanjiVG "main" release zip (base files only, no -Kaisho etc. variants):
    https://github.com/KanjiVG/kanjivg/releases  ->  kanjivg-YYYYMMDD-main.zip
then run (after tools/build_kanjidic.py):
    python3 tools/build_kanjivg_bundle.py kanjivg-YYYYMMDD-main.zip \
        ScribeVG/Data/kanjidic.json ScribeVG/Data/kanjivg ScribeVG/Data/components.json

Bundles every kanji that KANJIDIC2 gives a school grade or an (old, 4-level)
JLPT level, plus any kanji those are built from, so parts can be learned
before the whole. SVGs are copied unchanged.

components.json lists, for each bundled kanji, its *direct* kanji components
taken from the kvg:element attributes on KanjiVG <g> groups. A group counts
as a component when its element is a real kanji (in KANJIDIC2) with its own
KanjiVG file and more than one stroke; we don't look inside a group once it
counts (so a component of a component is not repeated). One-stroke parts
(一 丶 丿 乙 亅 丨) are skipped so they don't gate everything.

KanjiVG (c) Ulrich Apel and contributors, CC BY-SA 3.0, https://kanjivg.tagaini.net
"""
import json, os, re, sys, zipfile
import xml.etree.ElementTree as ET

KVG = "{http://kanjivg.tagaini.net}"
SVG = "{http://www.w3.org/2000/svg}"

zip_path, dic_path, out_dir, comp_path = sys.argv[1:5]
version = (re.search(r"(\d{8})", os.path.basename(zip_path)) or [None, "unknown"])[1]

with open(dic_path, encoding="utf-8") as f:
    kanjidic = json.load(f)["kanji"]

zf = zipfile.ZipFile(zip_path)
# Base files are named kanji/<5-hex-digit codepoint>.svg; variants have a "-Suffix".
files = {}
for name in zf.namelist():
    m = re.fullmatch(r"kanji/([0-9a-f]{5})\.svg", name)
    if m:
        files[chr(int(m[1], 16))] = name

def parse(ch):
    # KanjiVG declares the kvg namespace in an internal DTD; ElementTree ignores DTDs,
    # so map the prefix by hand.
    raw = zf.read(files[ch]).decode("utf-8")
    raw = re.sub(r"<!DOCTYPE.*?\]>", "", raw, flags=re.S)
    raw = raw.replace('xmlns:kvg="https://kanjivg.tagaini.net/"', 'xmlns:kvg="http://kanjivg.tagaini.net"')
    if 'xmlns:kvg=' not in raw:
        raw = raw.replace("<svg ", '<svg xmlns:kvg="http://kanjivg.tagaini.net" ', 1)
    return ET.fromstring(raw)

_trees, _strokes = {}, {}
def tree(ch):
    if ch not in _trees:
        _trees[ch] = parse(ch)
    return _trees[ch]

def stroke_count(ch):
    if ch not in _strokes:
        _strokes[ch] = sum(1 for p in tree(ch).iter(SVG + "path") if "-s" in p.get("id", ""))
    return _strokes[ch]

def is_component(el, ch):
    return (el and el != ch and el in kanjidic and el in files and stroke_count(el) > 1)

def direct_components(ch):
    root = tree(ch)
    top = next((g for g in root.iter(SVG + "g") if g.get("id") == f"kvg:{ord(ch):05x}"), None)
    found = []
    def walk(node):
        for g in node.findall(SVG + "g"):
            el = g.get(KVG + "element")
            if is_component(el, ch):
                if el not in found:
                    found.append(el)
            else:
                walk(g)
    if top is not None:
        walk(top)
    return found

targets = sorted(k for k, e in kanjidic.items() if ("jlpt" in e or "grade" in e) and k in files)
# CJK compatibility ideographs (U+F900-FAFF, e.g. old-form 海 神 in names) share the
# base kanji's glyph and have no KanjiVG file of their own.
missing = sorted(k for k, e in kanjidic.items() if ("jlpt" in e or "grade" in e) and k not in files
                 and not 0xF900 <= ord(k) <= 0xFAFF)

components, queue = {}, list(targets)
while queue:
    ch = queue.pop()
    if ch in components:
        continue
    components[ch] = direct_components(ch)
    queue.extend(c for c in components[ch] if c not in components)

# Refuse cycles: a kanji must never (indirectly) need itself.
state = {}
def visit(ch, path):
    if state.get(ch) == 2: return
    if state.get(ch) == 1: raise SystemExit(f"component cycle: {' -> '.join(path + [ch])}")
    state[ch] = 1
    for c in components[ch]: visit(c, path + [ch])
    state[ch] = 2
for ch in components: visit(ch, [])

os.makedirs(out_dir, exist_ok=True)
for name in os.listdir(out_dir):
    if name.endswith(".svg"):
        os.remove(os.path.join(out_dir, name))
for ch in components:
    with open(os.path.join(out_dir, f"{ord(ch):05x}.svg"), "wb") as f:
        f.write(zf.read(files[ch]))

doc = {
    "source": "KanjiVG (c) Ulrich Apel and contributors, CC BY-SA 3.0, https://kanjivg.tagaini.net",
    "version": version,
    "note": "Direct kanji components from kvg:element groups; one-stroke parts omitted.",
    "kanji": {ch: components[ch] for ch in sorted(components, key=ord)},
}
with open(comp_path, "w", encoding="utf-8") as f:
    json.dump(doc, f, ensure_ascii=False, separators=(",", ":"))

extra = len(components) - len(targets)
print(f"{len(targets)} graded/JLPT kanji + {extra} component-only kanji = {len(components)} SVGs -> {out_dir}")
if missing:
    print(f"{len(missing)} graded/JLPT kanji have no KanjiVG file: {''.join(missing)}")
for ch in "氷休森明海語":
    print(ch, components.get(ch))

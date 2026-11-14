#!/usr/bin/env python3
"""Build ScribeVG/Data/sentences.json from Louis's UmaiKanji database.

    python3 tools/build_sentences.py umaikanji.sqlite ScribeVG/Data/kanjidic.json ScribeVG/Data/sentences.json

umaikanji.sqlite (from the UmaiKanji project, not in this repo) holds Japanese
example sentences from the Tatoeba Project (https://tatoeba.org, CC BY 2.0 FR),
their English translations (table `links`), and per-sentence word lists with
kana readings (table `tvocab`). Sentence ids are Tatoeba ids, and the matching
recording by Emi Asano is `{id}.m4a` (the audio is not bundled; ScribeVG plays it
from a local folder).

Output (compact JSON):
  {"source": ..., "k": {"水": [id, id, ...]},           # best-first, up to MAX_PER_KANJI
   "s": {"4702": {"j": ja, "r": kana reading, "e": English, "a": 1 if audio}}}

Ranking prefers sentences that have audio and an English translation, are short,
and whose other kanji are easy (old JLPT 4 first, then school grade, then frequency).
"""
import json, sqlite3, sys

MAX_PER_KANJI = 8

db, kanjidic_path, out = sys.argv[1], sys.argv[2], sys.argv[3]
kanjidic = json.load(open(kanjidic_path, encoding="utf-8"))["kanji"]


def is_kanji(ch):
    o = ord(ch)
    return 0x4E00 <= o <= 0x9FFF or 0x3400 <= o <= 0x4DBF or 0xF900 <= o <= 0xFAFF or 0x20000 <= o <= 0x2FFFF or ch == "々"


def difficulty(ch):
    """Roughly 0 (easy, e.g. 日) .. 10 (rare)."""
    if ch == "々":
        return 0.0
    e = kanjidic.get(ch)
    if e is None:
        return 10.0
    jlpt = e.get("jlpt")  # old scale: 4 easiest .. 1 hardest
    d = (4 - jlpt) * 1.5 if jlpt else 6.0
    g = e.get("grade")
    d += (g - 1) * 0.3 if g and g <= 6 else (2.0 if g else 3.0)
    d += min(e.get("freq", 2500), 2500) / 1250
    return d


con = sqlite3.connect(db)
ja = {sid: (phrase, bool(audio)) for sid, phrase, audio in
      con.execute("select sid, phrase, hasAudio from sentences where lang='jpn' and phrase is not null")}

# English: the lowest-id linked translation (usually the original Tatoeba pair).
english = {}
for sid, phrase in con.execute(
        "select l.sid, e.phrase from links l join sentences e on e.sid = l.tid and e.lang = 'eng' "
        "where l.sid in (select sid from sentences where lang = 'jpn') order by e.sid desc"):
    english[sid] = phrase

vocab = {}
for sid, word, reading in con.execute("select sid, word, reading from tvocab"):
    if word and reading and word != reading:
        vocab.setdefault(sid, {})[word] = reading


def kana_reading(sid, text):
    """Replace each word with its reading, longest match first, left to right."""
    words = vocab.get(sid, {})
    if not words:
        return None
    lengths = sorted({len(w) for w in words}, reverse=True)
    out, i = [], 0
    while i < len(text):
        for n in lengths:
            w = text[i:i + n]
            if w in words:
                out.append(words[w]); i += n; break
        else:
            out.append(text[i]); i += 1
    r = "".join(out)
    return r if r != text else None


candidates = {}
for sid, (text, audio) in ja.items():
    kanji = [c for c in text if is_kanji(c) and c != "々"]
    if not kanji:
        continue
    for k in set(kanji):
        others = [difficulty(c) for c in set(kanji) if c != k]
        hardest = max(others, default=0.0)
        mean = sum(others) / len(others) if others else 0.0
        cost = (0 if audio else 100) + (0 if sid in english else 50) \
            + len(text) * 0.25 + hardest * 1.0 + mean * 0.5 + len(others) * 0.3
        candidates.setdefault(k, []).append((cost, sid))

ranked, used = {}, set()
for k, items in candidates.items():
    items.sort()
    ids = [sid for _, sid in items[:MAX_PER_KANJI]]
    ranked[k] = ids
    used.update(ids)

sentences = {}
complete = 0
for sid in sorted(used):
    text, audio = ja[sid]
    e = {"j": text}
    r = kana_reading(sid, text)
    if r:
        e["r"] = r
        if not any(is_kanji(c) for c in r):
            complete += 1
    if sid in english:
        e["e"] = english[sid]
    if audio:
        e["a"] = 1
    sentences[str(sid)] = e

doc = {
    "source": "Sentences: Tatoeba Project (tatoeba.org), CC BY 2.0 FR, via Louis Waweru's UmaiKanji. "
              "Audio (not bundled): recorded by Emi Asano.",
    "k": dict(sorted(ranked.items(), key=lambda kv: kv[0])),
    "s": sentences,
}
with open(out, "w", encoding="utf-8") as f:
    json.dump(doc, f, ensure_ascii=False, separators=(",", ":"))
print(f"{len(ranked)} kanji, {len(sentences)} sentences ({complete} with a full kana reading, "
      f"{sum(1 for s in sentences.values() if 'a' in s)} with audio) -> {out}")

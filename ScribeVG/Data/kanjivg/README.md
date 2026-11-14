# KanjiVG stroke files

The `.svg` stroke-order files in this folder come from the
[KanjiVG project](https://kanjivg.tagaini.net) ([GitHub](https://github.com/KanjiVG/kanjivg))
by Ulrich Apel and contributors, release r20260714, licensed under
[CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/). They are copied
unchanged from `kanjivg-20260714-main.zip` (base forms only, no Kaisho or other variants).

`tools/build_kanjivg_bundle.py` reproduces this folder: it bundles every kanji that
KANJIDIC2 gives a school grade or an (old, 4-level) JLPT level, about 2,900, plus the
roughly 220 parts they are built from (e.g. 亻 氵 宀 艹), 3,128 files in all.

`../components.json` is derived from the same KanjiVG files (and so is also
CC BY-SA 3.0): for each bundled kanji, its direct kanji parts from the `kvg:element`
groups, e.g. 氷 → 水, 明 → 日 月. ScribeVG uses it to unlock a kanji only after
you have learned its parts.

ScribeVG reads the files directly. Pick another folder in the app to practice
other KanjiVG characters.

## Meanings and readings

`Data/kanjidic.json` is generated from [KANJIDIC2](https://www.edrdg.org/wiki/index.php/KANJIDIC_Project)
by `tools/build_kanjidic.py`. KANJIDIC2 is the property of the
[Electronic Dictionary Research and Development Group](https://www.edrdg.org/)
and is used under [CC BY-SA 4.0](https://www.edrdg.org/edrdg/licence.html).

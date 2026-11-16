#!/usr/bin/env python3
"""Build ScribeVG/Assets/ScribeVG.ico from ScribeVG/Assets/ScribeVG.svg.

The SVG is original ScribeVG artwork (MIT), hand-constructed, not from KanjiVG
or a font. Sizes 16, 20, 24, 32, 40, 48, 64 and 128 are stored as 32-bit BMP
(with alpha); 256 is PNG-compressed, which Windows and WPF both accept.

Requires cairosvg and Pillow:

    python3 -m pip install cairosvg pillow
    python3 tools/build_icon.py

Run from the repository root. Pass --svg / --ico to override paths.
"""

from __future__ import annotations

import argparse
import io
import struct
import sys
from pathlib import Path

try:
    import cairosvg
except ImportError:
    sys.exit("cairosvg is required: python3 -m pip install cairosvg")

try:
    from PIL import Image
except ImportError:
    sys.exit("Pillow is required: python3 -m pip install pillow")

REPO = Path(__file__).resolve().parent.parent
DEFAULT_SVG = REPO / "ScribeVG" / "Assets" / "ScribeVG.svg"
DEFAULT_ICO = REPO / "ScribeVG" / "Assets" / "ScribeVG.ico"

# 20 and 40 are used by Windows at 125% and some title-bar metrics.
SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)
PNG_SIZE = 256


def render_png(svg_path: Path, px: int) -> bytes:
    return cairosvg.svg2png(
        url=str(svg_path),
        output_width=px,
        output_height=px,
    )


def ico_bmp_with_alpha(im: Image.Image) -> bytes:
    """32-bit ICO DIB: BITMAPINFOHEADER + BGRA XOR (bottom-up) + 1-bpp AND mask."""
    im = im.convert("RGBA")
    w, h = im.size
    rgba = im.tobytes()
    xor = bytearray(w * 4 * h)
    for y in range(h):
        dst_y = h - 1 - y
        for x in range(w):
            i = (y * w + x) * 4
            r, g, b, a = rgba[i : i + 4]
            j = (dst_y * w + x) * 4
            xor[j : j + 4] = bytes((b, g, r, a))

    and_row = ((w + 31) // 32) * 4
    mask = bytearray(and_row * h)
    for y in range(h):
        dst_y = h - 1 - y
        for x in range(w):
            a = rgba[(y * w + x) * 4 + 3]
            if a == 0:
                mask[dst_y * and_row + (x // 8)] |= 0x80 >> (x % 8)

    header = struct.pack(
        "<IIIHHIIIIII",
        40,       # biSize
        w,
        h * 2,    # XOR + AND, per the ICO convention
        1,        # biPlanes
        32,       # biBitCount
        0,        # BI_RGB
        len(xor) + len(mask),
        0,
        0,
        0,
        0,
    )
    return header + xor + bytes(mask)


def write_ico(path: Path, frames: list[tuple[int, bytes]]) -> None:
    """frames: list of (size, image-bytes) in the order they should appear."""
    count = len(frames)
    offset = 6 + 16 * count
    entries = []
    blobs = []
    for size, data in frames:
        width_b = 0 if size >= 256 else size
        height_b = 0 if size >= 256 else size
        entries.append(
            struct.pack(
                "<BBBBHHII",
                width_b,
                height_b,
                0,  # color count
                0,  # reserved
                1,  # planes
                32,  # bit count
                len(data),
                offset,
            )
        )
        blobs.append(data)
        offset += len(data)

    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("wb") as fp:
        fp.write(struct.pack("<HHH", 0, 1, count))  # reserved, type=icon, count
        for entry in entries:
            fp.write(entry)
        for blob in blobs:
            fp.write(blob)


def verify_ico(path: Path) -> None:
    data = path.read_bytes()
    reserved, typ, count = struct.unpack_from("<HHH", data, 0)
    if reserved != 0 or typ != 1:
        raise RuntimeError(f"{path} is not a valid ICO")
    if count != len(SIZES):
        raise RuntimeError(f"{path} has {count} frames, expected {len(SIZES)}")
    seen = []
    for i in range(count):
        width, height, _c, _r, _p, bits, nbytes, off = struct.unpack_from(
            "<BBBBHHII", data, 6 + 16 * i
        )
        size = width or 256
        seen.append(size)
        blob = data[off : off + nbytes]
        if size == PNG_SIZE:
            if not blob.startswith(b"\x89PNG\r\n\x1a\n"):
                raise RuntimeError(f"{size}px frame is not PNG-compressed")
        else:
            if blob.startswith(b"\x89PNG"):
                raise RuntimeError(f"{size}px frame should be a BMP DIB, not PNG")
            bi_size, bi_w, bi_h, planes, bit_count = struct.unpack_from("<IIIHH", blob, 0)
            if bi_size != 40 or bi_w != size or bi_h != size * 2 or bit_count != 32:
                raise RuntimeError(
                    f"{size}px BMP header looks wrong: "
                    f"biSize={bi_size} {bi_w}x{bi_h} {bit_count}bpp"
                )
    if tuple(seen) != SIZES:
        raise RuntimeError(f"ICO sizes are {seen}, expected {list(SIZES)}")
    # Round-trip through Pillow so we know each frame decodes.
    with Image.open(path) as ico:
        loaded = []
        for sz in SIZES:
            ico.size = (sz, sz)
            frame = ico.copy().convert("RGBA")
            loaded.append(frame.size)
            if frame.size != (sz, sz):
                raise RuntimeError(f"Pillow decoded {sz} as {frame.size}")
    print(
        f"wrote {path} ({path.stat().st_size} bytes): "
        f"{', '.join(str(s) for s in seen)}; {PNG_SIZE} is PNG, others are 32-bit BMP"
    )


def build(svg: Path, ico: Path) -> None:
    if not svg.is_file():
        sys.exit(f"missing SVG: {svg}")
    frames: list[tuple[int, bytes]] = []
    for size in SIZES:
        png_bytes = render_png(svg, size)
        im = Image.open(io.BytesIO(png_bytes)).convert("RGBA")
        if im.size != (size, size):
            im = im.resize((size, size), Image.Resampling.LANCZOS)
        if size == PNG_SIZE:
            frames.append((size, png_bytes if png_bytes.startswith(b"\x89PNG") else _png_bytes(im)))
        else:
            frames.append((size, ico_bmp_with_alpha(im)))
    write_ico(ico, frames)
    verify_ico(ico)


def _png_bytes(im: Image.Image) -> bytes:
    buf = io.BytesIO()
    im.save(buf, format="PNG")
    return buf.getvalue()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--svg", type=Path, default=DEFAULT_SVG)
    parser.add_argument("--ico", type=Path, default=DEFAULT_ICO)
    args = parser.parse_args()
    build(args.svg.resolve(), args.ico.resolve())


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""Build ScribeVG/Data/audio.svpak from Emi Asano's {sentenceId}.m4a clips.

The pack ships inside the app (ClickOnce and Microsoft Store MSIX) instead of
being downloaded. It is kept out of git: the recordings are copyright
Tokibits/PPDAC, and the repository is public.

Format (little-endian):
    magic   4 bytes  b"SVPK"
    version u32      1
    count   u32
    count x (id i32, offset i64, length i32), sorted by id; offset from file start
    clip data, each clip XORed with a 32-byte key = SHA-256("ScribeVG audio pack v1:" + id)

The XOR only stops the clips from being copied out as plain files; it is not
encryption.

Usage:
    python3 tools/build_audio_pack.py <folder-or-.tgz> [ScribeVG/Data/audio.svpak]
"""
import hashlib
import io
import os
import re
import struct
import sys
import tarfile

CLIP = re.compile(r"(?:^|/)(\d+)\.m4a$")


def key_for(clip_id: int) -> bytes:
    return hashlib.sha256(f"ScribeVG audio pack v1:{clip_id}".encode()).digest()


def scramble(data: bytes, clip_id: int) -> bytes:
    key = key_for(clip_id)
    reps = key * (len(data) // 32 + 1)
    return bytes(a ^ b for a, b in zip(data, reps))


def read_clips(source: str) -> dict[int, bytes]:
    clips: dict[int, bytes] = {}
    if os.path.isdir(source):
        for name in os.listdir(source):
            m = CLIP.search(name)
            if m:
                with open(os.path.join(source, name), "rb") as f:
                    clips[int(m.group(1))] = f.read()
    else:
        with tarfile.open(source) as tar:
            for member in tar.getmembers():
                m = CLIP.search(member.name)
                if m and member.isfile():
                    clips[int(m.group(1))] = tar.extractfile(member).read()
    return clips


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    source = sys.argv[1]
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(
        os.path.dirname(__file__), "..", "ScribeVG", "Data", "audio.svpak")
    clips = {k: v for k, v in read_clips(source).items() if len(v) >= 1024}
    ids = sorted(clips)
    header = 12 + 16 * len(ids)
    index = io.BytesIO()
    body = io.BytesIO()
    offset = header
    for clip_id in ids:
        data = clips[clip_id]
        index.write(struct.pack("<iqi", clip_id, offset, len(data)))
        body.write(scramble(data, clip_id))
        offset += len(data)
    tmp = out + ".tmp"
    with open(tmp, "wb") as f:
        f.write(b"SVPK" + struct.pack("<II", 1, len(ids)))
        f.write(index.getvalue())
        f.write(body.getvalue())
    os.replace(tmp, out)
    print(f"Wrote {len(ids)} clips, {offset / 1e6:.1f} MB to {os.path.normpath(out)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Batch resize character PNGs to 768x768, rename by role.

Assumes source PNGs already have transparent backgrounds. Pipeline per file:
    1. crop to content bbox
    2. scale by longer side, keep aspect ratio
    3. paste centered horizontally, feet anchored to bottom margin
    4. save as PNG-32, delete original if renamed
"""

from pathlib import Path

from resize_to_square import crop_to_content, fit_to_square
from PIL import Image


# color / role token in filename -> canonical role name
RENAME_MAP = {
    "green":  "player_p1_768",
    "red":    "player_p2_768",
    "blue":   "player_p3_768",
    "orange": "player_p4_768",
    "ghost":  "ghost_768",
    "boss":   "boss_768",
}

SIZE = 768
BOTTOM_MARGIN = 24


def process_one(src: Path, dst: Path) -> None:
    img = Image.open(src).convert("RGBA")
    img = crop_to_content(img)
    out = fit_to_square(img, size=SIZE, bottom_margin=BOTTOM_MARGIN)
    out.save(dst, format="PNG")


def main() -> None:
    root = Path("/Users/xuanenyun/tencent/minigame/output/characters")
    for src in sorted(root.glob("*.png")):
        stem = src.stem.lower()
        if stem not in RENAME_MAP:
            print(f"[skip] {src.name} (no mapping)")
            continue
        dst = root / f"{RENAME_MAP[stem]}.png"
        process_one(src, dst)
        if src.resolve() != dst.resolve():
            src.unlink()
        print(f"[ok] {src.name} -> {dst.name}")


if __name__ == "__main__":
    main()

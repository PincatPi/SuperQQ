#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Batch resize run-frame PNGs to 768x768 (transparent canvas, no bg removal).
Feet anchored at bottom with a small margin, horizontally centered.
"""

from pathlib import Path
from PIL import Image

SRC_DIR = Path("/Users/xuanenyun/tencent/minigame/output/frames/run")
OUT_DIR = SRC_DIR / "run_768"

SIZE = 768
BOTTOM_MARGIN = 24


def crop_to_content(img: Image.Image) -> Image.Image:
    bbox = img.getbbox()
    return img.crop(bbox) if bbox else img


def fit_to_square(img: Image.Image, size: int, bottom_margin: int) -> Image.Image:
    src_w, src_h = img.size
    max_h = size - bottom_margin
    scale = min(size / src_w, max_h / src_h)
    new_w = max(1, int(round(src_w * scale)))
    new_h = max(1, int(round(src_h * scale)))
    resized = img.resize((new_w, new_h), Image.LANCZOS)

    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    off_x = (size - new_w) // 2
    off_y = size - bottom_margin - new_h
    canvas.paste(resized, (off_x, off_y), resized)
    return canvas


def main() -> None:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    files = sorted(SRC_DIR.glob("*.png"))
    for src in files:
        # skip files that are already in the output subdir
        if OUT_DIR in src.parents:
            continue
        img = Image.open(src).convert("RGBA")
        img = crop_to_content(img)
        out_img = fit_to_square(img, SIZE, BOTTOM_MARGIN)
        dst = OUT_DIR / src.name
        out_img.save(dst, format="PNG")
        print(f"[ok] {src.name} -> {dst.relative_to(SRC_DIR)}")


if __name__ == "__main__":
    main()

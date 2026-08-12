#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Resize a character PNG to a square canvas with a transparent background.

Pipeline:
1. Load source PNG (RGB or RGBA).
2. Remove near-white background -> alpha channel.
3. Crop to the tight alpha bbox.
4. Fit into a square canvas by longer side, keep aspect ratio.
5. Anchor feet to bottom (leave a small bottom margin) and center horizontally.
6. Export PNG-32.

Usage:
    python resize_to_square.py <src> <dst> [--size 768] [--bottom-margin 24] [--white-thr 240]
"""

import argparse
from pathlib import Path

from PIL import Image


def remove_white_bg(img: Image.Image, threshold: int = 240) -> Image.Image:
    """Turn near-white pixels transparent. Keeps existing alpha if any."""
    img = img.convert("RGBA")
    px = img.load()
    w, h = img.size
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if r >= threshold and g >= threshold and b >= threshold:
                px[x, y] = (r, g, b, 0)
    return img


def crop_to_content(img: Image.Image) -> Image.Image:
    """Crop transparent margins around the subject."""
    bbox = img.getbbox()
    if bbox is None:
        return img
    return img.crop(bbox)


def fit_to_square(
    img: Image.Image,
    size: int = 768,
    bottom_margin: int = 24,
) -> Image.Image:
    """Scale by the longer side, then paste centered horizontally / anchored to bottom."""
    src_w, src_h = img.size
    max_h = size - bottom_margin  # reserve bottom margin for the ground line
    scale = min(size / src_w, max_h / src_h)
    new_w = max(1, int(round(src_w * scale)))
    new_h = max(1, int(round(src_h * scale)))
    resized = img.resize((new_w, new_h), Image.LANCZOS)

    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    off_x = (size - new_w) // 2
    off_y = size - bottom_margin - new_h  # feet aligned to bottom margin
    canvas.paste(resized, (off_x, off_y), resized)
    return canvas


def main() -> None:
    parser = argparse.ArgumentParser(description="Resize character PNG to square with transparent bg.")
    parser.add_argument("src", type=Path, help="source PNG path")
    parser.add_argument("dst", type=Path, help="output PNG path")
    parser.add_argument("--size", type=int, default=768, help="target square side, default 768")
    parser.add_argument("--bottom-margin", type=int, default=24, help="pixels reserved below feet")
    parser.add_argument("--white-thr", type=int, default=240, help="white threshold for bg removal")
    args = parser.parse_args()

    img = Image.open(args.src)
    # img = remove_white_bg(img, threshold=args.white_thr)
    img = crop_to_content(img)
    out = fit_to_square(img, size=args.size, bottom_margin=args.bottom_margin)

    args.dst.parent.mkdir(parents=True, exist_ok=True)
    out.save(args.dst, format="PNG")
    print(f"[ok] {args.src} -> {args.dst} ({args.size}x{args.size}, PNG-32)")


if __name__ == "__main__":
    main()

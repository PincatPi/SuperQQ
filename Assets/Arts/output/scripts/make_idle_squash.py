#!/usr/bin/env python3
"""
Generate 4-frame idle loop by vertical squash.

Frame rule (per user):
    01 = base (original)
    02 = squash level 1 (compressed down by 1 step)
    03 = squash level 2 (max compression, lowest point)
    04 = squash level 1 (== 02, rebound mid)

Anchor: feet bottom Y is locked. Only vertical scale changes.
"""

from pathlib import Path
from PIL import Image

# --- config ---
SRC_DIR = Path("/Users/xuanenyun/tencent/minigame/output/characters")
OUT_DIR = Path("/Users/xuanenyun/tencent/minigame/output/frames")

# input file -> output prefix
TARGETS = {
    "player_p1_768.png": "player_p1_idle",
    "player_p2_768.png": "player_p2_idle",
    "player_p3_768.png": "player_p3_idle",
    "player_p4_768.png": "player_p4_idle",
    "ghost_768.png":     "ghost_idle",
}

# squash levels in pixels (height reduction from top; feet locked at bottom)
# frame 01: 0px, frame 02: 8px, frame 03: 16px, frame 04: 8px
SQUASH_STEPS = [0, 8, 16, 8]


def squash_vertical(img: Image.Image, dy: int) -> Image.Image:
    """
    Vertically compress the entire image by dy pixels, feet anchored at bottom.
    Width unchanged. Top area filled transparent.
    """
    if dy <= 0:
        return img.copy()

    w, h = img.size
    new_h = h - dy
    # resize down vertically only
    squeezed = img.resize((w, new_h), Image.LANCZOS)

    # paste onto a fresh transparent canvas, bottom-aligned
    canvas = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    canvas.paste(squeezed, (0, dy), squeezed)
    return canvas


def process_one(src: Path, out_prefix: str):
    if not src.exists():
        print(f"[skip] {src.name} not found")
        return

    img = Image.open(src).convert("RGBA")
    for i, dy in enumerate(SQUASH_STEPS, start=1):
        out = OUT_DIR / f"{out_prefix}_{i:02d}.png"
        frame = squash_vertical(img, dy)
        frame.save(out, "PNG")
        print(f"  -> {out.name}  (squash {dy}px)")


def main():
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    for fname, prefix in TARGETS.items():
        print(f"[{fname}]")
        process_one(SRC_DIR / fname, prefix)
    print("done.")


if __name__ == "__main__":
    main()

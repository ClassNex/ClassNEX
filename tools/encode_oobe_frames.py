"""Encode timestamped, window-only Windows Graphics Capture samples from an OOBE test."""
import json
from pathlib import Path
import sys

from PIL import Image, ImageChops


folder = Path(sys.argv[1]).resolve(strict=True)
files = sorted((folder / "window-frames").glob("*.png"))
stamps = [int(f.stem) for f in files]
images = []
for f in files:
    with Image.open(f) as frame:
        images.append(frame.convert("RGB"))
if len(images) < 8 or not any(ImageChops.difference(images[0], f).getbbox() for f in images[1:]):
    raise RuntimeError(f"No usable window animation in {len(images)} samples")
durations = [b - a for a, b in zip(stamps, stamps[1:])] + [65]
images[0].save(folder / "oobe-window.gif", save_all=True, append_images=images[1:], duration=durations, loop=0)
(folder / "recording.json").write_text(json.dumps({"samples": len(images), "observedMilliseconds": stamps[-1] - stamps[0],
    "source": "Windows Graphics Capture on the isolated wizard HWND. Timestamps are sample times, not display FPS."}), encoding="utf-8")

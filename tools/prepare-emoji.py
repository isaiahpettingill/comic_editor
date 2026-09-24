"""Run with `uv run --with fonttools tools/prepare-emoji.py`.

Keep all glyphs and COLR/CPAL color data; remove only the duplicate SVG drawings,
which Avalonia's Skia renderer doesn't use. Record source and output hashes.
"""

import hashlib
import io
import json
from pathlib import Path
import urllib.request

from fontTools.ttLib import TTFont

root = Path(__file__).resolve().parent.parent
url = "https://raw.githubusercontent.com/google/fonts/main/ofl/notocoloremoji/"
data = urllib.request.urlopen(url + "NotoColorEmoji-Regular.ttf", timeout=60).read()
font = TTFont(io.BytesIO(data), recalcTimestamp=False)
if "SVG " in font:
    del font["SVG "]
output = root / "ComicEditor/Assets/Fonts/NotoColorEmoji-Regular.ttf"
font.save(output)
manifest = root / "ComicEditor/Assets/Fonts/sources.json"
items = json.loads(manifest.read_text())
entry = next(x for x in items if x["file"].endswith("NotoColorEmoji-Regular.ttf"))
entry.update(
    source_sha256=hashlib.sha256(data).hexdigest(),
    sha256=hashlib.sha256(output.read_bytes()).hexdigest(),
    bytes=output.stat().st_size,
    modification="Removed duplicate SVG table; retained complete COLR/CPAL glyphs.",
)
manifest.write_text(json.dumps(items, indent=2) + "\n")
print(f"Noto Color Emoji: {len(data)} -> {output.stat().st_size} bytes")

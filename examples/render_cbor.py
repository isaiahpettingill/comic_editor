"""Render one CBOR comic frame: uv run examples/render_cbor.py story.cbor es 0 frame.png"""
# /// script
# requires-python = ">=3.11"
# dependencies = ["cbor2>=5.6", "Pillow>=10"]
# ///
import argparse
import io
from pathlib import Path

import cbor2
from PIL import Image


def render(data: bytes, language: str, frame_index: int = 0) -> Image.Image:
    stream = io.BytesIO(data)
    comic = cbor2.load(stream)
    if stream.read(1) or comic["version"] != 1:
        raise ValueError("Unsupported CBOR comic")
    width, height = comic["width"], comic["height"]
    if not (0 < width <= 16384 and 0 < height <= 16384 and width * height <= 64_000_000):
        raise ValueError("Canvas exceeds this example's limits")
    if comic["fallback_language"] not in comic["languages"]:
        raise ValueError("Invalid fallback language")
    if language not in comic["languages"]:
        language = comic["fallback_language"]
    if not 0 <= frame_index < len(comic["frames"]):
        raise ValueError("Frame index out of range")

    def png(data):
        with Image.open(io.BytesIO(data)) as image:
            if image.format != "PNG":
                raise ValueError("Expected PNG")
            return image.convert("RGBA")

    base = png(comic["frames"][frame_index])
    if base.size != (width, height):
        raise ValueError("Artwork dimensions do not match canvas")
    canvas = Image.new("RGBA", (width, height), "white")
    canvas.alpha_composite(base)
    for text in comic["text"]:
        if text["frame"] != frame_index or text["language"] != language:
            continue
        overlay = png(text["png"])
        x, y = text["x"], text["y"]
        if x < 0 or y < 0 or x + overlay.width > width or y + overlay.height > height:
            raise ValueError("Text lies outside canvas")
        canvas.alpha_composite(overlay, (x, y))
    return canvas


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Render a comic in the selected language (unknown tags use fallback).")
    parser.add_argument("input", type=Path)
    parser.add_argument("language")
    parser.add_argument("frame", type=int, help="Zero-based frame index")
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    render(args.input.read_bytes(), args.language, args.frame).save(args.output)

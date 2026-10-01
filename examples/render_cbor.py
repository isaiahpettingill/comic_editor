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


def load(data: bytes) -> dict:
    stream = io.BytesIO(data)
    comic = cbor2.load(stream)
    if stream.read(1) or comic["version"] not in (1, 2):
        raise ValueError("Unsupported CBOR comic")
    width, height = comic["width"], comic["height"]
    if not (0 < width <= 16384 and 0 < height <= 16384 and width * height <= 64_000_000):
        raise ValueError("Canvas exceeds this example's limits")
    if comic["fallback_language"] not in comic["languages"]:
        raise ValueError("Invalid fallback language")
    if comic["version"] == 1:
        comic["vars"] = []
        comic["frames"] = [dict(png=png, duration_ms=1000, req="always") for png in comic["frames"]]
    names = comic["vars"]
    if not isinstance(names, list) or len(set(names)) != len(names) or any(not valid_variable(name) for name in names):
        raise ValueError("Invalid variable declarations")
    for frame in comic["frames"]:
        req = frame["req"]
        variable = req[4:] if req.startswith("not ") else req
        if req not in ("always", "never") and variable not in names:
            raise ValueError("Undeclared frame variable")
        if type(frame["duration_ms"]) is not int or not 1 <= frame["duration_ms"] <= 3_600_000:
            raise ValueError("Invalid frame duration")
    return comic


def valid_variable(name: str) -> bool:
    import re
    return isinstance(name, str) and name not in ("always", "never", "not") and re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]{0,63}", name) is not None


def selected_frames(comic: dict, variables=()) -> list[int]:
    defined = set(variables)
    if not defined <= set(comic["vars"]):
        raise ValueError("Unknown playback variable")
    def matches(req):
        if req == "always": return True
        if req == "never": return False
        if req.startswith("not "): return req[4:] not in defined
        return req in defined
    return [index for index, frame in enumerate(comic["frames"]) if matches(frame["req"])]


def render(data: bytes, language: str, frame_index: int = 0) -> Image.Image:
    return render_frame(load(data), language, frame_index)


def render_frame(comic: dict, language: str, frame_index: int) -> Image.Image:
    width, height = comic["width"], comic["height"]
    if language not in comic["languages"]:
        language = comic["fallback_language"]
    if not 0 <= frame_index < len(comic["frames"]):
        raise ValueError("Frame index out of range")

    def png(data):
        with Image.open(io.BytesIO(data)) as image:
            if image.format != "PNG":
                raise ValueError("Expected PNG")
            return image.convert("RGBA")

    base = png(comic["frames"][frame_index]["png"])
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


def play(comic: dict, language: str, variables=()):
    """One-shot Tk viewer: skip excluded frames and display each remaining frame for duration_ms."""
    import tkinter as tk
    from PIL import ImageTk
    frames = selected_frames(comic, variables)
    if not frames:
        raise ValueError("No frames match the selected variables")
    window = tk.Tk()
    window.title("Comic playback")
    label = tk.Label(window)
    label.pack()
    def show(position):
        if position == len(frames):
            window.title("Comic playback — finished")
            return
        index = frames[position]
        image = ImageTk.PhotoImage(render_frame(comic, language, index))
        label.configure(image=image)
        label.image = image  # Keep the Tk image alive.
        window.after(comic["frames"][index]["duration_ms"], show, position + 1)
    show(0)
    window.mainloop()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Render a comic in the selected language (unknown tags use fallback).")
    parser.add_argument("input", type=Path)
    parser.add_argument("language")
    parser.add_argument("frame", type=int, nargs="?", default=0, help="Zero-based frame index")
    parser.add_argument("output", type=Path, nargs="?")
    parser.add_argument("--var", action="append", default=[], help="Set a case-sensitive playback variable; repeat for multiple variables")
    parser.add_argument("--play", action="store_true", help="Open timed playback (requires Tk)")
    args = parser.parse_args()
    comic = load(args.input.read_bytes())
    if args.play:
        play(comic, args.language, args.var)
    elif args.output is not None:
        render_frame(comic, args.language, args.frame).save(args.output)
    else:
        parser.error("Provide an output PNG path, or use --play")

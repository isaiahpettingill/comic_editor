"""Render the Papirus-style SVG into application icons.

Requires cairosvg: python -m pip install cairosvg
"""

from pathlib import Path
import struct

import cairosvg


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "ComicEditor/Assets/comic-editor.svg"
SIZES = (16, 22, 24, 32, 48, 64, 128, 256)


def render(size: int) -> bytes:
    source = SOURCE.with_name("comic-editor-small.svg") if size <= 24 else SOURCE
    return cairosvg.svg2png(url=str(source), output_width=size, output_height=size)


def write_ico(path: Path) -> None:
    images = [render(size) for size in SIZES]
    offset = 6 + 16 * len(images)
    with path.open("wb") as stream:
        stream.write(struct.pack("<HHH", 0, 1, len(images)))
        for size, data in zip(SIZES, images):
            dimension = 0 if size == 256 else size
            stream.write(struct.pack("<BBBBHHII", dimension, dimension, 0, 0, 1, 32,
                                     len(data), offset))
            offset += len(data)
        for data in images:
            stream.write(data)


if __name__ == "__main__":
    (ROOT / "ComicEditor/Assets/comic-editor.png").write_bytes(render(256))
    (ROOT / "ComicEditor.Android/ComicIcon.png").write_bytes(render(512))
    write_ico(ROOT / "ComicEditor/Assets/comic-editor.ico")

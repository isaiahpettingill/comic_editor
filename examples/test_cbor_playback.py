"""Run with: uv run --with cbor2 --with pillow python examples/test_cbor_playback.py"""
import io
import unittest

import cbor2
from PIL import Image
from render_cbor import load, render, selected_frames


def png(color):
    output = io.BytesIO()
    Image.new("RGBA", (2, 1), color).save(output, format="PNG")
    return output.getvalue()


class PlaybackTests(unittest.TestCase):
    def test_v2_selection_keeps_original_indices_and_localized_text(self):
        comic = dict(version=2, width=2, height=1, languages=["en", "es"], fallback_language="en", vars=["A", "B"],
                     frames=[dict(png=png((255, 0, 0, 128)), duration_ms=80, req=req)
                             for req in ("never", "A", "not A", "always", "B")],
                     text=[dict(frame=1, language="es", x=0, y=0, png=png((0, 0, 255, 255)))])
        data = cbor2.dumps(comic)
        self.assertEqual([1, 3], selected_frames(load(data), ["A"]))
        self.assertEqual([2, 3], selected_frames(load(data)))
        self.assertEqual([1, 3, 4], selected_frames(load(data), ["A", "B"]))
        self.assertEqual((0, 0, 255, 255), render(data, "es", 1).getpixel((0, 0)))
        self.assertEqual((255, 127, 127, 255), render(data, "unknown", 1).getpixel((0, 0)))
        with self.assertRaises(ValueError): selected_frames(load(data), ["a"])
        comic["frames"][0]["duration_ms"] = 0
        with self.assertRaises(ValueError): load(cbor2.dumps(comic))

    def test_legacy_defaults(self):
        data = cbor2.dumps(dict(version=1, width=2, height=1, languages=["en"], fallback_language="en",
                               frames=[png((0, 0, 0, 0))], text=[]))
        comic = load(data)
        self.assertEqual([0], selected_frames(comic))
        self.assertEqual(1000, comic["frames"][0]["duration_ms"])
        self.assertEqual("always", comic["frames"][0]["req"])
        self.assertEqual((255, 255, 255, 255), render(data, "en").getpixel((0, 0)))


if __name__ == "__main__":
    unittest.main()

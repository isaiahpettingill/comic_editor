# Compressed indexed sources and color mode

Editable indexed documents now write source **version 6**, with an 8-bit indexed
PNG in each layer's new protobuf field 7 (`indexed_png`). The editable model
remains version 3. Existing raw indexed sources (versions 1–3), RGBA PNG sources
(version 4), and RGBA QOI sources (version 5) remain readable. RGBA still writes
version 5/QOI. Saving an existing indexed file upgrades its source encoding.

**Compatibility:** ComicEditor/compiler v0.1.34 and earlier reject source v6.
Keep an original backup for older editors, and upgrade the official
`comic-compile` executable alongside ComicEditor. Destructos delegates editable
source imports to that executable: select **comic-compile v0.1.35 or later**
from the matching editor release. Its older “v0.1.32+” guidance is not sufficient for v6.
No Godot PNG/QOI extension or runtime codec change is required.
Compiled CBOR remains v2 and compiled indexed protobuf remains v2; tests compare
the complete compiled bytes from equivalent v3/v6 sources. Native compiler
tests run on all desktop release platforms.

## Exact editing semantics

Pixels retain their original palette slots, including different slots with
identical RGB colors. Slot 255 remains transparent. No palette reduction,
quantization, compositing, layer deduplication, or artwork conversion occurs.
Hidden layers, metadata, translations, text, requirements, timing and QOA audio
are retained. The source PNG profile uses a fixed 256-entry PLTE and tRNS;
unused palette slots are padding, not extra editable colors. It is deliberately
not a general-purpose PNG importer.

The reader bounds dimensions to 2048 per axis, validates chunks and CRC,
checks the exact project palette/transparency, supports all five row filters,
bounds decoded length and rejects out-of-range indices and mixed payloads.
Compression uses .NET zlib Optimal, not maximum compression. Fixed PNG palette
metadata adds roughly 1 KiB per layer, so extremely tiny images can grow.

## Default and conversion

Indexed remains the fresh-install default: for palette artwork it stores one
channel and is substantially smaller than RGBA QOI. Existing preferences and
document modes are preserved. QOI can load faster on some content; there is no
universal size-and-speed winner.

The visible **Color mode** control beside the palette, or **Canvas → Color
mode…**, explains the tradeoff and offers explicit indexed→RGBA conversion.
Conversion preserves appearance, all frames and audio, and supports Undo/Redo.
RGBA allows per-pixel alpha/unrestricted pixel colors. Automatic reverse
quantization is intentionally not offered because it could silently discard
colors or partial transparency; Undo restores the exact indexed document.

## Reproduce benchmarks

```sh
DOTNET_TieredCompilation=0 dotnet run --project tools/IndexedSourceBenchmark -c Release
# Optional local file arguments are read only and never copied into the repo:
DOTNET_TieredCompilation=0 dotnet run --project tools/IndexedSourceBenchmark -c Release -- /path/to/input.ctsc
```

Linux .NET 11 Release, 1080×720, one layer, deterministic seed42, two warmups and
seven samples (median), tiered compilation disabled to reduce JIT phase noise. Save includes snapshot and serialization; load includes
parse, decoding, buffer reconstruction and validation. Disk, UI, font work and
recovery writes are not included. These are not Windows UI measurements.

| Content | Raw v3 bytes | Indexed PNG bytes | RGBA QOI bytes | PNG save / load ms | RGBA save / load ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| Blank | 778,239 | 3,927 | 13,337 | 0.94 / 9.85 | 2.24 / 9.05 |
| Sparse diagonal | 778,239 | 5,882 | 15,182 | 1.37 / 9.60 | 2.15 / 8.60 |
| Color blocks | 778,239 | 9,933 | 122,370 | 1.74 / 11.68 | 3.16 / 10.90 |
| Random palette noise | 778,239 | 684,492 | 2,145,827 | 19.43 / 15.71 | 12.89 / 18.60 |

An exact-index QOI alternative was also measured: encoding each slot as opaque
grayscale preserves duplicate palette entries, unlike ordinary palette-to-RGBA
conversion. Its image payloads were 12,565 / 14,410 / 64,824 / 1,599,629 bytes,
respectively. Indexed PNG wins size in all four fixtures, without an artificial
index-as-color convention. Timings vary with tiered JIT, hardware and content;
the benchmark is intended to make this tradeoff reproducible, not promise
universal performance.

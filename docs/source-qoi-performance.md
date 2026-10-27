# QOI source artwork, PNG game assets

This change is **source-only**. Editable RGBA `.ctsc` documents write wire version 5
with QOI in layer field 6 (`rgba_qoi`). Indexed documents still write version 3
with palette-index bytes. Source versions 1–4 remain readable; old RGBA PNG
version 4 files are upgraded when saved. The editable model stays version 4.
ComicEditor/compiler v0.1.33 and earlier reject new source version 5: retain a
backup when sharing with older editors. This feature needs the compiler built
from this change or a later release; it is not present in v0.1.33.

**Compiled CBOR remains version 2 with PNG images**, and compiled protobuf stays
at its existing versions (RGBA version 3/PNG). No QOI support or native extension
is needed in Destructos. Its `.ctsc` importer must use the updated official
`comic-compile` when the source is version 5. Normal PNG exports, printing, and
image import are unchanged. Fonts and QOA audio follow the existing rules.

The codec is the existing MIT [QoiFu](../third_party/QoiFu/README.md), pinned and
vendored without algorithm modifications. A bounded adapter validates headers,
dimensions, complete chunks, run lengths, and exact end markers before decoding
allocations. Tests use independent official QOI C-reference fixtures, including
transparent pixels with nonzero RGB.

## Reproduce

Run `tools/benchmark-source.sh` with the repository SDK. It compares the complete
source model and PNG serializer at released commit `671b9b4` with the current
QOI serializer, using identical source documents. Both use the same sparse
copy-on-write pixel buffers. No arbitrary user files are read or modified.

Measurements are Release .NET 11 on Linux, 720×480 RGBA, one artwork layer per
frame, deterministic seed 42, two warmups and five measured iterations. Save
includes snapshot, validation, full protobuf serialization, SHA-256 of new and
existing bytes, temporary-file write and replacement; load includes file read,
protobuf parse, image decode, sparse-buffer reconstruction and validation.
The temporary local filesystem/page cache is included; UI scheduling, storage
pickers, browser storage, recovery writes, and network drives are not. These
are not Windows UI latency measurements. Saves intentionally represent a new
revision, not the unchanged-Ctrl+S fast path. Round-trip pixels are checked.

### Twelve-frame results

| Content | Save PNG → QOI (ms) | Load PNG → QOI (ms) | Source bytes PNG → QOI |
| --- | ---: | ---: | ---: |
| Blank | 19.52 → 12.13 | 67.09 → 63.06 | 158,053 → 69,037 |
| Sparse red line | 19.48 → 9.88 | 45.95 → 29.80 | 158,161 → 69,121 |
| Repeated-color pixel art | 46.49 → 16.15 | 75.99 → 46.94 | 699,169 → 538,609 |
| Smooth gradient | 343.37 → 29.02 | 392.05 → 76.21 | 6,722,905 → 2,945,569 |
| Dense random RGBA | 932.25 → 141.00 | 927.50 → 125.63 | 17,504,449 → 20,721,145 |

These fixtures are synthetic representatives, not a claim about every image.
QOI made these saves/loads faster, but dense noise grew **18%**. Blank/sparse
codec work was already cheap; buffer reconstruction still dominates some loads.
The pinned library allocates a worst-case encoded buffer, and its packed-ARGB
API requires a channel adapter. Consequently memory is **not uniformly lower**:
12-frame sparse-save managed allocation rose 1.67 → 20.09 MiB; sparse-load
17.46 → 32.99 MiB. Dense-save allocation fell 167.57 → 98.88 MiB but dense-load
rose 82.39 → 107.38 MiB. Dimensions are capped at 2048 per axis; input conversion
uses a pooled array. This trades additional bounded allocation on simple images
for faster source compression, without changing runtime PNG compatibility.

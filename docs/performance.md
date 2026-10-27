> Historical v0.1.33 sparse-buffer measurements below. For the later source-only QOI change, see [source QOI performance](source-qoi-performance.md); its source wire bytes differ, while compiled assets remain PNG.

# Artwork and save performance

## Representation and compatibility

Artwork now uses sparse **32 × 32 packed tiles**: one byte per indexed pixel or
four bytes per RGBA pixel. A missing tile means transparent; clearing its last
nontransparent pixel releases it. The fixed tile-pointer grid avoids per-pixel
hash tables and still addresses dense images directly. RGBA zero-alpha pixels
with nonzero RGB are retained exactly, not discarded as visually transparent.

Undo, shape previews, and background-save snapshots share immutable tiles.
The first write to a shared tile copies only that tile. Each snapshot owns its
pointer grid. Snapshot capture occurs on the UI thread before the worker is
started; workers only read. The editor never mutates a tile shared with a worker
or undo snapshot. No mutable buffer is exposed to callers.

The `Rows` hex-string adapter remains for compatibility/cold paths, but painting,
rendering, snapshots, validation, and protobuf read/write use packed pixels.
The `.ctsc` protobuf schema, indexed bytes, RGBA PNGs, and QOA data are unchanged.
Tests compare exact output bytes and exercise sparse/dense tile boundaries,
legacy row replacement, independent mutation of both snapshot owners, erase,
and restoration. Existing format fixtures continue to pass.

## Reproduce

From a checkout with the SDK in `global.json`:

```sh
bash tools/benchmark-artwork.sh
```

The optional first argument selects a historical baseline commit; by default it
is `d8fe2115e1d264ca96052d61e1d0d56742ba8b5b`. The script compiles that commit's
**complete format model and codec**, under a separate namespace, against the
current implementation. It does not edit the checkout. No historical code is
executed beyond the named local model/codec benchmark. It uses Debug builds for
both implementations, one warmup, three encoding/stroke samples, and 50 snapshot
samples. Timing is elapsed wall time; allocation is per-thread managed bytes.
It is a microbenchmark, not a measured end-to-end Windows UI latency guarantee.

Fixtures: 720 × 480 RGBA, one layer, 1 or 12 frames. Sparse art is a horizontal
720-pixel line; dense art is seeded random RGBA (`Random(42)`). The current model
is loaded from the baseline's serialized bytes to preserve IDs and exact content.
Every fixture verifies byte-for-byte identical `.ctsc` output before measuring.
The stroke case changes 1,000 pixels in already allocated tiles on every sample.

## Local results (2026-10-26)

.NET `11.0.100-rc.1.26425.128`, Linux cloud executor, warmed Debug builds:

| Operation | Before | Packed buffers | Allocated before → after |
|---|---:|---:|---:|
| Sparse, 1 frame: encode | 34.5 ms | 3.2 ms | 14.7 MiB → 128 KiB |
| Sparse, 12 frames: encode | 252.5 ms | 33.3 ms | 175.5 MiB → 1.5 MiB |
| Dense, 1 frame: encode | 122.9 ms | 94.6 ms | 27.1 MiB → 12.6 MiB |
| Dense, 12 frames: encode | 1468.0 ms | 1196.1 ms | 324.9 MiB → 150.8 MiB |
| 1,000 changed pixels | 1.16 ms | 0.046 ms | 22.1 MiB → effectively zero |

Exact file sizes are unchanged: sparse 13,797 / 158,161 bytes and dense
1,459,275 / 17,503,897 bytes for 1 / 12 frames respectively. This is an internal
representation change, not a claim of a smaller on-disk format.

Snapshots were already shallow for immutable strings; they are **not claimed to
be faster**: 12-frame captures were about 0.04–0.09 ms in both versions, with
managed allocation reduced from roughly 62–70 KiB to 50–58 KiB. The benefit is
cheap copy-on-write editing after capture without retaining large hex strings.
Attached QOA arrays still copy for safe snapshot ownership.

## Save coordination

- Manual saves queue behind a current write instead of silently dropping another
  Ctrl+S. A repeated unchanged save verifies the external-file hash without
  rewriting the file or needlessly triggering a Godot import.
- Manual save, autosave, and recovery share a revision-bound immutable serialized
  capture. Recovery no longer keeps the file operation busy after its write ends.
- Active stroke/text captures bypass reuse and remain dirty, even when later edits
  belong to the same undo revision. A subsequent stable save can mark them clean.
- Save As reserves the document before opening the picker. Cancellation leaves
  its name, binding, and saved revision alone; repeated keys cannot stack pickers.
- External-change checks, atomic desktop replacement, recovery flushing, and
  awaiting project writes before closing remain in place.

Dense-image PNG compression and protobuf output still take meaningful time and
allocate output buffers. Full-frame encoder scratch buffers are pooled; renderer
scratch is only a scanline. Faster lossy storage or a changed `.ctsc` codec is not
introduced. Browser builds without worker threads can still spend CPU time on
serialization on their browser thread; these desktop measurements do not prove
browser responsiveness.

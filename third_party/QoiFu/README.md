# QoiFu managed QOI codec

`QOI.cs` is unchanged generated C# from the existing MIT-licensed QoiFu library:

- https://github.com/pfusik/qoi-fu
- Commit `2fab2661f2b72fe73643a8ed7be782bd1053f6a2` (2026-09-04)
- https://github.com/pfusik/qoi-fu/blob/2fab2661f2b72fe73643a8ed7be782bd1053f6a2/transpiled/QOI.cs
- SHA-256 `9ab2bce7f61299dcb141c4cdfcbcf5a306bea6fc5ca485f72a1de61262924e9f`
- Full MIT copyright/permission notice: `LICENSE`, copied from upstream `QOI.fu`.

The official QOI project lists QoiFu as an implementation. Its managed C# has no
native dependencies, reflection or runtime code generation. The wrapper adds
strict dimension, opcode-length, run, trailer and trailing-byte checks before
allocation, and converts packed RGBA to/from QoiFu's ARGB arrays. It preserves
RGB under zero alpha. Encoding uses channels=4, colorspace=0. The wrapper is not
a new codec implementation. The pinned upstream source remains unmodified.

QOI is used for editable source images only. Compiled runtime protobuf/CBOR
images remain PNG; this dependency does not change the game runtime contract.

QoiSharp 1.0.0 was evaluated but not selected: its last upstream commit predates
the final specification and its RGBA encoder allocates 4 bytes per pixel although
the format's worst-case encoding requires 5 bytes per pixel.

Specification/reference: https://github.com/phoboslab/qoi/blob/master/qoi.h
The independent official-reference fixtures have reproducible provenance under
`ComicEditor.Format.Tests/Fixtures/Qoi/`. NativeAOT/browser compatibility follows
from the managed implementation; platform builds still need ordinary CI testing.

The notice is also copied to `licenses/QoiFu-MIT.txt` for release packaging and
embedded in both format/app assemblies, with an About dialog license entry.

# Independent official-reference fixtures

Generated with the official MIT-licensed phoboslab/qoi C encoder and decoder,
commit `ffb2d2cb74a1de60819b21b939f7209aa53e91c1`, not the managed codec.
The raw `.rgba`/`.rgb` files contain straight-channel pixel bytes in raster order.
The 67×3 image contains repeated black pixels, invisible RGB, wrapping channel
differences, repeated colors, and arbitrary alpha. 3×1 tests RGB-only decoding.

Reproduce with the pinned upstream `qoi.h`:

```sh
cc -O2 -I /path/to/pinned/qoi tools/qoi-reference/reference.c -o /tmp/qoi-reference
/tmp/qoi-reference ComicEditor.Format.Tests/Fixtures/Qoi/official-rgba-67x3.rgba ComicEditor.Format.Tests/Fixtures/Qoi/official-rgba-67x3.qoi 67 3 4
/tmp/qoi-reference ComicEditor.Format.Tests/Fixtures/Qoi/official-rgb-3x1.rgb ComicEditor.Format.Tests/Fixtures/Qoi/official-rgb-3x1.qoi 3 1 3
```

The generator also verifies every decoded byte with the independent C decoder.
The normal managed test suite requires no native dependency.

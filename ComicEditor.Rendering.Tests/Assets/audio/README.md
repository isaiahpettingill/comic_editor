# Synthetic import fixture

`tone.flac` is a generated 440 Hz sine wave, 2,205 samples, mono, 22,050 Hz.
No recording or third-party media is included. Recreate with:

```sh
ffmpeg -f lavfi -i 'sine=frequency=440:sample_rate=22050:duration=0.1' -c:a flac tone.flac
```

Run the FFmpeg integration test with `COMIC_TEST_FFMPEG=1`; the native WAV/QOA,
missing-FFmpeg and converter-cancellation tests do not require FFmpeg.

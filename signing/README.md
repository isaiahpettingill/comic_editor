# Release signatures

Desktop editor and CLI executables, Windows setup, desktop archives, and the Linux installer have detached `.sig` files. Signatures are RSA-PSS with a 3072-bit key, SHA-256, MGF1-SHA-256, and a 32-byte salt. A signature is 384 raw bytes. The public key is `desktop-public.pem`; its SHA-256 fingerprint (DER SubjectPublicKeyInfo) is:

```text
4309c3b48dffefce875c66d99d0bfbc69ea38b59ad876dc59f49870a846a2a34
```

The private key lives in the repository's `DESKTOP_SIGNING_PRIVATE_KEY` Actions secret. It is never committed or included in an artifact. Trusted branch/tag builds require it; pull request builds are unsigned. Every signature is verified immediately after creation, and the release job verifies desktop packages again before publishing.

## Verify a download

Obtain the public key from a trusted checkout of this repository. Download both the package and its matching `.sig`, then run (OpenSSL 1.1.1 or newer):

```sh
openssl dgst -sha256 -verify signing/desktop-public.pem -signature ComicEditor-win-x64-setup.exe.sig -sigopt rsa_padding_mode:pss -sigopt rsa_pss_saltlen:32 ComicEditor-win-x64-setup.exe
```

Replace the filename to verify a Linux/macOS archive or an extracted executable. Alternatively, from the source checkout with Node installed:

```sh
node tools/release-signing.mjs verify ComicEditor-win-x64-setup.exe
```

The desktop updater embeds the public key and requires a valid package signature in addition to GitHub's checksum and size. Missing, truncated, modified, or wrong-key signatures stop the update before installation. Existing clients without this feature still use their original checksum verification for the first upgrade.

These are repository-controlled verification signatures. They do not provide a Windows Authenticode publisher identity or Apple Developer ID notarization, and do not remove OS publisher warnings. No certificate is installed into a user's trust store.

Keep this signing identity stable: changing only the secret will fail builds; changing the public key without a migration prevents existing clients from verifying new releases. A rotation requires a release that trusts the new key before packages begin using it.

## Android

Android already uses its own stable RSA-4096 keystore, held in the four `ANDROID_KEYSTORE_*` / `ANDROID_KEY_*` repository secrets. The APK is signed with Android's v2/v3 schemes. CI verifies the finished APK with `apksigner` and checks the certificate against `android-certificate.sha256`; changing the key would prevent existing installations from updating.

```sh
apksigner verify --verbose --print-certs ComicEditor-android-arm64.apk
```

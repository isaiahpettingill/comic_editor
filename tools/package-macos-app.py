"""Wrap the native payload in a Finder application with cutscene associations."""

from pathlib import Path
import plistlib
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile


def package(payload: Path, destination: Path, version: str) -> None:
    with tempfile.TemporaryDirectory(prefix="comic-macos-") as temporary:
        app = Path(temporary) / "ComicEditor.app"
        contents = app / "Contents"
        binaries = contents / "MacOS"
        shutil.copytree(payload, binaries)
        for name in ["ComicEditor.Desktop", "compiler/comic-compile"]:
            (binaries / name).chmod(0o755)
        resources = contents / "Resources"
        resources.mkdir()
        iconset = Path(temporary) / "comic-editor.iconset"
        iconset.mkdir()
        for size in [16, 32, 128, 256, 512]:
            for scale in [1, 2]:
                suffix = "@2x" if scale == 2 else ""
                subprocess.run(
                    [
                        "sips",
                        "-z",
                        str(size * scale),
                        str(size * scale),
                        str(binaries / "comic-editor.png"),
                        "--out",
                        str(iconset / f"icon_{size}x{size}{suffix}.png"),
                    ],
                    check=True,
                    stdout=subprocess.DEVNULL,
                )
        subprocess.run(
            [
                "iconutil",
                "-c",
                "icns",
                str(iconset),
                "-o",
                str(resources / "comic-editor.icns"),
            ],
            check=True,
        )
        info = {
            "CFBundleIdentifier": "org.comiceditor.storyboard",
            "CFBundleName": "ComicEditor",
            "CFBundleDisplayName": "ComicEditor",
            "CFBundlePackageType": "APPL",
            "CFBundleExecutable": "ComicEditor.Desktop",
            "CFBundleIconFile": "comic-editor.icns",
            "CFBundleShortVersionString": version,
            "CFBundleVersion": version,
            "NSHighResolutionCapable": True,
            "CFBundleDocumentTypes": [
                {
                    "CFBundleTypeName": "ComicEditor Cutscene",
                    "CFBundleTypeRole": "Editor",
                    "LSHandlerRank": "Owner",
                    "LSItemContentTypes": ["org.comiceditor.cutscene"],
                }
            ],
            "UTExportedTypeDeclarations": [
                {
                    "UTTypeIdentifier": "org.comiceditor.cutscene",
                    "UTTypeDescription": "ComicEditor Cutscene",
                    "UTTypeConformsTo": ["public.data"],
                    "UTTypeTagSpecification": {
                        "public.filename-extension": ["ctsc", "cutscene"],
                        "public.mime-type": "application/vnd.comiceditor.cutscene",
                    },
                }
            ],
        }
        (contents / "Info.plist").write_bytes(plistlib.dumps(info))
        with zipfile.ZipFile(destination, "w", zipfile.ZIP_DEFLATED) as archive:
            for file in sorted(app.rglob("*")):
                archive.write(file, file.relative_to(app.parent))
    print(f"Packaged {destination}")


if __name__ == "__main__":
    version = ET.parse(
        Path(__file__).resolve().parent.parent / "Directory.Build.props"
    ).findtext(".//Version")
    package(Path(sys.argv[1]), Path(sys.argv[2]), version)

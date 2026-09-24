"""Validate Finder metadata and native executable permissions in the app archive."""

import importlib.util
from pathlib import Path
import plistlib
import tempfile
import unittest
from unittest.mock import patch
import zipfile

SPEC = importlib.util.spec_from_file_location(
    "macos_package", Path(__file__).with_name("package-macos-app.py")
)
PACKAGING = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PACKAGING)


class MacOSPackageTests(unittest.TestCase):
    def test_finder_associations_permissions_and_signature_preservation(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            payload = root / "payload"
            (payload / "compiler").mkdir(parents=True)
            for name in [
                "ComicEditor.Desktop",
                "compiler/comic-compile",
                "ComicEditor.Desktop.sig",
            ]:
                (payload / name).write_bytes(b"signed contents")
            destination = root / "application.zip"

            def icon_command(args, **kwargs):
                if args[0] == "iconutil":
                    Path(args[-1]).write_bytes(b"icon")

            with patch.object(PACKAGING.subprocess, "run", side_effect=icon_command):
                PACKAGING.package(payload, destination, "1.2.3")
            with zipfile.ZipFile(destination) as archive:
                info = plistlib.loads(
                    archive.read("ComicEditor.app/Contents/Info.plist")
                )
                self.assertEqual("1.2.3", info["CFBundleVersion"])
                declaration = info["UTExportedTypeDeclarations"][0]
                self.assertEqual(
                    ["ctsc", "cutscene"],
                    declaration["UTTypeTagSpecification"]["public.filename-extension"],
                )
                self.assertEqual(
                    [declaration["UTTypeIdentifier"]],
                    info["CFBundleDocumentTypes"][0]["LSItemContentTypes"],
                )
                for name in ["ComicEditor.Desktop", "compiler/comic-compile"]:
                    entry = archive.getinfo("ComicEditor.app/Contents/MacOS/" + name)
                    self.assertEqual(0o755, (entry.external_attr >> 16) & 0o777)
                    self.assertEqual(b"signed contents", archive.read(entry))
                self.assertEqual(
                    b"signed contents",
                    archive.read(
                        "ComicEditor.app/Contents/MacOS/ComicEditor.Desktop.sig"
                    ),
                )


if __name__ == "__main__":
    unittest.main()

"""Smoke-test the published AOT updater entry point without changing an installation."""

import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import uuid


def check(package: Path) -> None:
    metadata = json.loads((package / "update.json").read_text())
    executable = (
        "ComicEditor.Desktop.exe"
        if metadata["runtime"].startswith("win-")
        else "ComicEditor.Desktop"
    )
    with tempfile.TemporaryDirectory(prefix="comic helper 'spaces' ") as temporary:
        root = Path(temporary)
        for file in package.iterdir():
            if file.is_file() and (
                file.name == executable or file.suffix in {".dll", ".so", ".dylib"}
            ):
                shutil.copy2(file, root / file.name)
        token = uuid.uuid4().hex
        plan = root / "plan.json"
        plan.write_text(
            json.dumps(
                {
                    "Token": token,
                    "Target": str(root / "unchanged"),
                    "Runtime": metadata["runtime"],
                    "Version": metadata["version"],
                    "ParentProcess": 2147483647,
                }
            ),
            encoding="utf-8",
        )
        # No approval file: the helper must report readiness, then exit without applying anything.
        result = subprocess.run(
            [str(root / executable), "--apply-update", str(plan)],
            capture_output=True,
            text=True,
            timeout=30,
        )
        assert result.returncode == 1, result.stdout + result.stderr
        assert (root / "helper.ready").read_text() == token
        assert not (root / "unchanged").exists()
        print(f"Native updater helper passed: {metadata['runtime']}")


if __name__ == "__main__":
    check(Path(sys.argv[1]).resolve())

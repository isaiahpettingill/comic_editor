"""Stamp release packages with metadata used by the in-app updater."""

import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET


def write_manifest(directory: Path, runtime: str, ref: str) -> None:
    version = ET.parse(
        Path(__file__).resolve().parent.parent / "Directory.Build.props"
    ).findtext(".//Version")
    if ref.startswith("v") and ref[1:] != version:
        raise ValueError(
            f"Release tag {ref} does not match application version {version}"
        )
    if runtime not in {"win-x64", "linux-x64", "osx-x64", "osx-arm64"}:
        raise ValueError(f"Unsupported runtime: {runtime}")
    (directory / "update.json").write_text(
        json.dumps({"version": version, "runtime": runtime}, indent=2) + "\n",
        encoding="utf-8",
    )


if __name__ == "__main__":
    write_manifest(Path(sys.argv[1]), sys.argv[2], sys.argv[3])

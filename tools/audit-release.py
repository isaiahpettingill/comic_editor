"""Reject debug payloads and report installed size before release packaging."""

from pathlib import Path
import sys


def audit(payload: Path) -> None:
    if not payload.is_dir() or not (payload / "update.json").is_file():
        raise ValueError("Expected a stamped desktop release directory")
    files = sorted(payload.rglob("*"))
    symbols = [
        p.relative_to(payload)
        for p in files
        if p.suffix.lower() in {".pdb", ".dbg", ".dsym"}
    ]
    if symbols:
        raise ValueError(
            "Debug symbols in release payload: " + ", ".join(map(str, symbols))
        )
    sizes = [(p.stat().st_size, p.relative_to(payload)) for p in files if p.is_file()]
    total = sum(size for size, _ in sizes)
    print(f"Installed payload (editor + CLI): {total / 1048576:.2f} MiB")
    for size, name in sorted(sizes, reverse=True)[:12]:
        print(f"  {size / 1048576:8.2f} MiB  {name}")
    # A generous regression guard for the full offline bundle, including CJK
    # fonts and both native executables. Review deliberately if features grow.
    if total > 160 * 1048576:
        raise ValueError("Release exceeds the 160 MiB installed-size budget")


if __name__ == "__main__":
    audit(Path(sys.argv[1]).resolve())

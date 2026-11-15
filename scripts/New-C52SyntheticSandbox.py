#!/usr/bin/env python3
"""Create new synthetic-only C-52 fixtures; never inspect or reuse a real directory."""

import json
import pathlib
import sys


def main() -> int:
    if len(sys.argv) != 2:
        raise SystemExit("usage: New-C52SyntheticSandbox.py <new-empty-sandbox>")
    root = pathlib.Path(sys.argv[1]).resolve()
    root.mkdir(parents=True, exist_ok=False)
    data = root / "installed" / "HolooSample" / "data"
    backup = root / "backup"
    data.mkdir(parents=True)
    backup.mkdir()
    (root / "programs.json").write_text(json.dumps([
        {"name": "هلو نمونه", "install_folder": "installed/HolooSample", "version": "0.0-demo"}
    ], ensure_ascii=False), encoding="utf-8")
    (root / "sql-instances.json").write_text(json.dumps(["HOLOOSAMPLE"]), encoding="utf-8")
    for extension in ("mdb", "accdb", "mdf", "fdb", "db", "dbf", "sqlite"):
        (data / f"synthetic.{extension}").write_bytes(b"NOT-A-DB")
    (data / "ignore.txt").write_text("synthetic non-candidate", encoding="utf-8")
    magic = bytes([0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x01, 0x00,
                   0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09])
    (backup / "sample-backup.zip").write_bytes(magic + bytes(4080))
    print(f"C52_SYNTHETIC sandbox={root} real_data=false")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

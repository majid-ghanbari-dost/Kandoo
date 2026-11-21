#!/usr/bin/env python3
"""Report APK packaging facts without executing or modifying the APK."""

import hashlib
import json
import pathlib
import sys
import zipfile


def main() -> int:
    if len(sys.argv) != 3:
        raise SystemExit("usage: Inspect-KandooApk.py <apk> <report.json>")
    apk = pathlib.Path(sys.argv[1])
    output = pathlib.Path(sys.argv[2])
    raw = apk.read_bytes()
    with zipfile.ZipFile(apk) as archive:
        entries = [
            {"name": item.filename, "bytes": item.file_size}
            for item in archive.infolist()
            if item.filename.startswith("assemblies/")
            or item.filename.endswith("/libmonodroid.so")
            or item.filename.endswith("/libxamarin-app.so")
        ]
    managed = [
        item for item in entries
        if item["name"].startswith("assemblies/") and item["bytes"] > 0
    ]
    report = {
        "apk": apk.name,
        "bytes": len(raw),
        "sha256": hashlib.sha256(raw).hexdigest(),
        "managed_payload_entries": managed,
        "managed_payload_present": bool(managed),
        "relevant_entries": entries,
        "note": "Packaging evidence only; launch failure cause requires emulator logcat.",
    }
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2, sort_keys=True))
    sample = ",".join(f"{item['name']}({item['bytes']})" for item in managed[:12])
    print(
        f"::notice::C50_APK path={apk} sha256={report['sha256']} "
        f"managed_payload_present={str(report['managed_payload_present']).lower()} "
        f"managed_payload_entries={len(managed)} sample={sample or 'none'}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

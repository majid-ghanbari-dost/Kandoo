#!/usr/bin/env python3
"""Emit synthetic CI UI/crash evidence as visible GitHub annotations."""

import pathlib
import sys
import xml.etree.ElementTree as ET


def annotate(level: str, marker: str, text: str) -> None:
    clean = text.replace("\r", " ").replace("\n", " ")
    if len(clean) > 5000:
        clean = clean[:5000] + "…"
    print(f"::{level}::{marker} {clean}")


def inspect(label: str, root: pathlib.Path) -> None:
    ui = root / "ui.xml"
    if ui.exists():
        try:
            document = ET.parse(ui)
            values = []
            for node in document.iter():
                for key in ("text", "content-desc"):
                    value = node.attrib.get(key)
                    if value:
                        values.append(value)
            annotate("notice", "C58_UI_VALUES", f"run={label} values={' | '.join(values) or 'NONE'}")
        except Exception as error:
            annotate("warning", "C58_UI_PARSE", f"run={label} error={error} raw={ui.read_text(errors='replace')[:2000]}")
    logcat = root / "logcat.txt"
    if not logcat.exists():
        annotate("warning", "C58_LOGCAT_MISSING", f"run={label}")
        return
    lines = logcat.read_text(errors="replace").splitlines()
    indexes = [i for i, line in enumerate(lines) if "KANDOO_CRASH" in line or "KandooCrash" in line]
    annotate("notice", "C58_CRASH_INDEXES", f"run={label} count={len(indexes)}")
    shown = set()
    for index in indexes[:4]:
        for current in range(max(0, index - 2), min(len(lines), index + 13)):
            if current not in shown:
                annotate("warning", "C58_CRASH_CONTEXT", f"run={label} {lines[current]}")
                shown.add(current)


def main() -> int:
    if len(sys.argv) < 2:
        raise SystemExit("usage: Inspect-C58Evidence.py <label=path> [...]")
    for item in sys.argv[1:]:
        label, separator, path = item.partition("=")
        if not separator:
            raise SystemExit("each evidence argument must be label=path")
        inspect(label, pathlib.Path(path))
    return 0


if __name__ == "__main__":
    sys.exit(main())

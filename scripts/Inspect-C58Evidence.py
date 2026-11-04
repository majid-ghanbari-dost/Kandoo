#!/usr/bin/env python3
"""Emit CI UI/crash evidence as visible GitHub annotations."""

import os
import pathlib
import re
import subprocess
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
            nodes = []
            for node in document.iter():
                attributes = node.attrib
                nodes.append(
                    f"{attributes.get('class', '?')}[{attributes.get('resource-id', '')}]"
                    f" bounds={attributes.get('bounds', '')}"
                    f" text={attributes.get('text', '')}"
                    f" desc={attributes.get('content-desc', '')}")
            for offset in range(0, len(nodes), 30):
                annotate("notice", "C58_UI_NODES", f"run={label} from={offset} {' | '.join(nodes[offset:offset + 30])}")
        except Exception as error:
            annotate("warning", "C58_UI_PARSE", f"run={label} error={error} raw={ui.read_text(errors='replace')[:2000]}")
    logcat = root / "logcat.txt"
    if not logcat.exists():
        annotate("warning", "C58_LOGCAT_MISSING", f"run={label}")
        return
    lines = logcat.read_text(errors="replace").splitlines()
    debug_lines = [line for line in lines if "KANDOO_C58" in line]
    for line in debug_lines[:45]:
        annotate("notice", "C58_DEBUG", f"run={label} {line}")
    indexes = [i for i, line in enumerate(lines) if "KANDOO_CRASH" in line or "KandooCrash" in line]
    annotate("notice", "C58_CRASH_INDEXES", f"run={label} count={len(indexes)}")
    shown = set()
    for index in indexes[:2]:
        for current in range(max(0, index - 3), min(len(lines), index + 48)):
            if current not in shown:
                annotate("warning", "C58_CRASH_CONTEXT", f"run={label} {lines[current]}")
                shown.add(current)
    if not indexes:
        diagnostic_indexes = [
            i for i, line in enumerate(lines)
            if re.search(r"exception|fatal|\banr\b|notimplemented|font|typeface|failed", line, re.IGNORECASE)
            and re.search(r"ir\.kandoo\.app|AndroidRuntime|Mono|Microsoft\.Maui", line, re.IGNORECASE)]
        annotate("notice", "C58_EXCEPTION_INDEXES", f"run={label} count={len(diagnostic_indexes)}")
        for index in diagnostic_indexes[:12]:
            for current in range(max(0, index - 1), min(len(lines), index + 3)):
                if current not in shown:
                    annotate("warning", "C58_EXCEPTION_CONTEXT", f"run={label} {lines[current]}")
                    shown.add(current)


def inspect_theme_attributes(root: pathlib.Path, apk: pathlib.Path) -> None:
    logcat = root / "logcat.txt"
    if not logcat.exists() or not apk.exists():
        return
    resource_ids = sorted(set(re.findall(
        r"TypedValue\{t=0x2/d=(0x[0-9a-fA-F]+)",
        logcat.read_text(errors="replace"))))
    if not resource_ids:
        return
    sdk = os.environ.get("ANDROID_HOME") or os.environ.get("ANDROID_SDK_ROOT")
    candidates = sorted(pathlib.Path(sdk, "build-tools").glob("*/aapt2")) if sdk else []
    if not candidates:
        annotate("warning", "C58_AAPT_MISSING", f"ids={','.join(resource_ids)}")
        return
    try:
        result = subprocess.run(
            [str(candidates[-1]), "dump", "resources", str(apk)],
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            errors="replace",
            timeout=120,
            check=False)
    except (OSError, subprocess.SubprocessError) as error:
        annotate("warning", "C58_AAPT", f"error={error}")
        return
    if result.returncode != 0:
        annotate("warning", "C58_AAPT", f"exit={result.returncode} output={result.stdout[:2000]}")
        return
    lines = result.stdout.splitlines()
    for resource_id in resource_ids:
        matches = [i for i, line in enumerate(lines) if resource_id.lower() in line.lower()]
        if not matches:
            annotate("warning", "C58_THEME_ATTRIBUTE", f"id={resource_id} not_found_in_apk_resources")
            continue
        for index in matches[:2]:
            context = " | ".join(lines[index:index + 4])
            annotate("notice", "C58_THEME_ATTRIBUTE", f"id={resource_id} {context}")


def main() -> int:
    if len(sys.argv) < 2:
        raise SystemExit("usage: Inspect-C58Evidence.py <label=path> [...] [apk=path]")
    evidence = []
    apk = None
    for item in sys.argv[1:]:
        label, separator, path = item.partition("=")
        if not separator:
            raise SystemExit("each evidence argument must be label=path")
        if label == "apk":
            apk = pathlib.Path(path)
            continue
        evidence.append((label, pathlib.Path(path)))
    for label, root in evidence:
        inspect(label, root)
        if apk is not None:
            inspect_theme_attributes(root, apk)
    return 0


if __name__ == "__main__":
    sys.exit(main())

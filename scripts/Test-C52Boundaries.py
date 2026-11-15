#!/usr/bin/env python3
"""Fail if the C-52 production console gains network or non-report write APIs."""

import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
PROJECT = ROOT / "src" / "Kandoo.HolooDiscovery"
BANNED = (
    "System.Net", "HttpClient", "WebRequest", "TcpClient", "UdpClient", "Socket(",
    "SqlConnection", "Microsoft.Data.Sql", "Npgsql", "ODBC", "OleDb",
    "File.WriteAllBytes", "File.AppendAll", "File.OpenWrite", "File.Create",
    "Directory.CreateDirectory", "BinaryWriter", "StreamWriter",
)


def main() -> int:
    sources = {
        path: path.read_text(encoding="utf-8")
        for path in PROJECT.rglob("*.cs")
        if not {"bin", "obj"}.intersection(path.relative_to(PROJECT).parts)
    }
    violations = []
    for path, text in sources.items():
        for token in BANNED:
            if token in text:
                violations.append(f"{path.relative_to(ROOT)}: forbidden {token}")
    writes = sum(text.count("File.WriteAllText") for text in sources.values())
    if writes != 1 or "File.WriteAllText" not in sources[PROJECT / "ReportWriter.cs"]:
        violations.append("production must have exactly one report-writing call in ReportWriter.cs")
    if violations:
        print("\n".join("::error::C52_BOUNDARY " + item for item in violations))
        return 1
    print(f"::notice::C52_BOUNDARIES PASS files={len(sources)} network_apis=0 non_report_writes=0")
    return 0


if __name__ == "__main__":
    sys.exit(main())

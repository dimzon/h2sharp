#!/usr/bin/env python3
"""Print the uncovered lines of one source file from merged cobertura reports.

Unions the line data of every report under the results directory (the same
normalization as merge_cov.py: "H2Sharp\\X" and "X" name one file), then prints
the lines of the requested file that no report covers, with their source text.

Usage (from the repository root):
    py -3 tools/coverage/uncovered.py [--results-dir artifacts/TestResults]
                                      FILE (case-insensitive substring)

Standard library only.
"""

import argparse
import glob
import os
import re
from xml.etree import ElementTree as ET


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--results-dir",
        default=os.path.join("artifacts", "TestResults", "coverage-sprint"),
        help="directory with one subdirectory per suite holding coverage.cobertura.xml",
    )
    parser.add_argument("file", help="case-insensitive substring of the source path")
    return parser.parse_args()


def normalize(filename: str) -> str:
    if filename.startswith("H2Sharp\\"):
        return filename[len("H2Sharp\\") :]
    return filename


def main() -> int:
    args = parse_args()
    reports = glob.glob(
        os.path.join(args.results_dir, "*", "*", "coverage.cobertura.xml")
    )
    if not reports:
        print(f"no coverage.cobertura.xml under {args.results_dir}")
        return 1

    seen: dict[str, dict[int, list[tuple[str, int]]]] = {}
    sources: dict[str, str] = {}
    for report in sorted(reports):
        tree = ET.parse(report)
        for pkg in tree.getroot().find("packages"):
            for cls in pkg.find("classes"):
                raw = cls.get("filename")
                if raw is None:
                    continue
                name = normalize(raw)
                if args.file.lower() not in name.lower():
                    continue
                lines = cls.find("lines")
                if lines is None:
                    continue
                data = seen.setdefault(name, {})
                for ln in lines:
                    num = int(ln.get("number"))
                    hits = int(ln.get("hits", "0"))
                    cc = ln.get("condition-coverage")
                    entry = data.setdefault(num, [])
                    entry.append((os.path.basename(os.path.dirname(report)), hits, cc))

    for name, data in seen.items():
        print(f"=== {name} ===")
        path = os.path.join("src", "H2Sharp", name.replace("\\", os.sep))
        if not os.path.exists(path):
            path = name.replace("\\", os.sep)
        text: dict[int, str] = {}
        if os.path.exists(path):
            with open(path, encoding="utf-8-sig") as f:
                for i, line in enumerate(f, 1):
                    text[i] = line.rstrip("\n")
        total = len(data)
        covered = sum(1 for entries in data.values() if any(h > 0 for _, h, _ in entries))
        print(f"lines: {covered}/{total}")
        for num in sorted(data):
            hits = max(h for _, h, _ in data[num])
            if hits > 0:
                continue
            branch = data[num][0][2] or ""
            branch_part = ""
            m = re.match(r"\((\d+)/(\d+)\)", branch)
            if m and m.group(1) != m.group(2):
                branch_part = f"  [branch {m.group(1)}/{m.group(2)}]"
            source = text.get(num, "").strip()
            print(f"  {num:5} {source}{branch_part}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

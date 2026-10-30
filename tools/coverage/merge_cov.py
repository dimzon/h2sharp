#!/usr/bin/env python3
"""Merge coverlet Cobertura reports from the H2Sharp test suites.

Each suite writes its own coverage.cobertura.xml; the per-report totals cannot
just be added up, because every suite covers mostly the same code. This script
unions covered lines and branches across all reports and prints overall totals,
per-assembly and per-directory breakdowns, and the least-covered files.

Reports disagree on the relative root: some use "H2Sharp\\..." (relative to
src), others bare "..." (relative to src/H2Sharp). Both spellings are
normalized to the latter so the same file unions instead of doubling.

Usage (from the repository root):
    py -3 tools/coverage/merge_cov.py [--results-dir artifacts/TestResults]
                                      [--min-lines 200] [--top 15]

Requires only the Python standard library.
"""

import argparse
import glob
import os
from collections import defaultdict
from xml.etree import ElementTree as ET


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--results-dir",
        default=os.path.join("artifacts", "TestResults"),
        help="directory with one subdirectory per suite holding coverage.cobertura.xml",
    )
    parser.add_argument(
        "--min-lines",
        type=int,
        default=200,
        help="only files with at least this many coverable lines appear in the tail list",
    )
    parser.add_argument(
        "--top",
        type=int,
        default=15,
        help="how many least-covered files to list",
    )
    return parser.parse_args()


def normalize(filename: str) -> str:
    # See the module docstring: "H2Sharp\\X" and "X" name the same file.
    # "H2Sharp.X\\..." is a different assembly and is left alone.
    if filename.startswith("H2Sharp\\"):
        return filename[len("H2Sharp\\") :]
    return filename


def main() -> int:
    args = parse_args()
    files = glob.glob(
        os.path.join(args.results_dir, "*", "*", "coverage.cobertura.xml")
    )
    if not files:
        print(f"no coverage.cobertura.xml under {args.results_dir}")
        return 1
    print(f"reports: {len(files)}")

    line_hit: set = set()
    line_seen: set = set()
    br_cov: dict = defaultdict(int)
    br_tot: dict = defaultdict(int)
    asm_line_hit: dict = defaultdict(int)
    asm_line_tot: dict = defaultdict(int)
    asm_br_cov: dict = defaultdict(int)
    asm_br_tot: dict = defaultdict(int)
    file_hit: dict = defaultdict(set)
    file_tot: dict = defaultdict(set)

    for f in sorted(files):
        print(f"parsing {f}")
        tree = ET.parse(f)
        packages = tree.getroot().find("packages")
        if packages is None:
            continue
        for pkg in packages:
            asm = pkg.get("name")
            classes = pkg.find("classes")
            if classes is None:
                continue
            for cls in classes:
                raw = cls.get("filename")
                if raw is None:
                    continue
                fn = normalize(raw)
                lines = cls.find("lines")
                if lines is None:
                    continue
                for ln in lines:
                    num = ln.get("number")
                    k = (fn, num)
                    ak = (asm, fn, num)
                    line_seen.add(k)
                    asm_line_tot[ak] += 1
                    file_tot[fn].add(num)
                    if int(ln.get("hits", "0")) > 0:
                        line_hit.add(k)
                        asm_line_hit[ak] = 1
                        file_hit[fn].add(num)
                    # This coverlet emits branches as a condition-coverage
                    # attribute ("50% (1/2)"), not as child elements.
                    cc = ln.get("condition-coverage")
                    if cc and "/" in cc:
                        try:
                            cov, tot = cc.split("(")[1].rstrip(")").split("/")
                            cov, tot = int(cov), int(tot)
                        except ValueError:
                            cov, tot = 0, 0
                        if tot > 0:
                            bk = (fn, num)
                            abk = (asm, fn, num)
                            br_tot[bk] = max(br_tot[bk], tot)
                            br_cov[bk] = max(br_cov[bk], cov)
                            asm_br_tot[abk] = max(asm_br_tot[abk], tot)
                            asm_br_cov[abk] = max(asm_br_cov[abk], cov)

    hl, tl = len(line_hit), len(line_seen)
    hb = sum(br_cov.values())
    tb = sum(br_tot.values())
    print(f"OVERALL lines: {hl}/{tl} = {100.0 * hl / tl:.2f}%")
    print(f"OVERALL branches: {hb}/{tb} = {100.0 * hb / tb:.2f}%")

    print("--- per assembly (lines / branches) ---")
    asms = sorted(
        {a for (a, _, _) in asm_line_tot} | {a for (a, _, _) in asm_br_tot}
    )
    for a in asms:
        t = sum(1 for k in asm_line_tot if k[0] == a)
        h = sum(1 for k in asm_line_tot if k[0] == a and asm_line_hit.get(k))
        bt = sum(asm_br_tot[k] for k in asm_br_tot if k[0] == a)
        bh = sum(asm_br_cov[k] for k in asm_br_tot if k[0] == a)
        lp = 100.0 * h / t if t else 0
        bp = 100.0 * bh / bt if bt else 0
        print(f"{a}: lines {h}/{t} ({lp:.2f}%) branches {bh}/{bt} ({bp:.2f}%)")

    print("--- per top-level src dir ---")
    dirs: dict = defaultdict(lambda: [0, 0])
    for fn, nums in file_tot.items():
        parts = fn.replace("\\", "/").split("/")
        d = parts[0] if len(parts) > 1 else "(root)"
        dirs[d][0] += len(file_hit.get(fn, set()))
        dirs[d][1] += len(nums)
    for d in sorted(dirs):
        h, t = dirs[d]
        print(f"{d}: {h}/{t} ({100.0 * h / t:.2f}%)")

    print(f"--- {args.top} least-covered files (>={args.min_lines} lines) ---")
    rows = []
    for fn, nums in file_tot.items():
        t = len(nums)
        if t >= args.min_lines:
            h = len(file_hit.get(fn, set()))
            rows.append((100.0 * h / t, h, t, fn))
    for p, h, t, fn in sorted(rows)[: args.top]:
        print(f"{p:5.2f}% {h}/{t} {fn}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

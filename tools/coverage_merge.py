#!/usr/bin/env python3
"""Incremental coverage merge for one class (default: H2Sharp.Command.Parser).

Usage:
    python coverage_merge.py [--class NAME] DIR... [NEWDIR...]

Each DIR is a coverage results directory (the one containing */coverage.cobertura.xml
subdirectories). Line hits are unioned across all files, so a small run of only the
new tests can be merged against a full baseline without re-running everything.
"""
import argparse
import glob
import os
import sys
import xml.etree.ElementTree as ET


def cobertura_files(directory):
    pattern = os.path.join(directory, '*', 'coverage.cobertura.xml')
    return glob.glob(pattern)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('dirs', nargs='+')
    ap.add_argument('--class', dest='class_name', default='H2Sharp.Command.Parser')
    args = ap.parse_args()

    files = []
    for d in args.dirs:
        files.extend(cobertura_files(d))
    if not files:
        print('no cobertura files found', file=sys.stderr)
        sys.exit(2)

    total = set()
    covered = set()
    for f in files:
        try:
            root = ET.parse(f).getroot()
        except ET.ParseError:
            continue
        for cls in root.iter('class'):
            if (cls.get('name') or '') != args.class_name:
                continue
            for line in cls.iter('line'):
                n = int(line.get('number'))
                total.add(n)
                if int(line.get('hits') or 0) > 0:
                    covered.add(n)

    pct = 100.0 * len(covered) / len(total) if total else 0.0
    print('%s: %d/%d (%.1f%%)' % (args.class_name, len(covered), len(total), pct))
    print('uncovered: %d' % len(total - covered))


if __name__ == '__main__':
    main()

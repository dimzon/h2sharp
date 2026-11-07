# Coverage tooling

Full-suite coverage in Debug (Release inlining skews line numbers — see AGENTS.md).

## Run everything

From the repository root:

```powershell
.\tools\coverage\run_coverage.ps1
```

Runs Data, MvStore, Engine, Core (`H2Sharp.Tests`), SQL Specs and Native
sequentially with `--collect:"XPlat Code Coverage"`, then prints the merged
summary. The suites must run sequentially: parallel builds contend on the shared
output DLL (CS2012 file locks). The native suite needs `H2SHARP_NATIVE_TESTS=1`,
which the script sets itself. Suites with known red tests do not stop the run.

The native suite contains a few very heavy tests (`TestCache`,
`TestCachedQueryResults`, `TestAnalyzeTableTx`, ...). A full native run can take
much longer than the rest of the suites; see
[docs/COVERAGE_ANALYSIS.md](../../docs/COVERAGE_ANALYSIS.md) for the measured
breakdown and the exclusions used for the reported baseline.

`H2SHARP_NATIVE_MEMORY=1` selects in-memory databases in `TestBase`. It is a
speed profile for heavy tests, not a replacement for the file profile: tests
that inspect database files or persistence (`TestSpaceReuse`, `TestReadOnly`,
`TestSynonymForTable`, `TestMultiThread`) fail in it. The analysis above uses
it only for heavy tests that pass in memory.

## Merge only

If the per-suite reports already exist under `artifacts/TestResults/<Suite>/*/coverage.cobertura.xml`:

```powershell
py -3 tools/coverage/merge_cov.py [--results-dir artifacts/TestResults] [--min-lines 200] [--top 15]
```

Per-report totals cannot just be added up — every suite covers mostly the same
code — so the script unions covered lines and branches across all reports and
prints overall totals plus per-assembly, per-directory and least-covered-file
breakdowns. Reports disagree on the relative root (`H2Sharp\...` vs bare
`...`); both spellings are normalized so the same file unions instead of
doubling. Standard library only.

## Uncovered lines of one file

```powershell
py -3 tools/coverage/uncovered.py [--results-dir artifacts/TestResults/coverage-sprint] FILE
```

`FILE` is a case-insensitive substring of the source path, e.g.
`SetClauseList` or `Store\FileStore.cs`. The script unions the line data of
every report under the results directory (the same normalization as
`merge_cov.py`), then prints the lines of the file that no report covers,
with their source text and the partial-branch counts. This is the tool the
coverage sprints start from: pick the file, read the exact uncovered lines,
build the test for those lines.

## Current baseline (2026-10-30, commit 9e801f6)

Tests: Data 577/577, MvStore 87/87, Engine 4/9 (5 known red), Core 1471/1471,
Native 74/115 (41 known red, incl. 3 `BlockedByPort`).

Coverage: lines 63258/79030 = **80.04%**, branches 26435/34785 = **76.00%**.
H2Sharp 80.06%/76.04%, Crypto 93.57%/86.84%, Hashes 100%/100%,
JavaLegacy 50.63%/43.33%.

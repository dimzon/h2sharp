#!/usr/bin/env python3
"""Regenerate every generated file in src/H2Sharp from its upstream source.

CI runs this and then `git diff --exit-code`, so a generated table that no longer
matches its source fails the build instead of silently rotting. That matters most for
Util/CaseFolding.cs: it is a snapshot of one JVM's Unicode tables, and nothing else in
the build would notice when H2 moves to a newer ICU.

    python tools/regen.py            # regenerate in place
    python tools/regen.py --check    # regenerate, then fail if anything changed

Requires a JDK and the reference H2 checkout, because the whole point is to read the
reference. See docs/PORTING.md.
"""
import argparse
import os
import pathlib
import shutil
import subprocess
import sys
import tempfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
TOOLS = ROOT / "tools"
REFERENCE = ROOT / "reference" / "h2"
JAR = REFERENCE / "h2" / "target"

GENERATED = [
    pathlib.Path("src/H2Sharp/Api/ErrorCode.cs"),
    pathlib.Path("src/H2Sharp/Message/ErrorCodeKind.cs"),
    pathlib.Path("src/H2Sharp/Util/CaseFolding.cs"),
    pathlib.Path("src/H2Sharp/Util/ParserUtil.cs"),
]


def fail(message: str) -> "None":
    print(f"ERROR: {message}", file=sys.stderr)
    raise SystemExit(1)


def run(argv: list[str], cwd: pathlib.Path | None = None) -> str:
    result = subprocess.run(
        argv, cwd=cwd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if result.returncode != 0:
        fail(f"command failed: {' '.join(argv)}\n{result.stdout}\n{result.stderr}")
    return result.stdout


def h2_jar() -> pathlib.Path:
    """The built H2 jar, used as the classpath for the Java probes."""
    if not JAR.is_dir():
        fail(
            "reference H2 jar directory is missing: " + str(JAR) + "\n"
            "Build it with: (cd reference/h2/h2 && mvn -B -q -DskipTests package)")
    jars = sorted(JAR.glob("h2-*.jar"))
    # The shaded/uber jar also matches h2-*.jar, so prefer a plain one.
    plain = [j for j in jars if not j.name.endswith("-sources.jar")]
    if not plain:
        fail(f"no h2-*.jar in {JAR}. Build it with: "
             "(cd reference/h2/h2 && mvn -B -q -DskipTests package)")
    return plain[-1]


def regen_error_codes() -> None:
    run([sys.executable, str(TOOLS / "port_errorcode.py")])
    run([sys.executable, str(TOOLS / "port_dbexception_kind.py")])
    run([sys.executable, str(TOOLS / "port_parserutil.py")])


def regen_case_folding(work: pathlib.Path) -> None:
    """Java full mapping (reference JVM) x .NET simple mapping -> the override table."""
    if shutil.which("javac") is None or shutil.which("java") is None:
        fail("javac/java not found on PATH; needed to read the reference case mapping")

    classes = work / "java"
    classes.mkdir(parents=True, exist_ok=True)
    jar = h2_jar()

    run(["javac", "-d", str(classes), str(TOOLS / "DumpCaseTable.java")])
    java_rows = run(["java", "-cp", f"{classes}{os.pathsep}{jar}", "DumpCaseTable"])

    dotnet_rows = run(
        ["dotnet", "run", "--project", str(TOOLS / "CaseProbe"), "--configuration", "Release"])

    java_tsv = work / "case-java.tsv"
    dotnet_tsv = work / "case-dotnet.tsv"
    java_tsv.write_text(java_rows, encoding="ascii", newline="\n")
    dotnet_tsv.write_text(dotnet_rows, encoding="ascii", newline="\n")

    run([sys.executable, str(TOOLS / "gen_case_folding.py"), str(java_tsv), str(dotnet_tsv)])


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--check", action="store_true",
        help="fail if any generated file differs from what regeneration produces")
    args = parser.parse_args()

    if not (REFERENCE / "h2" / "src" / "main").is_dir():
        fail("reference/h2 is not checked out; see docs/PORTING.md")

    regen_error_codes()
    with tempfile.TemporaryDirectory() as tmp:
        regen_case_folding(pathlib.Path(tmp))

    if args.check:
        changed = run(["git", "diff", "--name-only", "--"] +
                      [str(p) for p in GENERATED]).split()
        if changed:
            print("ERROR: generated files are stale, run: python tools/regen.py",
                  file=sys.stderr)
            for path in changed:
                print(f"  {path}", file=sys.stderr)
            print(run(["git", "diff", "--"] + changed), file=sys.stderr)
            return 1
        print("generated files are up to date")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

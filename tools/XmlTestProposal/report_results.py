"""Compare only core/conformance/XML TRX files; no unrelated test projects are read."""
import json
import sys
from pathlib import Path
import xml.etree.ElementTree as ET

OUT = Path(r"C:\Users\User\AppData\Local\Temp\opencode\h2-xml-bcl-prototype-20261025\xml-test-proposal")
OLD = Path(r"C:\Users\User\AppData\Local\Temp\opencode\h2-regression-8e78f98-20261025")
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
def results(path):
    root = ET.parse(path).getroot()
    rows = root.findall("t:Results/t:UnitTestResult", NS)
    output = {row.attrib["testName"]: row.attrib["outcome"] for row in rows}
    assert len(output) == len(rows), "Duplicate test names: " + str(path)
    return output
def compare(before, after):
    transitions = {}
    for name, old in before.items():
        current = after.get(name, "MISSING")
        if current != old:
            transitions[name] = {"before": old, "after": current}
    new = {name: result for name, result in after.items() if name not in before}
    regressions = {name: transition for name, transition in transitions.items()
        if transition["before"] == "Passed" and transition["after"] != "Passed" or transition["after"] == "MISSING"}
    return {"before_count": len(before), "after_count": len(after), "transitions": transitions,
        "new_tests": new, "regressions_or_missing": regressions}
regex = len(sys.argv) > 1 and sys.argv[1] == "regex"
sequence = len(sys.argv) > 1 and sys.argv[1] == "sequence"
xml = None
if sequence:
    core = compare(results(OUT / "regex-core-full.trx"), results(OUT / "seq-core-full.trx"))
    specs = compare(results(OUT / "regex-specs-full.trx"), results(OUT / "seq-specs-full.trx"))
    report = {"core": core, "conformance": specs}
elif regex:
    core = compare(results(OUT / "xml-core-regression.trx"), results(OUT / "regex-core-full.trx"))
    specs = compare(results(OUT / "xml-specs-full.trx"), results(OUT / "regex-specs-full.trx"))
    report = {"core": core, "conformance": specs}
else:
    core = compare(results(OLD / "final-core.trx"), results(OUT / "xml-core-regression.trx"))
    specs = compare(results(OLD / "final-specs.trx"), results(OUT / "xml-specs-full.trx"))
    xml = compare(results(OUT / "xml-baseline.trx"), results(OUT / "xml-approved-original.trx"))
    report = {"core": core, "conformance": specs, "original_xml": xml}
(OUT / ("regex-regression-comparison.json" if regex else "xml-regression-comparison.json")).write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
for title, data in report.items():
    print(title + ": baseline=" + str(data["before_count"]) + ", current=" + str(data["after_count"])
        + ", changed=" + str(len(data["transitions"])) + ", new=" + str(len(data["new_tests"]))
        + ", regressions/missing=" + str(len(data["regressions_or_missing"])))
for name, outcome in core["new_tests"].items():
    if outcome != "Passed":
        assert name.startswith("H2Sharp.Tests.PostfixCastOracleTests."), "Unexpected new failing test: " + name
        print("Previously introduced non-XML WIP still fails: " + name)
assert not core["regressions_or_missing"]
assert not specs["regressions_or_missing"]
if sequence:
    assert len(specs["transitions"]) == 3
    assert all("SequenceGrammarTests" in name and item["before"] == "Failed" and item["after"] == "Passed"
        for name, item in specs["transitions"].items())
elif regex:
    assert len(specs["transitions"]) == 3
    assert all("StringFunctionTests.Regexp" in name and item["before"] == "Failed" and item["after"] == "Passed"
        for name, item in specs["transitions"].items())
else:
    assert xml is not None
    assert all(item["before"] == "Failed" and item["after"] == "Passed" for item in xml["transitions"].values())
    assert len(xml["transitions"]) == 6

"""Generate an XML-only proposal; never edit existing tests or scan unrelated suites."""
import difflib
import hashlib
import json
from pathlib import Path
import re

ROOT = Path(r"C:\Work\h2sharp")
HERE = Path(__file__).resolve().parent
OUT = Path(r"C:\Users\User\AppData\Local\Temp\opencode\h2-xml-bcl-prototype-20261025\xml-test-proposal")
TARGET = "tests/H2SharpSql.Specs/Functions/XmlFunctionTests.cs"
HELPER = "tests/H2SharpSql.Specs/Infrastructure/XmlFunctionAssert.cs"
original = (ROOT / TARGET).read_text(encoding="utf-8")
draft = (HERE / "XmlFunctionTests.cs.draft").read_text(encoding="utf-8")
helper = (HERE / "XmlFunctionAssert.cs").read_text(encoding="utf-8")
queries = lambda text: re.findall(r'QuerySingle<string>\("([^"\n]*)"\)', text)
facts = lambda text: re.findall(r"public void (\w+)\(\)", text)
assert len(queries(original)) == 8 and queries(original) == queries(draft)
assert len(facts(original)) == 6 and facts(original) == facts(draft)
assert original.count("using IDbConnection connection = TestDatabase.Open();") == 6
assert draft.count("using IDbConnection connection = TestDatabase.Open();") == 6
assert draft.count("[Fact]") == original.count("[Fact]") == 6
assert not re.search(r"Skip\s*=|BlockedByPort|ExecuteScalar|QuerySingle<byte", draft)
patch = "".join(difflib.unified_diff(original.splitlines(True), draft.splitlines(True),
    fromfile="a/" + TARGET, tofile="b/" + TARGET))
patch += "".join(difflib.unified_diff([], helper.splitlines(True),
    fromfile="/dev/null", tofile="b/" + HELPER))
patch_path = OUT / "proposed-xml-tests.patch"
if patch_path.exists():
    assert patch_path.read_text(encoding="utf-8") == patch, "Sealed XML proposal differs; use approval verification after applying it"
else:
    patch_path.write_text(patch, encoding="utf-8", newline="\n")
checksums = {name: hashlib.sha256((ROOT / name).read_bytes()).hexdigest() for name in [
    TARGET, "tests/H2SharpSql.Specs/Infrastructure/TestDatabase.cs",
    "tests/H2SharpSql.Specs/H2SharpSql.Specs.csproj"]}
manifest = {"scope": list(checksums), "sha256": checksums, "test_names": facts(original),
    "sql": queries(original), "proposal_sha256": hashlib.sha256(patch.encode()).hexdigest()}
path = OUT / "xml-only-manifest.json"
if path.exists():
    assert json.loads(path.read_text(encoding="utf-8")) == manifest, "XML proposal or protected XML files changed"
else:
    path.write_text(json.dumps(manifest, indent=2, ensure_ascii=False), encoding="utf-8")
print("XML-only protected files unchanged: 3; Fact names preserved: 6; exact SQL/getter calls preserved: 8")
print("Proposal: " + str(OUT / "proposed-xml-tests.patch"))

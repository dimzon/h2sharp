"""Record the user's exact XML-diff approval; do not rewrite the frozen baseline."""
import hashlib
import json
from pathlib import Path

ROOT = Path(r"C:\Work\h2sharp")
HERE = Path(__file__).resolve().parent
OUT = Path(r"C:\Users\User\AppData\Local\Temp\opencode\h2-xml-bcl-prototype-20261025\xml-test-proposal")
def checksum(path): return hashlib.sha256(path.read_bytes()).hexdigest()
proposal_sha = "a1728ce32cd4942c5ab17f83c61e592f35a86538d436f8773f6cd8d164cf3957"
assert checksum(OUT / "proposed-xml-tests.patch") == proposal_sha
approval = {
    "user_approval": "Утверждаю XML-diff",
    "proposal_sha256": proposal_sha,
    "expected_files": {
        "tests/H2SharpSql.Specs/Functions/XmlFunctionTests.cs": checksum(HERE / "XmlFunctionTests.cs.draft"),
        "tests/H2SharpSql.Specs/Infrastructure/XmlFunctionAssert.cs": checksum(HERE / "XmlFunctionAssert.cs"),
    },
}
for name, expected in approval["expected_files"].items():
    assert checksum(ROOT / name) == expected, "Applied file differs from approved XML proposal: " + name
path = OUT / "approved-xml-test-changes.json"
if path.exists(): assert json.loads(path.read_text(encoding="utf-8")) == approval
else: path.write_text(json.dumps(approval, ensure_ascii=False, indent=2), encoding="utf-8")
print("Exact approved XML diff and helper verified; proposal SHA256: " + proposal_sha)

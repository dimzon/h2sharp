"""Record the user's approval for the two jar-verified ALTER DOMAIN test changes."""
import hashlib
import json
from pathlib import Path

ROOT = Path(r"C:\Work\h2sharp")
OUT = Path(r"C:\Users\User\AppData\Local\Temp\opencode\h2-xml-bcl-prototype-20261025\xml-test-proposal")
name = "tests/H2SharpSql.Specs/Commands/AlterDomainCommandTests.cs"
text = (ROOT / name).read_text(encoding="utf-8")
assert "Assert.Equal(7, connection.QuerySingle<int>(\"SELECT V FROM T\"));" in text
assert "INSERT INTO T(V) VALUES (-1)" in text
assert "INSERT INTO T(V) VALUES (-2)" in text
approval = {
    "user_approval": "Утверждаю оба diff",
    "reason": "jar: SET ON UPDATE copies into dependent column, DROP leaves 7; NOCHECK validates new rows (23513)",
    "expected_files": {name: hashlib.sha256((ROOT / name).read_bytes()).hexdigest()},
}
path = OUT / "approved-domain-test-changes.json"
if path.exists():
    assert json.loads(path.read_text(encoding="utf-8")) == approval
else:
    path.write_text(json.dumps(approval, ensure_ascii=False, indent=2), encoding="utf-8")
print("Approved domain test changes recorded: " + approval["expected_files"][name])

"""Record the user's exact approval for the SequenceGrammar CYCLE test change."""
import hashlib
import json
from pathlib import Path

ROOT = Path(r"C:\Work\h2sharp")
OUT = Path(r"C:\Users\User\AppData\Local\Temp\opencode\h2-xml-bcl-prototype-20261025\xml-test-proposal")
name = "tests/H2SharpSql.Specs/Grammar/SequenceGrammarTests.cs"
text = (ROOT / name).read_text(encoding="utf-8")
assert 'CREATE SEQUENCE SEQ_ID MAXVALUE 2 START WITH 1 CYCLE' in text
assert text.count('SELECT NEXT VALUE FOR SEQ_ID') >= 3
assert 'Assert.Equal(2, connection.QuerySingle<int>("SELECT NEXT VALUE FOR SEQ_ID"));' in text
approval = {
    "user_approval": "Утверждаю diff CYCLE",
    "reason": "jar rejects MAXVALUE 1 START WITH 1 (90009); MAXVALUE 2 gives 1,2,1",
    "expected_files": {name: hashlib.sha256((ROOT / name).read_bytes()).hexdigest()},
}
path = OUT / "approved-ddl-test-changes.json"
if path.exists():
    assert json.loads(path.read_text(encoding="utf-8")) == approval
else:
    path.write_text(json.dumps(approval, ensure_ascii=False, indent=2), encoding="utf-8")
print("Approved CYCLE diff recorded: " + approval["expected_files"][name])

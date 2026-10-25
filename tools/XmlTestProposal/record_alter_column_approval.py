"""Record the separately approved jar-verified ALTER COLUMN assertions."""
import hashlib
import json
from pathlib import Path
import subprocess

ROOT = Path(r"C:\Work\h2sharp")
OUT = Path(r"C:\Users\User\AppData\Local\Temp\opencode\h2-xml-bcl-prototype-20261025\xml-test-proposal")
name = "tests/H2SharpSql.Specs/Commands/AlterTableAlterColumnCommandTests.cs"
original = subprocess.check_output(["git", "show", "2e560c9:" + name], cwd=ROOT).decode("utf-8")
expected = original.replace("using System;\n", "using System;\nusing System.Collections.Generic;\n")
expected = expected.replace("        Assert.Null(value);", "        // H2 2.5 DROP EXPRESSION removes a generated expression, not an ordinary DEFAULT.\n        Assert.Equal(\"'n/a'\", value);")
expected = expected.replace('        _ = Assert.ThrowsAny<Exception>(() => connection.ExecuteScalar("SELECT NAME FROM TEST"));',
    '        // Invisible columns remain addressable explicitly, but are excluded from SELECT *.\n'
    '        Assert.Equal("Ada", connection.ExecuteScalar<string>("SELECT NAME FROM TEST"));\n'
    '        IDictionary<string, object> row = connection.QuerySingle("SELECT * FROM TEST");\n'
    '        Assert.Single(row);\n'
    '        Assert.Equal(1, row["ID"]);')
assert (ROOT / name).read_text(encoding="utf-8") == expected
mv_name = "tests/H2Sharp.Tests/MVTableTests.cs"
mv_original = subprocess.check_output(["git", "show", "2e560c9:" + mv_name], cwd=ROOT).decode("utf-8")
mv_expected = mv_original.replace('Assert.Contains("CREATE TABLE", table.GetCreateSql(), StringComparison.Ordinal);',
    'Assert.Contains("CREATE MEMORY TABLE", table.GetCreateSql(), StringComparison.Ordinal);')
assert (ROOT / mv_name).read_text(encoding="utf-8") == mv_expected
approval = {
    "user_approval": "Утверждаю оба diff",
    "reason": "H2 2.5.259: DROP EXPRESSION keeps ordinary DEFAULT; INVISIBLE hides from wildcard only",
    "expected_files": {name: hashlib.sha256((ROOT / name).read_bytes()).hexdigest()},
}
path = OUT / "approved-alter-column-test-changes.json"
if path.exists():
    assert json.loads(path.read_text(encoding="utf-8")) == approval
else:
    path.write_text(json.dumps(approval, ensure_ascii=False, indent=2), encoding="utf-8")
print("Exact approved ALTER COLUMN changes checked and recorded")
mv_approval = {
    "user_approval": "Утверждаю diff",
    "reason": "H2 2.5.259 TableBase.getCreateSQL emits CREATE MEMORY TABLE for non-persistent indexes",
    "expected_files": {mv_name: hashlib.sha256((ROOT / mv_name).read_bytes()).hexdigest()},
}
mv_path = OUT / "approved-mv-table-test-change.json"
if mv_path.exists():
    assert json.loads(mv_path.read_text(encoding="utf-8")) == mv_approval
else:
    mv_path.write_text(json.dumps(mv_approval, ensure_ascii=False, indent=2), encoding="utf-8")
print("Exact approved MVTable assertion checked and recorded")

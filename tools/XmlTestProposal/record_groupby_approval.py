"""Record the user's approval for the obsolete GROUP BY position test update."""
import hashlib
import json
from pathlib import Path
import subprocess

ROOT = Path(r"C:\Work\h2sharp")
OUT = Path(r"C:\Users\User\AppData\Local\Temp\opencode\h2-xml-bcl-prototype-20261025\xml-test-proposal")
name = "tests/H2Sharp.Tests/SelectTextTests.cs"
original = subprocess.check_output(["git", "show", "17c9f68:" + name], cwd=ROOT).decode("utf-8")
old = '''    [Fact]
    public void GroupByPositionWaitsForMode()
    {
        // A bare number groups by position only where the mode says so, and the flag is
        // not ported: in regular mode it would group by a constant instead, which is a
        // guess this reader refuses to make either way.
        SessionLocal session = NewSession(out TransactionStore store, out _);
        try
        {
            TODOException e = Assert.Throws<TODOException>(
                () => session.PrepareCommand("SELECT NAME, COUNT(*) FROM T GROUP BY 1"));
            Assert.Contains("groupByColumnIndex", e.Message);
        }
        finally
        {
            store.Dispose();
        }
    }'''
new = '''    [Fact]
    public void GroupByPositionIsConstantInRegularMode()
    {
        // GROUP BY <integer> is a select-list position only in MySQL/MariaDB/PostgreSQL.
        // In regular mode it is the constant 1, so NAME is not grouped.
        SessionLocal session = NewSession(out TransactionStore store, out _);
        try
        {
            H2SharpException e = Assert.Throws<H2SharpException>(
                () => RunText(session, "SELECT NAME, COUNT(*) FROM T GROUP BY 1"));
            Assert.Equal(H2Sharp.Api.ErrorCode.MustGroupByColumn1, e.H2ErrorCode);
        }
        finally
        {
            store.Dispose();
        }
    }'''
assert old in original
expected = original.replace(old, new)
assert (ROOT / name).read_text(encoding="utf-8") == expected
approval = {
    "user_approval": "Утверждаю обновление",
    "reason": "GROUP BY position is mode-dependent in H2 2.5; regular mode keeps 1 as a constant and raises MUST_GROUP_BY_COLUMN_1 (90016). Verified against the pinned jar.",
    "expected_files": {name: hashlib.sha256((ROOT / name).read_bytes()).hexdigest()},
}
path = OUT / "approved-groupby-position-test-change.json"
if path.exists():
    assert json.loads(path.read_text(encoding="utf-8")) == approval
else:
    path.write_text(json.dumps(approval, ensure_ascii=False, indent=2), encoding="utf-8")
print("Approved GROUP BY test change recorded")

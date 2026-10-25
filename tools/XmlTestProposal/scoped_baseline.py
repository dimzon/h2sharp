"""Freeze/check only the two non-native test projects used by the XML work."""
import hashlib
import json
from pathlib import Path
import sys

ROOT = Path(r"C:\Work\h2sharp")
OUT = Path(r"C:\Users\User\AppData\Local\Temp\opencode\h2-xml-bcl-prototype-20261025\xml-test-proposal")
manifest = OUT / "non-native-test-checksums.json"
def checksum(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()
if sys.argv[1] == "freeze":
    files = {}
    for name in ["H2Sharp.Tests", "H2SharpSql.Specs"]:
        directory = ROOT / "tests" / name
        for pattern in ["**/*.cs", "*.csproj"]:
            for path in directory.glob(pattern):
                if not any(part in ["obj", "bin"] for part in path.relative_to(directory).parts):
                    files[path.relative_to(ROOT).as_posix()] = checksum(path)
    if manifest.exists():
        assert json.loads(manifest.read_text(encoding="utf-8")) == files, "Existing scoped baseline differs"
    else:
        manifest.write_text(json.dumps(files, indent=2), encoding="utf-8")
    print("Non-native protected files frozen: " + str(len(files)))
else:
    files = json.loads(manifest.read_text(encoding="utf-8"))
    allowed = {}
    if sys.argv[1] in ("verify-approved-xml", "verify-approved"):
        approval = json.loads((OUT / "approved-xml-test-changes.json").read_text(encoding="utf-8"))
        assert approval["user_approval"] == "Утверждаю XML-diff"
        assert approval["proposal_sha256"] == "a1728ce32cd4942c5ab17f83c61e592f35a86538d436f8773f6cd8d164cf3957"
        assert checksum(OUT / "proposed-xml-tests.patch") == approval["proposal_sha256"]
        allowed = dict(approval["expected_files"])
        assert set(allowed) == {"tests/H2SharpSql.Specs/Functions/XmlFunctionTests.cs",
            "tests/H2SharpSql.Specs/Infrastructure/XmlFunctionAssert.cs"}
        if sys.argv[1] == "verify-approved":
            ddl = json.loads((OUT / "approved-ddl-test-changes.json").read_text(encoding="utf-8"))
            assert ddl["user_approval"] == "Утверждаю diff CYCLE"
            allowed.update(ddl["expected_files"])
            domain = json.loads((OUT / "approved-domain-test-changes.json").read_text(encoding="utf-8"))
            assert domain["user_approval"] == "Утверждаю оба diff"
            allowed.update(domain["expected_files"])
            alter_column_path = OUT / "approved-alter-column-test-changes.json"
            if alter_column_path.exists():
                alter_column = json.loads(alter_column_path.read_text(encoding="utf-8"))
                assert alter_column["user_approval"] == "Утверждаю оба diff"
                assert set(alter_column["expected_files"]) == {"tests/H2SharpSql.Specs/Commands/AlterTableAlterColumnCommandTests.cs"}
                allowed.update(alter_column["expected_files"])
            mv_path = OUT / "approved-mv-table-test-change.json"
            if mv_path.exists():
                mv = json.loads(mv_path.read_text(encoding="utf-8"))
                assert mv["user_approval"] == "Утверждаю diff"
                assert set(mv["expected_files"]) == {"tests/H2Sharp.Tests/MVTableTests.cs"}
                allowed.update(mv["expected_files"])
            groupby_path = OUT / "approved-groupby-position-test-change.json"
            if groupby_path.exists():
                groupby = json.loads(groupby_path.read_text(encoding="utf-8"))
                assert groupby["user_approval"] == "Утверждаю обновление"
                assert set(groupby["expected_files"]) == {"tests/H2Sharp.Tests/SelectTextTests.cs"}
                allowed.update(groupby["expected_files"])
    changed = [name for name, expected in files.items()
        if not (ROOT / name).is_file() or checksum(ROOT / name) != allowed.get(name, expected)]
    changed += [name for name, expected in allowed.items()
        if name not in files and (not (ROOT / name).is_file() or checksum(ROOT / name) != expected)]
    print("Non-native protected files checked: " + str(len(files)))
    if allowed: print("Exact approved XML + DDL changes checked: " + str(len(allowed)))
    print("Unexpected existing test/fixture changes: " + str(len(changed)))
    for name in changed: print(name)
    if changed: sys.exit(1)

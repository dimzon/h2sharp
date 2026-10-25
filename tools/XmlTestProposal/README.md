# Approved XML test proposal

The five assertion changes in `XmlFunctionTests.cs.draft` and the exact helper
`XmlFunctionAssert.cs` were separately approved by the user (`Утверждаю XML-diff`)
and applied to `tests/H2SharpSql.Specs`. Eight SQL/getter calls and six Fact names
remain unchanged. The helper rejects unexpected nodes/data and literal LF with
escapeNewline=TRUE.

The immutable proposal hash is:
`a1728ce32cd4942c5ab17f83c61e592f35a86538d436f8773f6cd8d164cf3957`.

Evidence and scoped baseline files are outside the workspace:

```text
C:\Users\User\AppData\Local\Temp\opencode\h2-xml-bcl-prototype-20261025\xml-test-proposal
```

Use `record_approval.py` to verify the applied files against the accepted draft,
`scoped_baseline.py verify-approved-xml` for the 273 existing core/conformance
source files, and `report_results.py` for name-by-name TRX transitions.
`make_patch.py` is a proposal generator: it refuses to overwrite a different
sealed proposal after applying the approved patch. It is not a baseline updater.

Result summary and commands: `docs/XML_BCL_SQL_IMPLEMENTATION.md`.

Subsequent DDL batches have separately approved changes for CYCLE, DOMAIN and
ALTER COLUMN. `record_alter_column_approval.py` checks the exact two ALTER COLUMN
diffs and one MVTable formatter assertion against commit `2e560c9`; its sealed
approval records are stored in the same external evidence directory. Run
`scoped_baseline.py verify-approved` to check all 273 original protected files
with exactly these six approved XML/DDL/MVTable files. The original manifest is
not regenerated. Current DDL and regression results are in `MIGRATION_STATUS.md`.

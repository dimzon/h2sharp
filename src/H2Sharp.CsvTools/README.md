# H2Sharp.CsvTools

Optional `CSVREAD` and `CSVWRITE` functions for H2Sharp, backed by
[CsvHelper](https://joshclose.github.io/CsvHelper/). The functions are not part of the
engine: a connection registers them before it is opened.

```csharp
using H2Sharp.CsvTools;
using H2Sharp.Data;

using H2Connection connection = new("Data Source=app;Storage=Memory");
((IH2Extensions)connection).UseCsvTools();
connection.Open();
```

```sql
SELECT * FROM CSVREAD('data.csv');
SELECT ID, NAME FROM CSVREAD('data.csv', 'ID,NAME');
SELECT * FROM CSVREAD('data.csv', NULL, 'fieldSeparator=;');
CALL CSVWRITE('out.csv', 'SELECT * FROM T ORDER BY ID');
CALL CSVWRITE('out.csv', 'SELECT * FROM T', 'fieldSeparator=; null=NULL');
```

`CSVREAD` accepts the reference's 1–7 arguments (file, column list, options or charset,
field separator, field delimiter, escape, null string). `CSVWRITE` accepts 2–8 (file,
query, options or charset, then the same positional values and a line separator). Option
names are the reference's: `fieldSeparator`/`fieldSep`, `fieldDelimiter`/`fieldDelim`,
`escape`/`esc`/`escapeCharacter`, `lineComment`/`lineCommentCharacter`,
`lineSeparator`/`lineSep`, `null`/`nullString`, `quotedNulls`, `charset`/`characterSet`,
`preserveWhitespace`, `writeColumnHeader`, `caseSensitiveColumnNames`.

CSVWRITE runs its query through `H2Database.CurrentSessionConnection`: the borrowed
connection of the calling statement's session. The query therefore sees the caller's
uncommitted rows and belongs to the caller's transaction; the function cannot begin,
commit, roll back, enlist or change that transaction, and non-transactional statements are
refused on that connection. A function that uses the connection after it returned gets a
closed connection.

## Differences from the reference's CSV parser

CsvHelper parses the file, so a few behaviors differ from `org.h2.tools.Csv`:

- A quoted empty field and an unquoted empty field are both read as `NULL` (the reference
  keeps the quoted `""`). The null string applies wherever it appears.
- An escape character different from the field delimiter is not combined with quoted
  fields: CsvHelper selects either the RFC 4180 quoting mode or the escape mode.
- Comment characters are line-oriented in CsvHelper; the reference also treats them inside
  a field after leading whitespace.
- Malformed rows are read permissively (missing fields become `NULL`, extra fields are
  dropped, bad data is not an error), as the reference does, but the exact character-level
  recovery is CsvHelper's.
- `lineSeparator` on write defaults to `Environment.NewLine` (the platform default the
  reference uses); `preserveWhitespace` maps to CsvHelper's trim options.

The SQL shape, the argument order, the option names, the header naming, the return value
of CSVWRITE, and the `IO_EXCEPTION_1` error for a missing file follow the reference.

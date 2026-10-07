# java.text.MessageFormat in H2Sharp

H2 formats every SQL error message through `java.text.MessageFormat`. The rules are
easy to get subtly wrong, so `Message/ErrorMessages.cs` is a direct two-phase port of
the JDK's algorithm and is validated against the reference JVM by two differential
tests:

* `MessageFormatOracleTests.Format_MatchesReferenceJvm_Exhaustively` — every pattern
  over the alphabet `{', '{', '}', '0'}` of length 1..4 (340 patterns).
* `MessageFormatOracleTests.Translate_MatchesReferenceJvm_ForEveryCatalogueTemplate` —
  every message template in H2's English catalogue, with three argument sets chosen to
  exercise quoting (≈600 formatted messages). This is the test that matters, because
  real templates are longer and more varied than the short synthetic ones.

## The rules, as observed

**Quoting.** An apostrophe toggles "in quote". Inside a quoted region every character
is literal, braces included. A doubled apostrophe is a literal apostrophe and does not
toggle quoting. An apostrophe that toggles quoting is *not* itself emitted.

| pattern | result |
|---|---|
| `'{0}'` | `{0}` — a quoted brace is literal, not a placeholder |
| `'{0'` | `{0` — the unmatched quote is closed at the end |
| `''` | `'` |
| `''''` | `''` |
| `'` | `` (empty) |
| `'0` | `0` |
| `a'b` | `ab` — a lone apostrophe simply vanishes |
| `'{'{0}'}'` | `{A}` |

The `a'b` case has a visible consequence in H2: message 90146 reads
`Database {0} not found, and IFEXISTS=true, so we can't auto-create it`, and H2
renders it as `... so we cant auto-create it`, without the apostrophe. That is H2's
behaviour and H2Sharp reproduces it.

**Placeholders.**

| pattern | result |
|---|---|
| `{0}` | substitutes argument 0 |
| `{0` | `IllegalArgumentException` — unmatched braces |
| `{` | `IllegalArgumentException` |
| `{}` | `IllegalArgumentException` — empty argument index |
| `{9}` with 3 arguments | the literal text `{9}` — an unavailable argument prints its own placeholder |
| `{1,number}` | unsupported here; H2's catalogue never uses format types |

**Nested braces.** A `{` inside a placeholder opens a nested brace, tracked by a brace
stack. If such a brace is never closed, the pattern is *not* an error but no
placeholder is ever completed, so the raw prefix is emitted and nothing is
substituted:

| pattern | result |
|---|---|
| `{{` | `` (empty) |
| `}{{` | `}` |
| `{0{` | `` (empty) |

whereas a top-level `{` that is never closed *is* an error. This asymmetry is the
subtlest part of the algorithm and is exactly the kind of thing a hand-written
re-implementation gets wrong; hence the exhaustive test.

**There is no `{{` escape.** Braces are quoted with an apostrophe.

## Reproducing

```sh
(cd reference/h2/h2 && mvn -B -q -DskipTests package)
dotnet test --filter FullyQualifiedName~MessageFormat
```

The tests skip themselves when `reference/h2` or a JDK is absent.
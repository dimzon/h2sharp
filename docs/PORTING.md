# Porting rules

This document is the contract for translating H2 (Java) to H2Sharp (C#). It exists so that
the port stays auditable: every non-obvious C# decision must be traceable to a decision here
or to the corresponding Java code.

## 1. Scope

Ported: `api`, `bnf` (only if referenced by ported code), `command`, `compress/LZF`,
`constraint`, `engine`, `expression`, `index`, `mode`, `mvstore`, `result`, `schema`, `store`,
`table`, `util`.

Not ported: `fulltext`, `security`, `server`, `tools`, `jmx`, `jdbc`, `jdbcx`, `bnf` (dead code
in 2.5 — only referenced by the web console, which is out of scope).

The reference revision is pinned in `reference/H2_COMMIT.txt`. H2Sharp targets **H2 2.5.x**.

### Where the parser actually lives

H2 2.x deleted the BNF grammar file and its generated parser. `org.h2.bnf` survives only as
dead code for the web console. The real parser is recursive descent:

| Java | C# |
|---|---|
| `org.h2.command.Parser` (374 KB) | `H2Sharp.Command.Parser` |
| `org.h2.command.Tokenizer` (61 KB) | `H2Sharp.Command.Tokenizer` |
| `org.h2.command.Token` | `H2Sharp.Command.Token` |
| `org.h2.util.ParserUtil` (694 lines of token constants) | `H2Sharp.Command.ParserUtil` |

Do not look for a `.bnf` grammar file. There is none.

## 2. File headers

Every ported file starts with:

```csharp
/*
 * Copyright 2004-2026 H2 Group.
 * Copyright 2026 H2Sharp contributors.
 * Ported from org.h2.<package>.<Name>.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Modified: <one-line summary of what differs from the Java original>
 */
```

The `Modified:` line is mandatory. A ported file whose behaviour is byte-for-byte identical to
Java writes `Modified: none (mechanical port)`.

New files that have no Java counterpart write `Ported from: (new file, no Java counterpart)`.

## 3. Namespaces and type names

* Namespace mirrors the Java package: `org.h2.mvstore` -> `H2Sharp.MvStore`.
* Class names keep the H2 spelling verbatim, including H2's compact forms:
  `MVStore`, `MVMap`, `BTree`, `ValueVarchar`, `CreateTableAsSelect`.
* When .NET already defines a colliding name, rename with an explicit suffix and say so in
  `Modified:` — do not silently pick a different name. See the collision table in §7.

## 4. Primitive mapping

| Java | C# | Note |
|---|---|---|
| `byte` (signed 8-bit) | `sbyte` | **not** `byte`. H2's `DataUtils` packs signed bytes into `long`. |
| `short` | `short` | |
| `char` | `char` | |
| `int` | `int` | |
| `long` | `long` | |
| `float` / `double` | `float` / `double` | Java and C# are both IEEE-754 binary32/64. `StrictMath` -> `Math`. |
| `boolean` | `bool` | |
| `BigDecimal` | `H2Sharp.Value.H2Decimal` | **Not** `System.Decimal`. See §5. |
| `String` | `string` | Both UTF-16. |
| `Object[]`, `Object` | `object?[]`, `object?` | |
| `int[]`, `long[]` | `int[]`, `long[]` | use `IntArray`/`LongArray` like H2 does |
| `char[]` | `char[]` or `ReadOnlySpan<char>` | prefer `ReadOnlySpan<char>` in new code only |

### Integer overflow

`Directory.Build.props` sets `CheckForOverflowUnderflow=false`. Java's `int`/`long` arithmetic
wraps silently and H2 relies on this (MVStore page/offset math, `DataUtils` bit packing,
`VersionedValue` operation codes). Keep the default project-wide rather than sprinkling
`unchecked {}` everywhere; that way a ported expression keeps reading like the Java original.

`Math.multiplyExact`/`addExact` -> `Math.*Checked` stays available where H2 calls it explicitly.

## 5. Types with no direct equivalent

### `BigDecimal` -> `H2Decimal`

`System.Decimal` is 96-bit with no exponent and cannot represent values H2 accepts
(`NUMERIC(38, 10)`, `1E+30`, intermediate scale growth before a final `setScale`). Port
`ValueDecimal` on top of `H2Decimal`: a `BigInteger` unscaled value plus an `int` scale,
reproducing `java.math.BigDecimal` semantics that H2 depends on:

* `add`/`subtract`/`multiply` grow scale and must throw when precision exceeds the configured
  maximum (`SysProperties.MAX_DECIMAL_SCALE`, `MAX_NUMERIC_PRECISION`).
* `divide` uses the H2 rounding mode (`HALF_UP` by default, overridable via `MODE`).
* `stripTrailingZeros`, `toPlainString`, `compareTo`, `equals`/`hashCode` semantics differ
  between Java and .NET and are a common source of silent divergence — port each method
  literally rather than delegating to a .NET analogue.

### Temporal types

| H2 type | C# representation |
|---|---|
| `DATE` | `DateOnly` |
| `TIME` | `TimeOnly` |
| `TIMESTAMP` | `DateTime` (`Kind = Unspecified`) |
| `TIMESTAMP WITH TIME ZONE` | `DateTimeOffset` |
| `TIME / TIMESTAMP WITH TIME ZONE` nanosecond variants | `H2Sharp.Value.TimeNanoseconds`, `TimestampNanoseconds` |

`.NET` ticks are 100 ns; Java `Instant` is 1 ns. Do not round silently — H2 has explicit
nanos columns and `LocalDateTimeUtils` truncates to the target precision. Port that truncation
explicitly and test it.

Java `LocalDateTime` has no time zone; `DateTime` with `Kind=Unspecified` matches. Never use
`Kind=Utc`/`Local` — `LocalDateTimeUtils` assumes UTC throughout.

### `ByteBuffer` -> `Span<byte>`

`java.nio.ByteBuffer` has flip/rewind/compact/limit semantics that H2's `DataUtils` and MVStore
depend on. Do **not** scatter `BinaryPrimitives` calls around; port `H2Sharp.Store.ByteCursor`
with the same `position`/`limit`/`remaining` state machine so the code remains a line-by-line
translation. See `docs/notes/bytebuffer.md` when that file exists.

## 6. Language-level differences to watch

* **Nullability.** Java platform types map to `object?`. SQL `NULL` is a *value*
  (`ValueNull.INSTANCE`), not a null reference — do not unify the two. Only Java `null`
  references become C# `null`.
* **Generics.** Java raw types become generic where H2 uses them generically (`MVMap<TK,TV>`,
  `Cursor<TK,TV>`), otherwise `object?`. Java wildcards map to `in`/`out` only if variance is
  real; otherwise `?`.
* **Iterators.** Java `Iterator` -> `IEnumerator`/`IEnumerator<T>` or, where hot,
  `struct`-backed enumerators. Keep the Java shape unless profiling justifies a change.
* **`synchronized`.** Java `synchronized` methods -> `lock (this)`. Java `ReentrantLock` ->
  `SemaphoreSlim(1,1)` or `Monitor`. MVStore relies on **single-writer** discipline; keep it.
* **Autoboxing.** Java autoboxes `Integer.valueOf`; C# does not. Use explicit conversion and
  cached `static readonly` boxes only where H2 also caches them.
* **Static mutable state.** Java `static` fields are per-classloader; C# statics are per-
  assembly-load. H2Sharp will be loaded once per process in practice; do not introduce new
  mutable statics without saying so in `Modified:`.
* **Exceptions.** Java `RuntimeException` -> `H2SharpApiException`. `ErrorCodeException`
  carries a code from `H2Sharp.Api.ErrorCode`. Stack trace text is not portable; never assert
  on it in tests.
* **String formatting.** `String.format` differs from C# interpolation and from
  `string.Format` on several specifiers. Port number formatting through
  `H2Sharp.Util.NumberFormatter` and validate with golden tests against Java output.
* **Sorting.** Java `Arrays.sort(int[])` is a dual-pivot quicksort (unstable);
  `Array.Sort(int[])` in .NET is introsort (also unstable). Close enough for H2's uses, but
  any place that relies on `Comparable` tie-breaking must be audited — Java's `Collections.sort`
  is stable (TimSort), so **ported code that needs stability must say so**.
* **`Comparable` vs `IComparable<T>`.** Port `compareTo` as `CompareTo(object?)` for value
  types that H2 boxes, `CompareTo(T)` otherwise.

## 7. Known name collisions and resolutions

| H2 name | Problem in C# | Resolution |
|---|---|---|
| `org.h2.store.FileStore` | `System.IO.FileStore` | keep `H2Sharp.Store.FileStore`; add `using H2Store = H2Sharp.Store.FileStore;` where ambiguous |
| `org.h2.util.IOUtils` | none | `H2Sharp.Util.IOUtils` |
| `org.h2.engine.Database` | none | `H2Sharp.Engine.Database` |
| `org.h2.value.Value` | `System.Data.Value` | `H2Sharp.Value.Value` |
| `org.h2.result.ResultInterface` | none | keep name |
| `Expression` | `System.Linq.Expressions.Expression` | `H2Sharp.Expression.Expression`; never `using System.Linq.Expressions` in ported files |
| `Index` | generic concept | `H2Sharp.Index.Index` |
| `Trigger` | `System.ICloneable` family | `H2Sharp.Api.Trigger` |
| `Cache` | `System.Runtime.Caching` | `H2Sharp.Util.Cache` |

## 8. What "done" means for a ported unit

A Java class may be marked ported only when:

1. All public and package-private members are present, with Java names preserved.
2. Behaviour is identical for every input, including overflow, `null`, empty string and
   boundary values.
3. Error paths throw the H2-equivalent exception with the matching `ErrorCode`.
4. The Java Javadoc comment is carried over, including `@param`/`@return`.
5. Unit tests exist that were derived from the H2 test suite or from a documented reading of
   the Java implementation.

## 9. Tests

Two harnesses, both required:

* **Unit** — `tests/H2Sharp.Tests`. Port `h2/src/test/org/h2/test/Test*.java` where practical.
  Note that H2 has **no unit tests for the parser at all**: `Parser.java` has none, and
  `test/coverage/Tokenizer.java` and `test/trace/Parser.java` are helpers that merely
  share the name. The parser is covered only indirectly, by 320 `.sql` scripts run
  through `TestScript`. Differential testing against the reference JVM is therefore the
  only oracle available there.
* **Conformance** — `tests/H2Sharp.Conformance`. Runs `.sql` scripts. A script and its expected
  output are run against **both** H2Sharp and the reference Java H2; the outputs are normalised
  and compared. This is the primary oracle for the port, because it tests behaviour rather than
  the transliteration.

The reference Java checkout lives in `reference/h2/` and is git-ignored. Populate it with:

```sh
git clone https://github.com/h2database/h2database.git reference/h2
git -C reference/h2 rev-parse HEAD > reference/H2_COMMIT.txt
```

Clone the pinned revision rather than master when reproducing a run, otherwise the oracle has
drifted underneath the tests and the diff means nothing. CI does this explicitly.

Conformance tests that need the Java sidecar must skip (not fail) when `reference/h2` is absent.
That default is what makes a green run easy to over-read, so the conformance job in CI sets
`H2SHARP_REQUIRE_ORACLE=1`, which turns an unusable oracle into a failure. Environment variables:

| Variable | Effect |
|---|---|
| `H2SHARP_REFERENCE_ROOT` | Explicit path to `reference/h2` instead of walking up from the test binaries. |
| `H2SHARP_REQUIRE_ORACLE` | An unavailable oracle fails instead of skipping. Set it in the job that is supposed to prove Java compatibility. |

### How generators are written

Hard rule, learned the hard way: **one language per pipeline stage, and no intermediate
format between two languages.**

| Stage | Language | Why |
|---|---|---|
| Reading H2's Java | Java, via `JavacTask` and `com.sun.source` | The JDK already contains a complete Java parser. Reading Java with regular expressions means guessing where a comment block starts, and a wrong guess writes a plausible, wrong token table without any error |
| Writing C# | the same Java tool, emitting text | The facts (names, values, javadoc) and the template live in one place instead of being split between a dumper and a formatter that can disagree |
| Laying the C# out | `dotnet format`, driven by `.editorconfig` | Layout belongs to the formatter. A generator that also does pretty-printing becomes a second, worse pretty printer |

Concretely, `tools/PortTables.java` does all of it: parse, read the javadoc through
`DocTrees.getDocCommentTree`, emit `src/H2Sharp/Util/ParserUtil.cs` and
`src/H2Sharp/Command/Token.cs`. `regen.py` only orchestrates (compile it, run it, format,
diff); it never touches the contents of a table.

Two things this setup caught that a script would not have:

* An identifier scanner that starts on `isLetter` misses a leading underscore, so `_ROWID_`
  was split and came out as `_ParserUtil.Rowid`. Use `Character.isJavaIdentifierStart`.
* The keyword table is keyed by the text in a javadoc (`The token "ALL".`) while the
  declaration order comes from the source. Reading one from the AST and scraping the other
  would let them disagree; reading both from the AST makes that impossible.

#### Why not serialise the AST to JSON and generate from Roslyn?

It was evaluated and rejected. It needs the same Java extraction step, plus a schema, plus
a C# reader, plus Roslyn's `SyntaxFactory` and `Formatter` with an `AdhocWorkspace` to lay
the result out. That is strictly more machinery for the same output, and it reintroduces
exactly the two-tool split the rule above exists to prevent: a dumper in one language and an
emitter in another, joined by a format that has to be kept in sync with both sides.

Roslin earns its place when the job is *analysis or transformation of logic* rather than
copying a table, for example porting method bodies automatically. If we ever get there, a
shared structured IR is the right shape and the split is justified, because one
transpiler has to read Java semantics and write C# semantics. For constants it is not, and
the trigger to revisit this decision is written down rather than left to memory.

### Generated tables
Five files under `src/H2Sharp` are generated and must never be edited by hand:
`Api/ErrorCode.cs`, `Message/ErrorCodeKind.cs`, `Util/ParserUtil.cs`, `Command/Token.cs` and
`Util/CaseFolding.cs`. Regenerate all of them with:

```sh
python tools/regen.py            # rewrite in place
python tools/regen.py --check    # rewrite, then fail if anything changed
```

`--check` compares the working tree against HEAD, so it is only meaningful on a clean tree;
that is how CI uses it. It needs a JDK and a built reference jar, because the point of the
`CaseFolding` table is to read Java's Unicode *full* case mapping from the reference JVM and
diff it against what .NET does.

Two traps in that pipeline, both of which produced wrong output before they were found:
* `tools/CaseProbe` must **not** set `InvariantGlobalization`. Under invariant globalization
  the runtime uses its own built-in casing table instead of the platform one, and code points
  the platform maps look unmapped. The probe has to observe the same tables the library runs on.
* A guard threshold that skips the generated table (see `StringUtils.LowerOverrideThreshold`)
  must stay at or below the lowest key in that table. `CaseFoldingOverrideThresholdsAreExact`
  in `StringUtilsAllocationTests` fails if a regeneration breaks that.

### Authoring Java tests

Some behaviour has no reference test to port, so the Java side has to be written by us.
Keep those files in `tests/H2Sharp.Tests/Java/`, compiled and run against the built jar
through the existing `OracleRunner`; JUnit is not needed for a class with a `main` that
exits non-zero on failure. Every such file gets a paired .NET test, and the pairing is
the point: the Java file documents what the reference does, the .NET file asserts we do
the same.

Exhaustive or generated differential tests (every token x every flag combination, every
code point) stay in the .NET test project and are never proposed upstream: they are ours
to regenerate, and their runtime would be a burden in H2's own CI. Only small, cheap,
upstream-shaped regression tests are candidates for a pull request, and that decision is
made per test, not per repository.

## 10. When a hand-written mechanism may be deferred

A hand-written stand-in for an incompatible standard mechanism is a maintenance liability, but
not all of them cost the same to defer. The rule:

> **Deferring is only free where the wrong answer is never persisted.**

| Kind | Free to defer? | Why |
|---|---|---|
| Comparison or lookup whose result is never stored (collation, prefix matching) | yes | Replacing it later touches one method |
| Anything that feeds persisted metadata (identifier normalisation, stored keys) | no | Already-written databases keep the old normalisation; fixing it later means a migration |
| Anything that emits externally visible artefacts (URLs, signatures, wire bytes) | no | Already-issued artefacts cannot be reissued |

So when a gap is found, the first question is not "can we approximate this" but "does the
approximation get written down". If it does, build it properly now, even if the mechanism is
invasive, and back it with a differential test. If it does not, approximate, pin the
difference with an explicit test, and record in `MIGRATION_STATUS.md` what would trigger a
revisit.

Two examples already decided:

* `startsWithIgnoringCase` — deferred, because the result only feeds comparison. Bringing in
  ICU was measured and would not help: `CompareInfo.Compare` and
  `CompareInfo.GetStringComparer` agree with the approximation on every probed pair, so the
  gap is in the platform's collation weights, not in a missing API.
* `ToUpperEnglish` / `ToLowerEnglish` — not deferred, because identifier names reach page
  metadata. Hence the generated `Util/CaseFolding.cs`.

## 11. Deliberate non-port

Where a Java construct is clearly a Java-ism with no behavioural content, replace it rather
than transliterate — but only after confirming no observable difference, and record it in the
file's `Modified:` line. Examples already used:

* `ByteStack` -> `Span<byte>`-based cursor (decided in the `H2Sharp.Store.ByteCursor` work item).
* `StringUtils.toUpperEnglish` -> `char.ToUpperInvariant`.
* Java `Iterator` over an `ArrayList` that is only read -> `for` over a span where the Java
  code is obviously a linear scan.
# H2Sharp.Hashes

Optional hash providers for H2Sharp. The BCL has no SHA-224, and SHA3-224 needs a package; this
one supplies the `Func<HashAlgorithm>` factories that H2Sharp's `HASH` function uses. The core
engine keeps the SQL parsing and the UDF mechanism but does not take a hash-provider dependency.

```csharp
using H2Sharp.Data;
using H2Sharp.Hashes;

using H2Connection connection = new("Data Source=app;Storage=Memory");
((IH2Extensions)connection).UseH2Hashes();      // SHA-224 and SHA3-224 factories
connection.Open();
```

```sql
SELECT HASH('SHA-224', X'616263');
```

`UseH2Hashes(overrideBcl: true)` also replaces the BCL factories for the algorithms the package
knows, when an application deliberately wants one implementation for every name. The default is
`false`: standard names keep their standard providers and only the missing algorithms are added.

Factories are registered through the connection's extension surface before `Open()`, following
the ADR-001 contract for optional .NET extensions.

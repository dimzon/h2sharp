# H2Sharp.JavaLegacy

Optional Java-compatibility behavior for H2Sharp, kept out of the core because it changes SQL
results deliberately. It contains two pieces:

* `JavaRandom` — the JDK's random sequence for `RAND(seed)`, so a script that expects the JVM's
  numbers gets the same ones on .NET;
* `ORA_HASH` — the Oracle-compatible hash function as an ordinary H2Sharp UDF.

```csharp
using H2Sharp.Data;
using H2Sharp.JavaLegacy;

using H2Connection connection = new("Data Source=app;Storage=Memory");
((IH2Extensions)connection).UseJavaLegacy();
connection.Open();
```

```sql
SELECT RAND(42);
SELECT ORA_HASH('value');
```

Without this package `RAND(seed)` uses .NET's random generator; the sequence is H2Sharp's own and
not the JVM's. Registering the package makes the sequence match the reference engine, which is
what conformance scripts and migrated applications need. As with the other optional packages,
registration happens on a closed, not disposed, connection before `Open()`.

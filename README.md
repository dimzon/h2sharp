# H2Sharp

Embeddable SQL engine for .NET, ported from [H2 Database](https://github.com/h2database/h2database).

> **Status: in progress — a usable subset runs end to end.** An ADO.NET provider over an
> in-memory engine (DDL, DML, `SELECT` with `GROUP BY`/`UNION`/aggregates, parameters) is
> working and tested against the reference H2 on the JVM. `JOIN`, subqueries, window
> functions, constraints/rights and file databases are not ported yet, and many commands
> refuse with `TODOException` rather than answering wrongly. See
> [docs/ROADMAP.md](docs/ROADMAP.md) for the per-phase status and
> [MIGRATION_STATUS.md](MIGRATION_STATUS.md) for the working notes.

## Goals

* Embedded, in-memory and file-based relational database engine.
* Public API as a standard **ADO.NET** provider (`System.Data.Common`), so it works with Dapper,
  `System.Data.Common` consumers and EF Core.
* **MVStore** persistence ported faithfully enough that a database file produced by H2Sharp
  can be opened by the reference Java implementation, and vice versa.

## Non-goals

* Server/TCP mode and the H2 web console.
* Full-text search.
* Encrypted databases.

## Related projects

* `H2Sharp.EntityFrameworkCore` — an Entity Framework Core provider for this engine, in a
  sibling checkout (`../h2sharp-efcore`, branch `h2-provider`). It is a fork of the
  EFCore.PG provider with PostgreSQL's surface replaced by H2's: the type mappings, the
  migrations SQL generator, the connection and the DDL are H2's, and the SQL it generates is
  checked against the reference engine on the JVM rather than against this port. It does not
  work against the port yet: what it needs from the engine is listed in its
  `docs/DIVERGENCES.md` — identity DDL, `INFORMATION_SCHEMA`, `CREATE SEQUENCE`, `JOIN`, and
  the `AS` in front of a table alias.

* `H2Sharp.Linq2Db` — a [linq2db](https://github.com/linq2db/linq2db) provider for this
  engine, in a sibling checkout (`../h2sharp-linq2db`). It is written against linq2db's public
  API and against the engine's public API, and it speaks H2: the identifiers are always quoted,
  the types are H2's, the paging is `LIMIT`/`OFFSET`, the functions of H2 are offered as
  `H2SharpSpecificExtensions`, and the catalogue is read from `INFORMATION_SCHEMA` the way
  H2 2.5 keeps it. What it still needs from the engine is listed in its
  `docs/DIVERGENCES.md` — identity DDL, `CREATE SEQUENCE`, `CREATE ALIAS`, `COMMENT ON`,
  foreign keys in DDL, `OVER`, and the `LIMIT` with a number in a parameter.

## Building

```sh
dotnet build H2Sharp.sln
dotnet test
```

## Arrays with ADO.NET and Dapper

H2 array columns use `INTEGER ARRAY`, `VARCHAR(20) ARRAY`, or a bounded type such as
`INTEGER ARRAY[10]`. Array parameters accept CLR arrays, lists, and `IEnumerable` values;
enumerables are consumed once per execution. `string` and `byte[]` remain text and binary
parameters. Use jagged arrays for nested SQL arrays.

```csharp
using var connection = new H2Sharp.Data.H2Connection("jdbc:h2:mem:arrays");
connection.Open();
connection.Execute("CREATE TABLE T(ID INT PRIMARY KEY, NUMBERS INTEGER ARRAY)");

var parameters = new Dapper.DynamicParameters();
parameters.Add("id", 1);
parameters.Add("numbers", new List<int> { 10, 20 }, DbType.Object);
connection.Execute("INSERT INTO T VALUES (@id, @numbers)", parameters);
int[] numbers = connection.QuerySingle<int[]>("SELECT NUMBERS FROM T WHERE ID = 1");
```

The example uses `Dapper`, `System.Data`, and `System.Collections.Generic`. Explicit
`DbType.Object` sends one native array parameter. Dapper's ordinary `IN @ids` list expansion
also works.

`DbDataReader.GetValue()` returns typed arrays: `int[]`, `string[]`, `Guid[]`, etc.
Numeric arrays containing NULL elements return nullable arrays such as `int?[]`;
a NULL column returns `DBNull.Value`, and an empty array remains empty. `GetFieldType()`
reports the non-nullable element's array type independently of row contents.
Use `GetFieldValue<int?[]>()` for a consistent explicit type across rows. Reading a NULL
element into `int[]` throws `InvalidCastException`.

For Dapper properties declared as nullable arrays, configure its typed-reader path once:

```csharp
Dapper.SqlMapper.AddTypeMap(typeof(int?[]), DbType.Object, useGetFieldValue: true);
```

This also allows nullable arrays to be sent as one parameter from an anonymous parameter
object. Without this mapping, Dapper's default materializer directly casts `GetValue()`.

## Regular expressions

`REGEXP`, `~`, `~*`, `!~`, and `!~*` use **.NET regular-expression syntax** and
`System.Text.RegularExpressions`. This is a deliberate project decision. SQL operator
syntax and precedence follow H2 2.5; patterns are not translated from Java syntax.

`~` searches for a match, `~*` ignores case, and `!~` / `!~*` negate the match.
Inline options such as `(?m)`, `(?s)`, and `(?x)` are supported. NULL operands produce
NULL; an invalid pattern raises H2 error `22025`.

```sql
SELECT 'abc' ~ '^a', 'ABC' ~* '^a';
SELECT ID FROM TOPICS WHERE TITLE ~* '(?m)^release' ORDER BY ID;
```

## Licensing

H2 is dual-licensed under the Mozilla Public License 2.0 and the Eclipse Public License 1.0.
H2Sharp is a derivative work and is offered under the same terms; see
[LICENSE-H2.txt](LICENSE-H2.txt) and [docs/PORTING.md](docs/PORTING.md).

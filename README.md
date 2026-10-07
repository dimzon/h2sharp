# H2Sharp

Embeddable SQL engine for .NET, ported from [H2 Database](https://github.com/h2database/h2database).

> **Status: early development.** The port is in progress; see [docs/ROADMAP.md](docs/ROADMAP.md)
> for what is implemented and what is not. Do not use this in production yet.

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

## Building

```sh
dotnet build H2Sharp.sln
dotnet test
```

## Licensing

H2 is dual-licensed under the Mozilla Public License 2.0 and the Eclipse Public License 1.0.
H2Sharp is a derivative work and is offered under the same terms; see
[LICENSE-H2.txt](LICENSE-H2.txt) and [docs/PORTING.md](docs/PORTING.md).
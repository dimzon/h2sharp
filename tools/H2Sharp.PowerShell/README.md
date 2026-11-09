# H2Sharp.PowerShell

PowerShell module for H2Sharp. It provides `Invoke-H2Query`, modeled after
`Invoke-SqliteQuery` from the PSSQLite module.

## Build

```sh
dotnet build tools/H2Sharp.PowerShell/H2Sharp.PowerShell.csproj
```

## Import

```powershell
Import-Module ./artifacts/bin/H2Sharp.PowerShell/debug/H2Sharp.PowerShell.psd1
```

## Cmdlet: Invoke-H2Query

Executes SQL against an H2Sharp database and returns results as PowerShell objects.

### Parameters

| Parameter | Aliases | Description |
|---|---|---|
| `-Query` | `-Sql` | SQL statement or batch to execute. Mandatory. |
| `-Database` | `-DataSource`, `-Path` | Database file path or in-memory key. Default: `test`. |
| `-ConnectionString` |  | Full H2Sharp connection string. Overrides `-Database`/`-Storage`. |
| `-Storage` |  | `File` or `Memory`. Default: `File`. |
| `-Parameters` |  | Hashtable of named parameters. Use `$name` in SQL. |
| `-As` |  | Output format: `PSObject`, `DataTable`, `SingleValue`, `None`. Default: `PSObject`. |
| `-NonQuery` |  | Execute as non-query and return affected row count. |
| `-InputFile` |  | Read SQL from a file. |

### Examples

Basic query:

```powershell
Invoke-H2Query -Query 'SELECT 1 AS x'
```

Query and select properties:

```powershell
Invoke-H2Query -Query 'SELECT 1 AS x, 2 AS y' | Select-Object x, y
```

Use an in-memory database:

```powershell
Invoke-H2Query -Query 'SELECT 1 AS x' -Database shell -Storage Memory
```

Use a connection string:

```powershell
Invoke-H2Query -Query 'SELECT 1 AS x' -ConnectionString 'Data Source=demo;Storage=Memory'
```

Named parameters:

```powershell
Invoke-H2Query -Query 'SELECT $a + $b AS sum' -Parameters @{ a = 1; b = 2 }
```

Return a DataTable:

```powershell
$table = Invoke-H2Query -Query 'SELECT 1 AS x, 2 AS y' -As DataTable
$table.Rows
```

Return a single value:

```powershell
Invoke-H2Query -Query 'SELECT 1' -As SingleValue
```

Execute DDL/DML:

```powershell
Invoke-H2Query -Query 'CREATE TABLE t(a INT)' -NonQuery
Invoke-H2Query -Query 'INSERT INTO t VALUES (1),(2)' -NonQuery
Invoke-H2Query -Query 'SELECT * FROM t'
```

Read SQL from a file:

```powershell
Invoke-H2Query -InputFile .\script.sql
```

Use a file database:

```powershell
Invoke-H2Query -Query 'SELECT 1 AS x' -Database ./test.db
```

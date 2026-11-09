# H2Sharp.Shell

Console client for H2Sharp, similar to `sqlite3`. It can run SQL from the command line or
interactively.

## Build

```sh
dotnet build tools/H2Sharp.Shell/H2Sharp.Shell.csproj
```

## Run

```sh
dotnet run --project tools/H2Sharp.Shell -- [options] [dataSource]
```

After publishing/building, the executable is `H2Sharp.Shell`.

## Options

| Option | Description |
|---|---|
| `-cs`, `--connection-string <value>` | Full H2Sharp connection string. |
| `-d`, `--data-source <value>` | Database file path or in-memory key. |
| `--storage Memory\|File` | Storage kind. Default: `File`. |
| `-sql`, `--sql <statements>` | Execute SQL statements and exit. |
| `-list`, `--list` | Enable result list mode. |
| `-h`, `--help`, `-?` | Show help. |

Positional argument is treated as the data source when no connection string is given.
Default data source: `test`, storage `File`.

## Interactive commands

SQL statements must end with `;`. Commands are case-insensitive.

| Command | Description |
|---|---|
| `help`, `?` | Show help. |
| `list` | Toggle table/list result mode. |
| `maxwidth <n>` | Set maximum column width. Default: `100`. |
| `history` | Show last 20 statements. |
| `<number>` | Re-run a statement from history. |
| `exit`, `quit` | Close connection and exit. |

## Examples

Run one query:

```sh
dotnet run --project tools/H2Sharp.Shell -- -sql "SELECT 1"
```

Run several statements:

```sh
dotnet run --project tools/H2Sharp.Shell -- -sql "CREATE TABLE t(a INT); INSERT INTO t VALUES (1),(2); SELECT * FROM t;"
```

Open a file database:

```sh
dotnet run --project tools/H2Sharp.Shell -- test.db
```

Use an in-memory database:

```sh
dotnet run --project tools/H2Sharp.Shell -- -d shell --storage Memory -sql "SELECT 1"
```

Use a full connection string:

```sh
dotnet run --project tools/H2Sharp.Shell -- -cs "Data Source=demo;Storage=Memory" -sql "SELECT 1"
```

Interactive session:

```text
$ dotnet run --project tools/H2Sharp.Shell
sql> SELECT 1 AS x;
X
1
(1 row, 12 ms)
sql> list
Result list mode is now on
sql> SELECT 1 AS x, 2 AS y;
X: 1
Y: 2
(1 row, 8 ms)
sql> history
#1: SELECT 1 AS x, 2 AS y;
#2: SELECT 1 AS x;
sql> 2
SELECT 1 AS x;
X
1
(1 row, 5 ms)
sql> exit
Connection closed
```

Enable list mode from the command line:

```sh
dotnet run --project tools/H2Sharp.Shell -- -list -sql "SELECT 1 AS a, 2 AS b"
```

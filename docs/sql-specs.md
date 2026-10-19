# Conformance-набор SQL-поверхности H2 (947 тестов)

`tests/H2SharpSql.Specs` — 947 тестов, проверяющих документированные конструкции SQL из
замороженного снимка документации в `docs/`. Набор перенесён из репозитория-донора
`h2sharp-sql` (заморожен) вместе со снимком; дальше тесты живут здесь.

Каждый тест привязан к **строке** документации: `<summary>docs/commands.html:1704 — ...</summary>`
над методом, и несёт маркер вида `// CMD-1704`. Одна документированная конструкция — один тест.

## Как запускать

Проект намеренно **не входит в `H2Sharp.sln`**: пока порт не дозрел, набор показывает сотни
падений, и ритуал `dotnet test H2Sharp.sln` от него не зависит. Запуск — явной командой:

    dotnet test tests/H2SharpSql.Specs/H2SharpSql.Specs.csproj

## Устройство

| Каталог | Документ-источник | Тестов |
|---|---|---:|
| `Commands/` | `docs/commands.html` | 254 |
| `Functions/` | `docs/functions(.aggregate,-window).html` | 325 |
| `Grammar/` | `docs/grammar.html` | 263 |
| `Datatypes/` | `docs/datatypes.html` | 66 |
| `SystemTables/` | `docs/systemtables.html` | 37 |
| `Dml/` | `docs/commands.html` (SELECT-выражения) | 2 |

`Infrastructure/TestDatabase.cs` — единственная точка подключения к БД: `Open()` открывает
новую пустую базу `jdbc:h2:mem:<guid>` на каждый вызов, поэтому тесты изолированы друг от
друга и порядок запуска не важен.

## Baseline

Первый прогон 2026-10-19, до дальнейших изменений порта: **102 из 947** прошли.

| Область | Прошло | Всего |
|---|---:|---:|
| `Commands/` | 11 | 254 |
| `Functions/` | 33 | 325 |
| `Grammar/` | 46 | 263 |
| `Datatypes/` | 10 | 66 |
| `SystemTables/` | 0 | 37 |
| `Dml/` | 2 | 2 |

Крупнейшие пробелы по числу падений: `EXTRACT` в парсере (66), `FILTER`/`OVER` (35),
`INFORMATION_SCHEMA` как таблица (25), `CREATE DOMAIN` (15), `ALTER TABLE` за пределами
`ADD`/`DROP CONSTRAINT` (12), `YEAR` (12), производные таблицы — `Table "("` (12),
`CREATE ROLE` (11), `HOUR` (10), ограничения таблицы кроме PRIMARY KEY (9),
`CREATE SEQUENCE` (8).

Расхождение версий документации (2.5.252) и эталонного чек-аута (2.5.259-SNAPSHOT,
`reference/H2_COMMIT.txt`) описано в `docs/README.md`: при разногласии источником истины
считается эталон, а не снимок.

# Conformance-набор SQL-поверхности H2 (952 теста)

`tests/H2SharpSql.Specs` — 952 теста, проверяющих документированные конструкции SQL из
замороженного снимка документации в `docs/`. Набор перенесён из репозитория-донора
`h2sharp-sql` (заморожен) вместе со снимком; дальше тесты живут здесь.

Каждый тест привязан к **строке** документации: `<summary>docs/commands.html:1704 — ...</summary>`
над методом, и несёт маркер вида `// CMD-1704`. Одна документированная конструкция — один тест.

## Как запускать

Проект намеренно **не входит в `H2Sharp.sln`**: пока порт не дозрел, набор показывает сотни
падений, и ритуал `dotnet test H2Sharp.sln` от него не зависит. Запуск — явной командой:

    dotnet test tests/H2SharpSql.Specs/H2SharpSql.Specs.csproj

## Прогон 2026-10-26

Текущее рабочее дерево после `b4ef56c` и D-004 cleanup.

* Полный набор: **803 / 952**, 149 падений.
* Зелёный прогон `--filter "Category!=BlockedByPort"`: **801 / 922**, 121 падение.
* `BlockedByPort`: **30** тестов; из них 12 прежних (коллатор, `SET PASSWORD`/`SET SALT`,
  CSV/FILE, memory/DB_OBJECT_SQL, ESTIMATED_ENVELOPE) и 18 D-004:
  8 user/role-команд и 10 `PrivilegeCommandTests`. `INFORMATION_SCHEMA.USERS/ROLES/
  RIGHTS/TABLE_PRIVILEGES/COLUMN_PRIVILEGES` не блокируются: на пустой базе они
  возвращают 0 строк; их тесты пока красные только из-за непортированных
  `CREATE USER`/`CREATE ROLE`/`GRANT`.

| Область | Прошло (полный) | Всего | Прошло (без BlockedByPort) | Всего |
|---|---:|---:|---:|---:|
| `Commands/` | 166 | 259 | 166 | 237 |
| `Functions/` | 304 | 325 | 302 | 317 |
| `Grammar/` | 243 | 263 | 243 | 263 |
| `Datatypes/` | 65 | 66 | 65 | 66 |
| `SystemTables/` | 23 | 37 | 23 | 37 |
| `Dml/` | 2 | 2 | 2 | 2 |

Причины 121 зелёного падения: 69 — синтаксис/непрочитанная конструкция, 31 — явный
`TODOException` (не портировано), 11 — отсутствующий объект/функция, 5 — расхождение
значений, остальное — отдельные conversion/parameter/collation-дефекты.

Крупнейшие оставшиеся классы (без `BlockedByPort`):

| Класс | Падений | Что это |
|---|---:|---|
| `InformationSchemaMoreTests` | 10 | `USERS/ROLES/RIGHTS/...` с непортированным D-004 setup, `ROUTINES`, `TRIGGERS`, `SESSIONS` |
| `ExpressionTests` | 8 | `FIELD`, `WITHIN GROUP`, time zone, прочие выражения |
| `DropObjectCommandTests` | 7 | `DROP AGGREGATE/ALIAS/CONSTANT/MATERIALIZED VIEW` и др. |
| `StatementGrammarTests` | 6 | непрочитанные формы DDL/DML |
| `CreateTriggerCommandTests` | 6 | triggers |
| `DateTimeFunctionTests` | 6 | `TO_CHAR`, `DAY_OF_WEEK`, `PARSE_DATETIME` |
| `CreateLinkedTableCommandTests` | 5 | linked tables |
| `ScriptCommandTests` | 5 | SCRIPT-опции |
| `CreateRoutineCommandTests` | 4 | routines/aliases |
| `MaintenanceCommandTests` | 4 | maintenance |
| `CreateTableCommandTests` | 4 | CREATE TABLE |
| `InformationSchemaTests` | 4 | info-schema columns/privileges |
| остальные | 52 | `RUNSCRIPT`, materialized views, window/table functions, crypto, grammar и др. |

`AggregateMiscTests` теперь **35/35**: перенесены `RANK`, `DENSE_RANK`, `PERCENT_RANK`,
`CUME_DIST`. Для `CUME_DIST(2)` на `1,2,3` ожидание исправлено на `0.75`: H2 2.5 включает
гипотетическую строку в популяцию (проверено эталонным jar).

Оконные функции в conformance закрыты: `WindowFunctionTests` **18/18**,
`WindowGrammarTests` **10/10**, `QUALIFY` проходит. `NTH_VALUE ... FROM LAST` читается в
эталонной форме — `FROM LAST` после закрывающей скобки; вариант внутри скобок H2 2.5.259
отвергает.

Приоритет на следующий заход: закрыть 69 syntax-дефектов (это не «не портировано», а
неполный парсер), затем 31 явный TODO. D-004-команды users/roles/privileges вынесены в
`BlockedByPort`; INFORMATION_SCHEMA-представления прав остаются активными и возвращают
пустые данные.

## Устройство

| Каталог | Документ-источник | Тестов |
|---|---|---:|
| `Commands/` | `docs/commands.html` | 259 |
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

## Исторический прогон 2026-10-25 (947 тестов)

Полный набор, коммит `8e78f98`: **673 из 947** прошли. Двенадцать падений помечены
`Category=BlockedByPort` (утверждённые/зафиксированные пробелы порта), поэтому зелёный
прогон — `--filter "Category!=BlockedByPort"`: 673 из 935, 262 падения.

| Область | Прошло | Всего |
|---|---:|---:|
| `Commands/` | 98 | 254 |
| `Functions/` | 257 | 325 |
| `Grammar/` | 230 | 263 |
| `Datatypes/` | 65 | 66 |
| `SystemTables/` | 21 | 37 |
| `Dml/` | 2 | 2 |

Крупнейшие оставшиеся пробелы: builtin-функции в парсере (37), `CREATE DOMAIN/TYPE`
(27), `CREATE ROLE` (13), `ALTER TABLE` за пределами `ADD/DROP CONSTRAINT` (23),
`SCRIPT`/`RUNSCRIPT`/`BACKUP` (14), `CREATE USER` (9), `CREATE TRIGGER` (8), `CREATE
ALIAS` (7), `CREATE MATERIALIZED VIEW` (5), `CREATE CONSTANT` (4), оконные агрегаты с
`WITHIN GROUP` (около 8).

Пять падений — не «не портировано», а расхождения в поведении, их стоит разобрать:
`SET ... DEFAULT`, `FETCH ... WITH TIES`, `INFORMATION_SCHEMA.SESSIONS`,
`DAY_OF_WEEK()` и регистрация внешнего ключа в `TableConstraintDefinition`.

BlockedByPort (12): коллатор (`SET COLLATION` ×2), `SET PASSWORD`/`SET SALT`,
CSV/FILE-функции (`CSVWRITE`, `FILE_READ`, `FILE_WRITE`), `MEMORY_FREE`/`MEMORY_USED`,
`DB_OBJECT_SQL`/`DATA_TYPE_SQL` для домена, `ESTIMATED_ENVELOPE`.

## Batch A: итог после согласованных R1–R3 и локальной J

Полный набор: **696 из 947** прошли, 251 падение. Без BlockedByPort:
**696 из 935**, 239 падений. Исходный baseline 673/947 не заменён.

| Область | Прошло | Всего |
|---|---:|---:|
| `Commands/` | 98 | 254 |
| `Functions/` | 280 | 325 |
| `Grammar/` | 230 | 263 |
| `Datatypes/` | 65 | 66 |
| `SystemTables/` | 21 | 37 |
| `Dml/` | 2 | 2 |

Перенесены GCD/LCM, GCD_AGG/LCM_AGG, JSON_OBJECT/JSON_ARRAY/FORMAT JSON и
ENVELOPE. Эталонный разбор A0 также закрыл MODE ×3, LISTAGG ON OVERFLOW ×2 и
JSON_ARRAYAGG ORDER BY. Всего по именам: 23 перехода Failed → Passed,
Passed → Failed — 0, пропавших/пропущенных тестов — 0.

Девять исходных JSON-тестов сохранили SQL, QuerySingle<string> и ожидания;
изменено только подключение отдельно утверждённой локальной Dapper-фикстуры.
Каждый тест использует свою копию Dapper в AssemblyLoadContext. Default mapper,
общий TestDatabase.Open и производственный ADO.NET mapping не меняются.

Все исходные 1268 H2Sharp.Tests прошли; отдельно 29 новых проверок
(5 Batch A oracle + 24 JSON/Dapper-контрактных). Подробности:
[REGRESSION_BATCH_A_INVESTIGATION.md](REGRESSION_BATCH_A_INVESTIGATION.md).

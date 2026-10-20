# Identity-колонки и генерируемые колонки

Заметка — план среза и карта эталонных мест. Сделана, чтобы источники не перечитывать:
всё, что нужно, собрано здесь, а ссылки на файлы эталона ведут к конкретным методам.

Эталон: H2 2.5.259-SNAPSHOT (`reference/h2`), коммит зафиксирован в
`reference/H2_COMMIT.txt`.

## Что в этом срезе

* `GENERATED [ALWAYS | BY DEFAULT] AS IDENTITY [(sequence options)]` — identity-колонка;
* `AS (expr)` / `AS expr` — генерируемая колонка (значение считается движком);
* `DEFAULT expr` и `DEFAULT … ON NULL` — умолчание;
* `ON UPDATE expr` — значение при обновлении;
* значение identity при вставке (из последовательности колонки);
* identity-поля в `INFORMATION_SCHEMA.COLUMNS`.

Вне среза (отдельными срезами): `ALTER TABLE … ALTER COLUMN … SET/DROP IDENTITY`,
`TRUNCATE TABLE … RESTART/CONTINUE IDENTITY`, `CREATE SEQUENCE` в DDL и
`INFORMATION_SCHEMA.SEQUENCES`.

## Что уже есть в порту (не перечитывать)

| Место | Что там |
|---|---|
| `Schemas/Sequence.cs` | эталонный `Sequence` целиком: ctor :71-85 (считает start/increment/min/max/cache), `GetNext` :244, `Modify` :96 — отказывается (персистентность последовательностей не портирована) |
| `Command/Ddl/SequenceOptions.cs` | эталонные опции последовательности |
| `Tables/TableTypes.cs:1300-1351` | эталонный `convertInsertRow` **уже есть**, включая второй проход по генерируемым колонкам (`generated > 0`) |
| `Tables/ColumnResolver.cs` | эталонный интерфейс резолвера: `GetColumns`, `FindColumn`, `GetValue(Column)`, `GetRowIdColumn` |
| `Expressions/ExpressionColumn.cs:108-132` | `GetValue` читает через тот резолвер, через который колонка отображена, — этого достаточно резолверу генерируемых колонок |
| `Command/Ddl/CommandWithColumns.cs:162` | эталонная точка `GenerateSequences` — заглушка |
| `Command/Ddl/CreateTable.cs:112` (`Update`) | эталонная структура создания таблицы; место для `GenerateSequences` и `PrepareExpressions` |
| `Tables/Column.cs:428/440/460/487` | `SetGeneratedExpression`, `SetDefaultExpression`, `SetOnUpdateExpression`, `IsDefaultOnNull`/`SetDefaultOnNull` — эталонные |
| `Command/Parser.cs:70` | `ColumnDefinition` (record) — расширяется |
| `Command/Parser.cs:3400-3439` | разбор колонки в `CREATE TABLE` |
| `Command/Parser.cs:2841-2877` | `PrepareCreateTable` — переносит свойства колонки на `Tables.Column` |

## Эталонные места (по именам, читать не нужно)

* `org.h2.command.Parser.parseColumnForTable` — порядок чтения колонки: тип, `INVISIBLE`/
  `VISIBLE`, `NOT NULL`, затем `AS (expr)` / `DEFAULT [ON NULL] expr` / `GENERATED [ALWAYS |
  BY DEFAULT] AS IDENTITY [(options)]` / `GENERATED ALWAYS AS (expr)`, затем `ON UPDATE expr`.
* `org.h2.command.Parser.parseSequenceOptions` и `parseBasicSequenceOption` — опции
  последовательности: `START WITH`, `INCREMENT [BY]`, `MINVALUE`, `MAXVALUE`, `CYCLE`,
  `NO CYCLE`, `NO MINVALUE`, `NO MAXVALUE`, `CACHE`, `RESTART`.
* `org.h2.table.Column`:
  * `setIdentityOptions(options, generatedAlways)` — сбрасывает `nullable` в false и
    `default`/`onUpdate` в null;
  * `hasIdentityOptions()`, `getIdentityOptions()`, `setSequence(sequence, generatedAlways)`,
    `getSequence()`;
  * `initializeSequence(session, schema, id, temporary)` — создаёт последовательность
    `SYSTEM_SEQUENCE_<uuid без дефисов, верхний регистр>`, кладёт её в схему, ставит
    колонке `NOT NULL`;
  * `isIdentity()` — `sequence != null || identityOptions != null`;
  * `getEffectiveDefaultExpression()` — null, если у колонки есть последовательность;
  * `validateConvertUpdateSequence` — при `value == null` и наличии последовательности берёт
    её следующее значение, иначе умолчание/генерируемое;
  * `getDefaultOrGenerated(session, row)` — при `isGeneratedAlways` ставит текущую строку в
    резолвер, считает выражение, снимает строку;
  * `prepareExpressions(session)` — для генерируемой колонки создаёт
    `GeneratedColumnResolver`, мапит по нему выражение (`MAP_INITIAL`) и оптимизирует;
  * `setGeneratedExpression(expr)` — выражение **не** оптимизируется (оптимизация позже, в
    `prepareExpressions`).
* `org.h2.table.GeneratedColumnResolver` — резолвер генерируемых колонок: отдаёт колонки
  таблицы **без генерируемых**, значение колонки — из текущей вставляемой строки, для
  колонки с id `-1` — ключ строки.
* `org.h2.table.Table.convertInsertRow` — генерируемые колонки считаются **вторым
  проходом**, когда значения остальных колонок строки уже записаны. Именно поэтому
  `AS (A+B)` и работает.
* `org.h2.command.ddl.CommandWithColumns.generateSequences` — для каждой колонки с
  identity-опциями создаёт последовательность.
* `org.h2.command.ddl.CreateTable.update` — `generateSequences` и `prepareExpressions` для
  каждой колонки **после** создания таблицы: резолверу нужны колонки таблицы.
* `org.h2.engine.SessionLocal.getNextValueFor(sequence, prepared)`.

## Механика `AS (A+B)` — коротко

1. `GENERATED ALWAYS AS (A+B)` → у колонки `isGeneratedAlways = true`, выражение сохранено
   как есть.
2. После создания таблицы `prepareExpressions` мапит выражение на `GeneratedColumnResolver`:
   в резолвере видны только **негенерируемые** колонки, и значение каждой берётся из текущей
   строки. Выражение оптимизируется после маппинга.
3. При вставке `convertInsertRow` сначала пишет значения обычных колонок, потом вторым
   проходом считает генерируемые — выражение видит уже записанные значения строки.
4. Identity-колонка генерируемой **не** является: у неё нет выражения по умолчанию, её
   значение приходит из последовательности. Поэтому в резолвере identity-колонка остаётся
   обычной колонкой, и на неё можно ссылаться.

## Арифметика, без которой `AS (A+B)` не работает

Генерируемая колонка считается выражением, а выражение может быть арифметикой, а её в порту
не было: в цепочке чтения выражения порта (`ReadQueryOr` → `ReadQueryAnd` →
`ReadQueryComparison` → `ReadQueryTerm`) не было уровней суммы и произведения. В эталоне
цепочка такая:

* `readExpression` → `readConcat` → `readSum` (`+`, `-`) → `readFactor` (`*`, `/`, `%`) →
  `readTerm`;
* `readConcat` — это `||`, и режим по умолчанию не разрешает склеивать строки плюсом;
* `%` в эталоне — это `MathFunction(r, readTerm(), MathFunction.MOD)`, а не отдельный оператор.

Порт теперь: `ReadQuerySum` → `ReadQueryFactor` → `ReadQueryTerm`, а выражение операции —
`Expressions/BinaryOperation.cs` (порт `org.h2.expression.BinaryOperation`): сложение,
вычитание, умножение, деление, с правилами расширения типов эталонa
(`optimizeNumeric`, `optimizeDecfloat`).

Что в `BinaryOperation` не портировано и отказывает по имени: операции над интервалами
(нужен `IntervalOperation`) и над датой-временем (нужны их классы), склейка строк плюсом
(нужен `ConcatenationOperation`; в обычном режиме эталон её и не разрешает).

Значения умеют считать и раньше: `Value.Add/Subtract/Multiply/Divide/Modulus/Negate` в порту
есть (`Value/Value.Arithmetic.cs` и классы значений) — не было только выражения-оператора.

## Режимы

Флагов режима у порта нет, но и не нужно: в **обычном** режиме эталон

* `updateSequenceOnManualIdentityInsertion = false` — ручная вставка в identity-BY-DEFAULT
  **не** двигает последовательность вперёд (в LEGACY — двигает);
* `nextValueReturnsDifferentValues = false` и `takeGeneratedSequenceValue = false` — при
  `prepared == null` значение всегда новое.

Значит поведение порта совпадает с обычным режимом эталона; расхождение возможно только в
режимах совместимости, которых у порта нет.

## План по файлам

1. **`src/H2Sharp/Tables/GeneratedColumnResolver.cs` (новый)** — порт
   `org.h2.table.GeneratedColumnResolver`: `Table table`, `Column[]? columns`,
   `Dictionary<string, Column>? columnMap`, `Row? current`; `Set(Row?)`; `GetColumns()` (без
   генерируемых, с ленивым кэшем); `FindColumn(name)`; `GetValue(column)` (значение текущей
   строки; для `columnId == -1` — ключ строки); `GetRowIdColumn()` → null (у портовой
   таблицы нет rowid-колонки, ветка не используется).
2. **`src/H2Sharp/Tables/Column.cs`** — `generatedTableFilter`; `PrepareExpressions(session)`;
   `GetDefaultOrGenerated(session, row)` со строкой в резолвере; identity-члены
   (`identityOptions`, `sequence`, `SetIdentityOptions`, `HasIdentityOptions`,
   `GetIdentityOptions`, `SetSequence`, `GetSequence`, `InitializeSequence`); `IsIdentity()` по
   эталону; значение identity из последовательности в `ValidateConvertUpdateSequence`.
3. **`src/H2Sharp/Engine/SessionLocal.cs`** — `GetNextValueFor(sequence, prepared)`.
4. **`src/H2Sharp/Schemas/Schema.cs`** — хранить последовательности: `Add`/`Remove`,
   `FindSequence(name)`, `GetAllSequences()`.
5. **`src/H2Sharp/Command/Ddl/CommandWithColumns.cs`** — `GenerateSequences` по эталону.
6. **`src/H2Sharp/Command/Ddl/CreateTable.cs`** — `GenerateSequences` и `PrepareExpressions`
   для колонок после создания таблицы.
7. **`src/H2Sharp/Command/Parser.cs`** — разбор колонки (identity/generated/default/on-update),
   опции последовательности, `ColumnDefinition`, `PrepareCreateTable`.
8. **`src/H2Sharp/Tables/InformationSchemaTable.cs`** — identity-поля `COLUMNS` из
   последовательности колонки.

## Проверка

* оракул на JVM (`tests/H2Sharp.Tests/IdentityOracleTests.cs`): значения identity, `START WITH`,
  ручная вставка в `ALWAYS` (90154), `GENERATED ALWAYS AS (A+B)`, identity-поля
  `INFORMATION_SCHEMA.COLUMNS`;
* тест через ADO.NET (`tests/H2Sharp.Data.Tests/H2IdentityTests.cs`) — тот же круг через
  провайдера;
* спеки: `Grammar/ConstraintTests` (START WITH 100), `Grammar/StatementGrammarTests`,
  `Grammar/SequenceGrammarTests`, `Grammar/MergeClauseTests` — все четыре создают таблицу с
  identity.

## Что сделано

* `Tables/GeneratedColumnResolver.cs` — резолвер генерируемой колонки.
* `Tables/Column.cs` — `identityOptions`, `sequence`, `PrepareExpressions`,
  `GetDefaultOrGenerated` со строкой в резолвере, `IsIdentity` по эталону, значение
  identity из последовательности.
* `Schemas/Sequence.cs` — `GetNext` (с механикой margin и cache), `GetCurrentValue`,
  `Cycle.EXHAUSTED`; `Flush` — никуда не пишет: последовательности порта живут в памяти.
* `Schemas/Schema.cs` — последовательности схемы (`Add`, `Remove`, `FindSequence`,
  `GetAllSequences`).
* `Engine/SessionLocal.cs` — `GetNextValueFor`.
* `Command/Ddl/CommandWithColumns.cs` — `GenerateSequences`; `Command/Ddl/CreateTable.cs` —
  последовательности и `PrepareExpressions` после создания таблицы.
* `Command/Parser.cs` — разбор колонки (identity, генерируемая, умолчание, on update),
  опции последовательности, `ColumnDefinition`, `PrepareCreateTable`; уровни арифметики
  `ReadQuerySum`/`ReadQueryFactor`.
* `Expressions/BinaryOperation.cs` — операция над двумя выражениями.
* `Value/DataType.cs` — `IsIntervalType`; `Value/TypeInfo.cs` — `ToDecfloatType`.
* `Tables/InformationSchemaTable.cs` — identity-поля `COLUMNS`.

## Продолжение среза: последовательности

Срез identity доведён до конца отдельно — сами последовательности, их каталог и изменение
identity-колонки. Что появилось:

| Место | Что там |
|---|---|
| `Command/Ddl/CreateSequence.cs`, `AlterSequence.cs`, `DropSequence.cs`, `TruncateTable.cs` | эталонные команды `CREATE`/`ALTER`/`DROP SEQUENCE` и `TRUNCATE TABLE` |
| `Command/Ddl/AlterTableAlterColumn.cs` | эталонный `ALTER TABLE … ALTER COLUMN` (пока только identity) |
| `Command/Ddl/SequenceOptions.cs` | `GetRestartValue` → `long?` (null, если restart не задан), `GetCacheSize` → `long?` |
| `Expressions/SequenceValue.cs` | `NEXT VALUE FOR` / `CURRENT VALUE FOR` |
| `Expressions/Functions/CompatibilitySequenceValueFunction.cs` | эталонные `NEXTVAL`/`CURRVAL` |
| `Schemas/Sequence.cs` | `Modify` (эталонная семантика), `CheckOptions`, `GetMaxCacheSize` |
| `Schemas/Schema.cs` | `GetSequence` (90036, `SequenceNotFound1`) |
| `Engine/SessionLocal.cs` | `GetCurrentValueFor`, карта текущих значений сессии |
| `Command/Parser.cs` | `PrepareCreateSequence`/`PrepareAlterSequence`/`PrepareDropSequence`/`PrepareTruncateTable`, `ReadSequence`, `ParseExpression(string)` (порт эталонного `parseExpression(String)`) |

Тонкости, которые пришлось узнать (оракул, а не чтение):

* `SequenceOptions.getRestartValue` возвращает **null**, если restart не задан. Значение
  `long?` в порте — это не украшение: иначе `ALTER SEQUENCE … RESTART` без значения терял бы
  текущее значение последовательности. `RESTART` без числа — «вернуться к стартовому», и
  знак этого в порте — `ValueExpression.Null` (в эталоне это `DEFAULT`).
* `SequenceOptions.getCacheSize` — тоже null, если кэш не задан.
* Последовательность проверяет опции **в конструкторе** (`checkOptions`): `MAXVALUE 1
  START WITH 1 CYCLE` на JVM отвечает 90009, и порт отвечает тем же.
* `Schema.getSequence` бросает 90036 (`SequenceNotFound1`), а `findSequence` возвращает null.
* `nextval('S')` разбирает имя **как выражение**: отсюда квалифицированное имя даёт схему, а
  недопустимое имя — синтаксическая ошибка. Порт для этого умеет `Parser.ParseExpression(string)`
  (порт эталонного `parseExpression(String)`).
* Опция владения читается **одним** токеном, `BELONGS_TO_TABLE`. Трёхсловное написание из
  документации (`BELONGS TO TABLE`) в эталоне — синтаксическая ошибка, и оракул это подтверждает:
  `CREATE SEQUENCE … BELONGS TO TABLE` на JVM отвечает 42000, а `BELONGS_TO_TABLE` проходит.
* Имя последовательности может быть с именем схемы: `CREATE SEQUENCE S.SEQ` — эталонное
  `readIdentifierWithSchema`. В порту это `Parser.ReadIdentifierWithSchema` (то же имя схемы
  может стоять перед именем базы и тогда пропускается). Неизвестная схема — 90079
  (`SchemaNotFound1`), как в эталоне.

Грамматика, взятая из эталонной (`docs/grammar.html`):

```
CREATE SEQUENCE [ IF NOT EXISTS ] name [ AS dataType ] [ START WITH long ]
    [ INCREMENT [ BY ] long ] [ MINVALUE long | NO MINVALUE ] [ MAXVALUE long | NO MAXVALUE ]
    [ CYCLE | NO CYCLE ] [ CACHE long ] [ BELONGS_TO_TABLE ]
ALTER SEQUENCE [ IF EXISTS ] name [ RESTART [ WITH long ] ] [ … те же опции … ]
DROP SEQUENCE [ IF EXISTS ] name
```

Остальное `ALTER TABLE … ALTER COLUMN` (RENAME, SET DEFAULT, SELECTIVITY, смена типа,
INVISIBLE) — не портировано и отказывает по имени; `COMMENT ON` не портирован вовсе.
* `Tables/TableTypes.cs` — последовательности таблицы (снимаются вместе с ней).

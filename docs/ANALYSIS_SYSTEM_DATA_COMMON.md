# Анализ поверхности System.Data.Common (.NET 10) для провайдера H2Sharp

**Дата:** 2026-11-04
**Статус:** реализовано в ветке `feature/system-data-common`; см. раздел 8.
**Источники:** документация Microsoft Learn (`?view=net-10.0`, пространства имён `System.Data.Common` и `System.Data`), код репозитория `C:\Work\h2sharp` (провайдер в `src/H2Sharp/Data/`), эталон H2 v2.5 (`reference/h2`, `org.h2.jdbc.*`), ADR-001 (`docs/ADR_DOTNET_EXTENSIONS.md`).

---

## 1. Резюме

Провайдер H2Sharp уже реализует практически весь базовый набор `System.Data.Common`: `DbConnection`, `DbCommand`, `DbDataReader`, `DbParameter`/`DbParameterCollection`, `DbTransaction`, `DbProviderFactory`, `DbConnectionStringBuilder`, `DbDataAdapter`, `DbCommandBuilder`, `DbException`, а также современные `DbDataSource`, `DbBatch`/`DbBatchCommand`/`DbBatchCommandCollection`, `DbColumn`. Это покрывает контракт провайдера ADO.NET целиком — включая async-перегрузки, savepoint'ы транзакций и батчи.

Реальные пробелы и дефекты:

* **P0 — дефект:** `H2ProviderFactory.CanCreateBatch = false` при полностью работающем `H2Batch` (правка: поднять флаг и реализовать `CreateBatch`).
* **P1 — дополнения:** интерфейс `IDbColumnSchemaGenerator` на `H2DataReader`; `GetSchema`/`GetSchemaAsync` на `H2Connection` (единственный крупный пункт с прямым Java-аналогом — `JdbcDatabaseMetaData`); живое поведение `H2DataAdapter` (события `RowUpdating`/`RowUpdated`/`FillError`); использование `DbEnumerator`.
* **P2 — опционально:** генерация команд `H2CommandBuilder`, async-перегрузки транзакций, `VisibleFieldCount`, провайдер-специфичное свойство параметра с `[DbProviderSpecificTypeProperty]`, кастомное событие `StatementCompleted` на `H2Command`.
* **Не делать:** `IDbConnectionExtension`/`IDbTransactionExtension`/`IDbDataSource` (в BCL .NET 10 не существуют), `DbDataSourceEnumerator` (нет серверного обнаружения), `DBDataPermission` (устаревшая CAS), EF6-пламбинг (`DbProviderServices` и др.), EF-специфичные типы (`IExtendedDataRecord`, `DataRecordInfo`), `IColumnMapping`/`ITableMapping` (внутренние механизмы адаптера).

---

## 2. Статус-кво: что уже унаследовано/реализовано

| Тип .NET | Класс H2Sharp | Файл | Примечания |
|---|---|---|---|
| `DbConnection` | `H2Connection` | `src/H2Sharp/Data/H2Connection.cs:49` | Дополнительно: `IH2Test`, `IH2Extensions`, `IH2RawExtensions` (ADR-001). Поднимает `StateChange` (см. раздел 4). `GetSchema`/`GetSchemaAsync` не перекрыты. |
| `DbCommand` | `H2Command` | `src/H2Sharp/Data/H2Command.cs:36` | Все абстрактные члены + async. `StatementCompleted` отсутствует. |
| `DbDataReader` | `H2DataReader` | `src/H2Sharp/Data/H2DataReader.cs:34` | `GetSchemaTable` (`:636`), `GetColumnSchemaAsync` (`:617`), `GetFieldValue<T>`/async. Нет sync `GetColumnSchema`/`IDbColumnSchemaGenerator`, нет `VisibleFieldCount`. |
| `DbParameter` / `DbParameterCollection` | `H2Parameter` / `H2ParameterCollection` | `src/H2Sharp/Data/H2Parameter.cs:31,101` | Полный набор абстрактных членов. |
| `DbTransaction` | `H2Transaction` | `src/H2Sharp/Data/H2Transaction.cs:28` | С savepoint'ами: `SupportsSavepoints = true`, `Save`, `Rollback(string)`, `Release` (`:73-89`). Async-перегрузки не перекрыты (базовые sync-fallback). |
| `DbProviderFactory` | `H2ProviderFactory` | `src/H2Sharp/Data/H2ProviderFactory.cs:27` | `CreateConnection/Command/Parameter/ConnectionStringBuilder/DataAdapter/CommandBuilder/DataSource`. **`CanCreateBatch = false` (`:109`) — дефект, см. раздел 3.1.** |
| `DbConnectionStringBuilder` | `H2ConnectionStringBuilder` | `src/H2Sharp/Data/H2ConnectionStringBuilder.cs:24` | Типизированные ключи над базовым индексером. |
| `DbDataAdapter` | `H2DataAdapter` | `src/H2Sharp/Data/H2DataAdapter.cs:23` | Только `new`-свойства команд. События `RowUpdating`/`RowUpdated`/`FillError` наследуются, но не поднимаются. |
| `DbCommandBuilder` | `H2CommandBuilder` | `src/H2Sharp/Data/H2CommandBuilder.cs:24` | Генерация отказывает `NotSupportedException` (нужен механизм схемы); `SetRowUpdatingHandler` пуст. |
| `DbColumn` | `H2DbColumn` (internal) | `src/H2Sharp/Data/H2DbColumn.cs:22` | Вернётся из `GetColumnSchemaAsync`. |
| `DbDataSource` | `H2DataSource` (+ `H2DataSourceBuilder`) | `src/H2Sharp/Data/H2DataSource.cs:20` | `CreateDbConnection`, `OpenConnection`, команды. |
| `DbBatch` / `DbBatchCommand` / `DbBatchCommandCollection` | `H2Batch` / `H2BatchCommand` / `BatchCommandCollection` | `src/H2Sharp/Data/H2Batch.cs:21,267,231` | Полная реализация; `H2Connection.CreateDbBatch()` работает (`H2Connection.cs:501-502`). |
| `DbException` | `H2SharpException` | `src/H2Sharp/H2SharpException.cs:94` | `override string SqlState` уже есть (`:127`); `IsTransient` не перекрыт (default false); `H2ErrorCode` = `getErrorCode()`. |

Интерфейсы `System.Data` (`IDbConnection`, `IDbCommand`, `IDataReader`, `IDataRecord`, `IDataParameter`, `IDataParameterCollection`, `IDbDataAdapter`, `IDbDataParameter`, `IDbTransaction`, `IDataAdapter`) реализуются базовыми классами `Db*` — отдельно в коде провайдера их объявлять не нужно.

---

## 3. Кандидаты «наследовать/реализовать»

### 3.1. P0 — дефект порта флага

| Что | Где | Что делать |
|---|---|---|
| `CanCreateBatch = false` | `H2ProviderFactory.cs:109` | Фактически батчи работают (`H2Batch`, `H2Connection.CreateDbBatch()`). Поднять `CanCreateBatch` до `true` и переопределить `CreateBatch()` → `new H2Batch(new H2Connection())` (по образцу `CreateCommand`). Тест: фабрика создаёт батч и выполняет его. |

Проверено и **НЕ является дефектом**:

* `H2SharpException.SqlState` — уже `override` (`H2SharpException.cs:127`), скрытия базового члена нет.
* `StateChange` в async-путях — `OpenAsync` делегирует в `Open()` (`H2Connection.cs:326-335`), где событие поднимается; `CloseAsync` не перекрыт и использует базовый sync-fallback на `Close()`, который событие поднимает.

### 3.2. P1 — функциональные дополнения

#### 1. `IDbColumnSchemaGenerator` на `H2DataReader`

* **Зачем:** единственный публичный интерфейс в `System.Data.Common` (помимо классов). Позволяет инструментам читать схему синхронно через `GetColumnSchema()`.
* **Где:** `H2DataReader.cs` — реализация тривиальна поверх существующего приватного билдера `ColumnDescriptions()` (уже используется в `GetColumnSchemaAsync`, `:617-629`).
* **Малость:** добавить `IDbColumnSchemaGenerator` в список интерфейсов класса и метод `ReadOnlyCollection<DbColumn> GetColumnSchema()`.

#### 2. `GetSchema` / `GetSchemaAsync` на `H2Connection`

* **Зачем:** это .NET-аналог `java.sql.DatabaseMetaData`; единственный крупный пункт с прямым Java-аналогом в эталоне (`org.h2.jdbc.JdbcDatabaseMetaData`). Нужен общим инструментам (обозреватели БД, дизайнеры, некоторые тесты).
* **Где:** `H2Connection.cs`; коллекции строятся запросами к `INFORMATION_SCHEMA` (как это делает эталон) или через метаданные движка. Разумное подмножество: `MetaDataCollections`, `DataTypes`, `ReservedWords`, `Tables`, `Columns`, `Indexes`, `ForeignKeys`, `Procedures`, `TableTypes`, `Catalogs`, `Schemas`.
* **Форма:** `public override DataTable GetSchema()`, `GetSchema(string collectionName)`, `GetSchema(string collectionName, string?[] restrictionValues)`, а также `GetSchemaAsync`-варианты (перекрыть, чтобы не идти через sync-fallback).
* **Оракул:** `JavaOracleFact`-тесты против `JdbcDatabaseMetaData` по протоколу `docs/REGRESSION_TEST_PROTOCOL.md`.

#### 3. Завершение `H2DataAdapter`: события строк и ошибок заполнения

* **Зачем:** `DbDataAdapter` даёт контракт `RowUpdating`/`RowUpdated`/`FillError`; без их подъёма адаптер не может сообщить об обновлении строк и конфликтах.
* **Где:** `H2DataAdapter.cs` + новые `H2RowUpdatingEventArgs : RowUpdatingEventArgs`, `H2RowUpdatedEventArgs : RowUpdatedEventArgs` (по образцу `SqlRowUpdatingEventArgs`/`SqlRowUpdatedEventArgs`). Перекрыть `CreateRowUpdatingEvent`/`CreateRowUpdatedEvent`/`OnRowUpdating`/`OnRowUpdated`/`OnFillError`.
* **Работает и без генерации билдера** — пользователь задаёт `Insert/Update/DeleteCommand` вручную; события срабатывают и для ручных команд.
* **Пометка:** это чистый .NET-слой без Java-аналога (у JDBC нет DataSet/адаптеров). Типы уже существуют в порте и являются частью принятой формы провайдера; расширение их поведения — развитие этого слоя, а не новая SQL-семантика. Фиксация в `MIGRATION_STATUS.md` желательна.

#### 4. `DbEnumerator`

* **Как использовать:** `DbEnumerator` — **конкретный** класс, наследовать его не нужно; он принимает `IDataReader` в конструкторе и оборачивает ридер для биндинга (`IEnumerator`, `ICustomTypeDescriptor`). Достаточно использовать `new DbEnumerator(reader)` в коде потребителя или вернуть его из `H2DataReader.GetEnumerator()`-совместимого пути.
* **Действие:** документация/пример в тестах, код провайдера не меняется.

### 3.3. P2 — опционально

| Пункт | Где | Комментарий |
|---|---|---|
| Генерация команд `H2CommandBuilder` | `H2CommandBuilder.cs` | Сначала `QuoteIdentifier`/`UnquoteIdentifier` по правилам режима (`Mode`/`Parser`), затем INSERT/UPDATE/DELETE по `GetSchemaTable`. Разблокирует автоматику адаптера. Крупная работа. |
| Async-перегрузки `H2Transaction` | `H2Transaction.cs` | `CommitAsync`/`RollbackAsync`/`SaveAsync`/`ReleaseAsync` — сейчас базовые sync-fallback; с учётом блокировки сессии можно дать неблокирующие варианты. |
| `VisibleFieldCount` | `H2DataReader.cs` | Должен быть равен `FieldCount` (скрытых столбцов нет) — тривиальный override. |
| `[DbProviderSpecificTypeProperty]` | `H2Parameter.cs` | Провайдер-специфичное свойство типа (например `H2DbType`) для дизайнеров; опционально. |
| Кастомное событие `StatementCompleted` | `H2Command.cs` | По образцу SqlClient. **Внимание:** BCL-контракт `DbCommand` его не объявляет (см. раздел 4); это чисто провайдерское расширение. |

### 3.4. Не делать (с обоснованием)

| Тип | Почему нет |
|---|---|
| `IDbConnectionExtension`, `IDbTransactionExtension`, `IDbDataSource` | **Не существуют в BCL .NET 10** (проверено: 404 в `System.Data.Common` и `System.Data`). Выдумывать свои аналоги нельзя. |
| `DbDataSourceEnumerator` / `CanCreateDataSourceEnumerator` | Рет не имеет серверного обнаружения; флаг честно `false` (`H2ProviderFactory.cs:98-104`). |
| `DBDataPermission`, `DBDataPermissionAttribute` | Устаревшая CAS; в современных провайдерах не используется. |
| `DbProviderServices`, `DbCommandDefinition`, `DbProviderManifest`, `DbXmlEnabledProviderManifest` | Пламбинг EF6; EF Core их не использует. |
| `IExtendedDataRecord`, `DataRecordInfo`, `EntityRecordInfo`, `FieldMetadata` | EF-специфика, к ADO.NET-провайдеру не относится. |
| `IColumnMapping`, `ITableMapping`, `DataColumnMapping`, `DataTableMapping` | Внутренние механизмы `DataAdapter`; провайдером не реализуются. |
| `DbMetaDataCollectionNames` | Константы; понадобятся вместе с `GetSchema` (раздел 3.2.2). |
| `Component.Disposed` | Наследуется из `Component`; действий не требует. |

---

## 4. События: сводная таблица

| Событие | Делегат | Где объявлено в BCL | Статус в H2Sharp | Что делать |
|---|---|---|---|---|
| `StateChange` | `StateChangeEventHandler` | `DbConnection` | Поднимается в `Open()` (`H2Connection.cs:314`), `Close()` (`:449`, `:470`); `OpenAsync` → `Open()`, `CloseAsync` — базовый sync-fallback | Уже ок; закрепить тестом подписки/срабатывания |
| `StatementCompleted` | `StatementCompletedEventHandler` | **Нет в BCL** (конкретные провайдерские команды, напр. `SqlCommand`) | Отсутствует | Опционально: своё событие на `H2Command` (P2) |
| `RowUpdating` / `RowUpdated` | `RowUpdatingEventHandler` / `RowUpdatedEventHandler` | `DbDataAdapter` (protected-хуки `CreateRowUpdatingEvent`/`CreateRowUpdatedEvent`/`OnRowUpdating`/`OnRowUpdated`; публичные события — в конкретных адаптерах) | Наследуется, не поднимается | `H2RowUpdatingEventArgs`/`H2RowUpdatedEventArgs` + проводка в `H2DataAdapter` (P1) |
| `FillError` | `FillErrorEventHandler` | `DataAdapter` | Наследуется, не поднимается | `OnFillError` в `H2DataAdapter` (P1) |
| `Disposed` | `EventHandler` | `Component` | Наследуется | Действий не требует |

---

## 5. Карта на эталон H2 v2.5

| Функция .NET | Java-аналог в эталоне | Статус |
|---|---|---|
| `GetSchema` / `GetSchemaAsync` | `JdbcDatabaseMetaData` (`org.h2.jdbc.JdbcDatabaseMetaData`) | Не реализовано — кандидат P1 (раздел 3.2.2) |
| `H2SharpException` / `SqlState` / `Kind` | `SQLException` + подклассы `JdbcSQL*Exception` | Реализовано (`H2SharpException.cs`); `SqlState` — override |
| `H2Connection.BeginTransaction(IsolationLevel)` | `JdbcConnection.setTransactionIsolation` | Реализовано (`H2Connection.cs:527+`) |
| `H2Transaction` savepoint'ы | `JdbcSavepoint` / `setSavepoint` | Реализовано (`H2Transaction.cs:73-89`) |
| `DbBatch` / `ExecuteBatch` | `JdbcStatement.executeBatch` / `addBatch` | Реализовано (`H2Batch.cs`) |
| `DbDataAdapter`, `DbCommandBuilder`, `DbEnumerator`, `DbDataSource`, `DbConnectionStringBuilder`, `DbProviderFactory` | Прямого аналога нет (JDBC не имеет DataSet/адаптеров; DriverManager ≈ `DbProviderFactories`) | Существуют в порте как часть .NET-границы; расширение их поведения — развитие уже принятого слоя, не новая SQL-семантика |

Правило репозитория «не изобретать сверх эталона» касается SQL-разбора, схемного реестра и кэш-семантики. Пункты P1/P2 — транспортные и метаданные API (.NET-граница), они не вводят собственный parser/AST и не меняют поведение движка.

---

## 6. План внедрения (порядок будущих PR)

Каждый шаг — отдельный PR; по завершении — снятие `BlockedByPort` там, где применимо.

**Шаг 1 — P0 (малый):**
* Файлы: `src/H2Sharp/Data/H2ProviderFactory.cs` (`CanCreateBatch` → true; override `CreateBatch()`).
* Тесты: `tests/H2Sharp.Data.Tests` — фабрика создаёт `H2Batch`, батч выполняется на соединении.
* Прогон: `dotnet test tests/H2Sharp.Data.Tests`.

**Шаг 2 — `IDbColumnSchemaGenerator` (малый):**
* Файлы: `src/H2Sharp/Data/H2DataReader.cs` (интерфейс + `GetColumnSchema()`).
* Тесты: sync `GetColumnSchema()` возвращает тот же результат, что `GetColumnSchemaAsync()`.

**Шаг 3 — `GetSchema`/`GetSchemaAsync` (крупный):**
* Файлы: `src/H2Sharp/Data/H2Connection.cs`; чтение `reference/h2/src/main/java/org/h2/jdbc/JdbcDatabaseMetaData.java`.
* Тесты: `JavaOracleFact` против `JdbcDatabaseMetaData` (по `docs/REGRESSION_TEST_PROTOCOL.md`) + интеграционные на коллекциях `Tables`/`Columns`/`DataTypes`/`ReservedWords`.

**Шаг 4 — `H2DataAdapter` события (средний):**
* Файлы: `src/H2Sharp/Data/H2DataAdapter.cs`, новые `H2RowUpdatingEventArgs.cs`/`H2RowUpdatedEventArgs.cs`.
* Тесты: row-update flow с ручными `Insert/Update/DeleteCommand`; `FillError` при сбое заполнения.

**Шаг 5 — опционально:**
* `H2CommandBuilder`: сначала `QuoteIdentifier`/`UnquoteIdentifier` отдельным PR с тестами цитирования, затем генерация команд.
* `H2Transaction` async-перегрузки; `VisibleFieldCount`; `[DbProviderSpecificTypeProperty]`; кастомный `StatementCompleted` — по отдельности.

**Сквозная проверка:** `dotnet test tests/H2Sharp.Data.Tests` (включая набор `AdoNet.Specification.Tests`); оракульные тесты — по протоколу раздела 8 `REGRESSION_TEST_PROTOCOL.md`.

---

## 7. Источники

* Microsoft Learn: `System.Data.Common` namespace, `?view=net-10.0` — https://learn.microsoft.com/en-us/dotnet/api/system.data.common?view=net-10.0
* Microsoft Learn: `System.Data` namespace (интерфейсы, делегаты, enum), `?view=net-10.0` — https://learn.microsoft.com/en-us/dotnet/api/system.data?view=net-10.0
* Microsoft Learn: `DbCommand` (в т.ч. отсутствие события `StatementCompleted`), `?view=net-10.0` — https://learn.microsoft.com/en-us/dotnet/api/system.data.common.dbcommand?view=net-10.0
* Проверки 404: `IDbConnectionExtension`, `IDbTransactionExtension`, `IDbDataSource` — отсутствуют в документации .NET 10.
* Код репозитория: `src/H2Sharp/Data/*`, `src/H2Sharp/H2SharpException.cs`, `src/H2Sharp/Engine/Database.cs`.
* Эталон: `reference/h2` (H2 v2.5, `org.h2.jdbc.*`).

---

## 8. Реализация

Реализовано в ветке `feature/system-data-common` (отдельный worktree):

* **P0:** `H2ProviderFactory.CreateBatch()` и `CanCreateBatch => true`;
  `H2Batch` принимает nullable-соединение.
* **P1.1:** `H2DataReader : IDbColumnSchemaGenerator`, sync/async schema
  читают один билдер.
* **P1.3:** `H2DataAdapter` с типизированными событиями
  `RowUpdating`/`RowUpdated` (`H2RowUpdatingEventArgs`/`H2RowUpdatedEventArgs`
  и делегаты `H2RowUpdatingEventHandler`/`H2RowUpdatedEventHandler`).
  `FillError` уже поднимается `DataAdapter.Fill`; код не менялся.
* **P1.2:** порт `org.h2.jdbc.meta` (`DatabaseMeta`, `DatabaseMetaLocalBase`,
  `DatabaseMetaLocal`) в `H2Sharp.Jdbc.Meta`; `Session.GetDatabaseMeta()`
  возвращает `DatabaseMeta`. Реализованы Tables, Columns, Schemas, Catalogs,
  TableTypes, IndexInfo, Primary/Imported/Exported/CrossReference keys,
  TypeInfo, SQLKeywords, BestRowIdentifier, PseudoColumns, SearchStringEscape,
  версии. `Procedures`/`ProcedureColumns` возвращают эталонные колонки без
  строк: `CREATE ALIAS`/`FunctionAlias` вне scope по D-037 (встраиваемый
  движок не хранит исполняемый код в базе, поэтому alias не может быть
  персистентным); privileges — `Right`/`User` (D-004); function lists —
  `Help`.
* **P1.2 (GetSchema):** `H2Connection.GetSchema`/`GetSchemaAsync` с
  коллекциями `MetaDataCollections`, `Restrictions`, `DataTypes`,
  `ReservedWords`, `Tables`, `Columns`, `Indexes`, `ForeignKeys`,
  `Procedures`, `TableTypes`, `Catalogs`, `Schemas`. Колонки engine-backed
  коллекций — эталонные; `MetaDataCollections`/`Restrictions` — стандартная
  ADO.NET-форма. `Procedures` всегда пуст (D-037).
* **Оракул:** `MetadataOracleTests` (`JavaOracleFact`) сравнивает строки
  `DatabaseMetaLocal` с `JdbcDatabaseMetaData`, включая `DATA_TYPE`
  (используются коды `java.sql.Types`).
* **Попутные эталонные исправления:** `Table.GetSQLTableType`,
  `Column.GetDefaultSQL`, стабильная сортировка `SimpleResult.SortRows`,
  `MVDelegateIndex.GetDiskSpaceUsed`, `DataType.ConvertValueTypeToSqlType`.
* **Проверка:** `dotnet test tests/H2Sharp.Data.Tests` — 717/717;
  `MetadataOracleTests` — зелёный при собранном эталонном jar.
* **Слияние:** ветка интегрируется в `main` полноценным merge-коммитом
  (`main` ведёт другой агент), конфликты разрешаются вручную.

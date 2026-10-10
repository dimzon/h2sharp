# Готовые наборы тестов для слоя ADO.NET

Подборка существующих (не написанных с нуля) наборов тестов, которые проверяют реализацию
`DbConnection` / `DbCommand` / `DbParameter` / `DbDataReader` / `DbTransaction` / `DbProviderFactory`.
Отсортировано по полезности для `H2Sharp.Data`.

Текущее состояние слоя в этом репозитории: `src/H2Sharp/Data/` — `H2Command.cs`, `H2Connection.cs`,
`H2Parameter.cs` (включая `H2ParameterCollection`), `H2Transaction.cs`, `H2ProviderFactory.cs`,
`H2ConnectionStringBuilder.cs`.
Набор уже подключён — см. раздел «Состояние на момент подключения» в конце документа.

---

## 1. AdoNet.Specification.Tests — основной вариант

**Репозиторий:** https://github.com/mysql-net/AdoNetApiTest
**NuGet:** `AdoNet.Specification.Tests` (версии: `1.0.0`, `2.0.0-alpha1..beta.9`, `2.0.0-beta.1`, `3.0.0-beta.1`)
**Лицензия:** MIT. Автор: Bradley Grainger.
**Страница результатов:** https://mysql-net.github.io/AdoNetResults/

Это готовый конформанс-набор для ADO.NET-провайдеров: не тесты конкретного провайдера, а
**базовые классы с тестами**, которые наследуешь. Формат: `public sealed class MyCommandTests :
CommandTestBase<MyFixture>`. Каждый провайдер подключает свой набор, получая ~300 проверок
бесплатно.

По словам README: «ADO.NET не имеет формальной спецификации провайдеров, но многие библиотеки
(Microsoft.Data.SqlClient, Npgsql, MySqlConnector) ведут себя одинаково» — набор фиксирует это
общее поведение.

### Состав (`src/AdoNet.Specification.Tests/`)

| Файл | Размер | Что покрывает |
|---|---|---|
| `CommandTestBase.cs` | 16.5 KB, ~68 тестов | `DbCommand`: `CommandText`, `CommandType`, `Connection`, `Parameters`, `CreateParameter`, `ExecuteNonQuery`/`ExecuteScalar`/`ExecuteReader` + Async-варианты, `Cancel`, `Prepare`, `Transaction` |
| `ParameterTestBase.cs` | 11 KB, ~45 тестов | `DbParameter` + `DbParameterCollection` |
| `ConnectionTestBase.cs` | 8.1 KB, ~30 тестов | `DbConnection`: Open/Close/Dispose/DisposeAsync, `State`, `Database`, `DataSource`, `ServerVersion`, `GetSchema`, `ConnectionString` |
| `DataReaderTestBase.cs` | 35.4 KB, ~130 тестов | `DbDataReader`: все типизированные геттеры, `GetBytes`/`GetChars`, `GetFieldValue<T>`, `GetStream`/`GetTextReader`, `GetColumnSchema`, `NextResult`, `HasRows`, `RecordsAffected` |
| `TransactionTestBase.cs` | 3.7 KB, ~11 тестов | `DbTransaction`: Commit/Rollback, повторные вызовы, вложенные транзакции |
| `DbProviderFactoryTestBase.cs` | 2.2 KB, ~11 тестов | `DbProviderFactory`: `Create*` и `CanCreate*` |
| `DbFactoryTestBase.cs` | 2.7 KB | Инициализация/Dispose фикстуры |
| `ConnectionStringTestBase.cs` | 0.7 KB | `ConnectionString` дефолты |
| `GetValueConversionTestBase.cs` + `.g.cs` | 3.4 KB + **316 KB** | Сгенерированная матрица конверсий типов (RoundTrip / WriteOnly / ReadOnly) |
| `ValueKind.cs` | 2.3 KB | Классификация поддерживаемых типов |
| `IDbFactoryFixture.cs`, `ISelectValueFixture.cs`, `IDeleteFixture.cs`, `UnexpectedValueException.cs` | — | Инфраструктура фикстур |

### Покрытие `ParameterTestBase` (то, что ищется)

- **Дефолты:** `ParameterName` (пустая строка), `Value` (null), `Direction` (Input), `DbType`
  (String), `IsNullable` (false), `Size` (0), `Precision` (0), `Scale` (0), `SourceColumn`
  (пустая строка), `SourceVersion` (Current).
- **Мутации:** `ParameterName` → null становится пустой строкой, `Value` → null, `SourceColumn`
  → null становится пустой строкой, `ResetDbType`.
- **`Bind`:** требует заданные `Name` и `Value`, работает со `string` / `byte[]` / `Stream`,
  бросает на неизвестном типе значения, no-op на неизвестном параметре.
- **`DbParameterCollection`:** `Add`/`Insert`/`Remove`/`RemoveAt` (включая null и out-of-range),
  индексеры `[int]`, `[string]`, `IDataParameterCollection`, `IList` — на null кидают,
  `Contains`/`IndexOf` на null возвращают false / −1, `Insert` на −1 и на 1 кидает,
  `RemoveAt(0)` на пустой коллекции кидает.

### Покрытие `CommandTestBase` (сокращённо)

- **Свойства:** `CommandText` пустая по умолчанию, приведение null → пустая, кидает при установке
  при открытом reader'е, не кидает после Dispose; `CommandType` = Text по умолчанию;
  `Parameters` не null и возвращает тот же объект; `CreateParameter` не null;
  `Connection` можно снять, нельзя поставить null при открытом reader'е, кидает при открытом
  reader'е.
- **Выполнение:** `ExecuteNonQuery` возвращает −1 для SELECT, кидает при закрытом соединении /
  пустом тексте / отсутствии соединения; `ExecuteScalar` возвращает DBNull для null, первое
  значение при батчинге, при нескольких колонках/строках, integer/real/string;
  `ExecuteReader` биндит параметры, `HasRows` = false для комментария, работает с
  `CommandBehavior.CloseConnection`, поддерживает хвостовые комментарии, кидает при открытом
  reader'е, при транзакции не из своей connection, при транзакции, когда она обязательна.
- **Async:** `ExecuteNonQueryAsync` / `ExecuteReaderAsync` / `ExecuteScalarAsync` /
  `GetFieldValueAsync` / `OpenAsync` — отмена через `CancellationToken` и те же ошибки.
- **`Cancel`:** на новой, выполненной, disposed-команде, disposed-соединении — no-op, дважды — no-op.
- **`Prepare`:** кидает при закрытом соединении / пустом тексте / отсутствии соединения.

### Как подключить

Два файла в тестовом проекте:

```csharp
public class H2FactoryFixture : IDbFactoryFixture
{
    public H2FactoryFixture() => ConnectionString = "Data Source=mem:test";

    public string ConnectionString { get; }
    public DbProviderFactory Factory => H2ProviderFactory.Instance;
}

public sealed class H2CommandTests : CommandTestBase<H2FactoryFixture>
{
    public H2CommandTests(H2FactoryFixture fixture) : base(fixture) { }

    [Fact(Skip = "Провайдер не поддерживает stored procedures")]
    public override void CommandType_text_by_default() { }
}
```

Аналогично наследуются `ParameterTestBase`, `ConnectionTestBase`, `DataReaderTestBase`,
`TransactionTestBase`, `DbProviderFactoryTestBase`, `GetValueConversionTestBase`.
Неподдерживаемое переопределяется с `[Fact(Skip = "...")]` — это штатный механизм набора.

### Готовые примеры подключения в том же репозитории

`tests/DotConnectMySql.Tests`, `tests/DotConnectPostgres.Tests`, `tests/DotConnectMySql.Tests`,
`tests/FaithlifeSqlite.Tests`, `tests/MicrosoftSqlClient2..5.Tests`, `tests/MicrosoftSqlite.Tests`,
`tests/MicrosoftSqlite5.Tests` — в каждом по 8–9 файлов: `*CommandTests.cs`, `*ParameterTests.cs`,
`*ConnectionTests.cs`, `*DataReaderTests.cs`, `*TransactionTests.cs`,
`*DbProviderFactoryTests.cs`, `*GetValueConversionTests.cs`, `*SelectValueFixture.cs`,
`*DbFactoryFixture.cs`.

### Требования к H2Sharp

Набору нужен `DbProviderFactory` (`CreateCommand`, `CreateParameter`, `CreateConnection`,
`CreateConnectionStringBuilder`, `CreateCommandBuilder`, `CreateDataAdapter`). В `H2Sharp.Data`
фабрики сейчас нет — её нужно добавить, иначе подключить набор нельзя.

---

## 2. dotnet/runtime — System.Data.Common.Tests

**Путь:** https://github.com/dotnet/runtime/tree/main/src/libraries/System.Data.Common/tests/System/Data/Common

Тесты контракта **базовых классов** `System.Data.Common` (не провайдера). MIT.

| Файл | Размер |
|---|---|
| `DbCommandTests.cs` | 4.3 KB |
| `DbConnectionTests.cs` | 6.6 KB |
| `DbTransactionTest.cs` | 2.7 KB |
| `DbDataReaderTest.cs` | 16.9 KB |
| `DbCommandBuilderTest.cs` | 7.7 KB |
| `DbConnectionStringBuilderTest.cs` | 121 KB |
| `DbDataAdapterTest.cs` | 5.5 KB |
| `DbProviderFactoryTest.cs` / `DbProviderFactoriesTests.cs` | 2.2 / 14.1 KB |
| `DbExceptionTests.cs` | 1.8 KB |
| `DbDataReaderMock.cs` | 6.8 KB |
| `RestrictedTypeHandlingTests.cs` | 17.9 KB |
| `FacadeTest.cs` | 0.7 KB |

**Что реально покрыто:** `DbCommandTests.cs` — один тест (`CanBeFinalized`, что `DbCommand`
финализируется). `DbConnectionTests.cs`, `DbTransactionTest.cs`, `DbDataReaderTest.cs` — в
основном инфраструктура: тестовые заглушки и моки.

**Оценка:** как источник ожидаемого поведения базовых классов (`DataProvider.cs` с
`SAMPLE_STRING`, `DataRecordInternalTest.cs`, `DbDataReaderMock.cs`) — полезно. Как покрытие
провайдера — нет, `DbParameter` здесь вообще не тестируется. Использовать точечно, не как основу.

---

## 3. Npgsql.Tests — эталон глубокого покрытия DbParameter

**Путь:** https://github.com/npgsql/npgsql/tree/main/test/Npgsql.Tests
**Также:** https://github.com/npgsql/npgsql/tree/main/test/Npgsql.Specification.Tests — демонстрация
подключения варианта 1 на реальном провайдере.

Не переиспользуемый набор, а источник идей: тесты `NpgsqlParameter`, `NpgsqlCommand`,
`NpgsqlParameterCollection`, `NpgsqlDataReader`, плюс инфраструктура для сравнения с PostgreSQL.
Стоит просмотреть, когда `AdoNet.Specification.Tests` уже позелёет и нужен второй уровень деталей
(специфичные для провайдера `DbType`-маппинги, array/timestamp/interval handling).

---

## 4. Microsoft.Data.SqlClient — тесты

**Путь:** https://github.com/dotnet/SqlClient/tree/main/src/Microsoft.Data.SqlClient/tests

- `UnitTests` — модульные тесты, не требуют сервера
- `FunctionalTests` — требуют SQL Server
- `Common` — общие хелперы

Требуют реального SQL Server, поэтому как переиспользуемый набор не подходят. Полезны как
эталон: `SqlParameter`-специфичные проверки, `CommandBehavior`-матрица, `SqlException`-коды.

---

## Итоговая рекомендация

1. **База:** `AdoNet.Specification.Tests` (вариант 1) — единственный набор, который и сделан
   для этой задачи, покрывает `DbCommand`/`DbParameter`/`DbParameterCollection`/`DbConnection`/
   `DbTransaction`/`DbProviderFactory`/`DbDataReader` и распространяется как готовая библиотека
   с MIT-лицензией.
2. **Дополнительно:** точечные тесты из `dotnet/runtime` (вариант 2) — на поведение базовых
   классов, особенно `DbParameter`/`DbConnectionStringBuilder`.
3. **Позже:** идеи из `Npgsql.Tests` (вариант 3) для специфичных типов H2.

---

## Состояние на момент подключения (проверено, а не предположено)

Набор подключён: `tests/H2Sharp.Data.Tests/AdoNetSpecificationTests.cs`, пакет
`AdoNet.Specification.Tests` **2.0.0-beta.1**.

**Версия важна, и это выяснилось при подключении, а не из документа.**

| Версия | xunit | Что в ней |
|---|---|---|
| `1.0.0` | xunit 2 | Только `CommandTestBase`, `ConnectionTestBase`, `DataReaderTestBase` — **`ParameterTestBase`, `TransactionTestBase`, `DbProviderFactoryTestBase` отсутствуют** |
| `2.0.0-beta.1` | xunit 2 | Полный набор баз, включая `ParameterTestBase` — **совместима с нашим xunit 2.9.2** |
| `3.0.0-beta.1` | **xunit.v3** | Полный набор, но тянет `xunit.v3` и конфликтует с xunit 2 на уровне типов (`CS0433: FactAttribute exists in both xunit.core and xunit.v3.core`) |

То есть документ выше, перечисляя состав из README, описывает 3.x, а для этого репозитория
подходит 2.0.0-beta.1: полный состав **и** xunit 2. Переход на 3.x — это отдельное решение
(миграция всех тестовых проектов на xunit v3), а не деталь версии пакета.

**Что дало подключение:** 291 тест, из них **72 проходят уже сейчас** (параметры,
коллекция параметров, свойства соединения, фабрика — всё, что не требует выполнения SQL),
219 красные и разложены так: reader 129, command 43, connection 15, parameter 15,
transaction 11, factory 4, плюс 2 Dapper-гейта.

**Что было нужно добавить для подключения:**

| Тип | Состояние |
|---|---|
| `H2ProviderFactory` | **Добавлен.** `Instance`, `CreateConnection`/`CreateCommand`/`CreateParameter`/`CreateConnectionStringBuilder`; `CreateDataAdapter` и `CreateCommandBuilder` отказывают, `CanCreate*` отвечают честно |
| `H2ConnectionStringBuilder` | **Добавлен.** Принимает и `Data Source=mem:x`, и `jdbc:h2:mem:x`, `ToH2ConnectionString()` приводит к URL, который понимает `ConnectionInfo` |
| `H2DataReader` | **Отсутствует** — `ExecuteDbDataReader` отказывает. Это 129 красных тестов: ридер нельзя написать, пока нет результата запроса |
| Фикстура `H2FactoryFixture` | Реализует `IDbFactoryFixture` + `ISelectValueFixture` + `IDeleteFixture` (последние два нужны `DataReaderTestBase<T>`) |

**Ошибки из раздела ниже уже неактуальны** — они были исправлены при подключении:
`ExecuteDbDataReader` больше не возвращает `int`, `[AllowNull]` на `CommandText` восстановлен,
`DbException` разрешён алиасом `H2DbException`. Сборка проходит с 0 warnings / 0 errors.
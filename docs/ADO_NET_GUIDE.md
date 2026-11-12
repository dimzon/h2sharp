# Руководство по ADO.NET-провайдеру H2Sharp

Практическое описание пространства имён `H2Sharp.Data`: как открыть соединение, как
выполняются команды, как связываются параметры, что возвращает ридер, как ведут себя
транзакции, батчи, `DbDataAdapter` и точки расширения .NET.

Документ написан по коду `src/H2Sharp/Data/` и по тестам репозитория. Каждый пример здесь —
из проверенного сценария; в спорных местах указано, каким тестом поведение закреплено.

Два правила, с которых стоит начать:

* **Подключение — только через `H2DataSource`.** Это владелец базы и единственный
  корректный способ получить соединение. Прямой `new H2Connection(...)` создаёт
  самодостаточную базу на одно соединение и не делит состояние с другими.
* **Работают оба хранилища: `Memory` и `File`.** Файловое хранилище MVStore полностью
  перенесено: файл, записанный портом, открывает эталонная Java H2 2.5.259, и наоборот
  (`tests/H2Sharp.Tests/FileDatabaseOracleTests.cs`). Покрытие кода тестами ≈92% и растёт.

Связанные документы: [MIGRATION_STATUS.md](../MIGRATION_STATUS.md) (рабочие заметки порта),
[COVERAGE_ANALYSIS.md](COVERAGE_ANALYSIS.md) (методика измерения покрытия),
[ADO_NET_TESTS.md](../ADO_NET_TESTS.md) (внешние конформанс-наборы),
[ADR_DOTNET_EXTENSIONS.md](ADR_DOTNET_EXTENSIONS.md) (утверждённая .NET-граница),
[APPROVED_DEVIATIONS.md](APPROVED_DEVIATIONS.md) (D-029/D-030: владение базой).
[ROADMAP.md](ROADMAP.md) описывает ранний снимок (HEAD `627a7d2`) и по статусу устарел —
для текущего состояния опирайтесь на тесты и рабочие заметки.

## Содержание

1. [Как это устроено](#как-это-устроено)
2. [Соединение и строка подключения](#соединение-и-строка-подключения)
3. [H2DataSource — единственный правильный способ подключения](#h2datasource--единственный-правильный-способ-подключения)
4. [Метаданные GetSchema](#метаданные-getschema)
5. [Команды](#команды)
6. [Параметры — главное место, где можно ошибиться](#параметры--главное-место-где-можно-ошибиться)
7. [Чтение результатов](#чтение-результатов)
8. [Настройка проекции значений IH2DataFormat](#настройка-проекции-значений-ih2dataformat)
9. [Транзакции](#транзакции)
10. [Фабрика провайдера и DbBatch](#фабрика-провайдера-и-dbbatch)
11. [DbDataAdapter и H2CommandBuilder](#dbdataadapter-и-h2commandbuilder)
12. [Точки расширения .NET](#точки-расширения-net)
13. [Асинхронность](#асинхронность)
14. [Ограничения и известные отказы](#ограничения-и-известные-отказы)
15. [Шпаргалка: симптом → причина → действие](#шпаргалка-симптом--причина--действие)
16. [Готовые рецепты](#готовые-рецепты)

---

## Как это устроено

`H2Sharp.Data` — это тонкий адаптер над встроенным движком. Между вызывающим кодом и
сессией нет ни драйвера, ни сетевого протокола:

```
DbConnection / DbCommand / DbDataReader      ← ваш код, Dapper, ORM
        ↓  H2Connection / H2Command / H2DataReader
H2ParameterRewriter  ($name → $N)            ← только слой ADO.NET
        ↓
SessionLocal.PrepareCommand(text)            ← эталонный парсер H2 2.5
        ↓
CommandContainer → ResultInterface
        ↓
H2ValueMapper  (Value ↔ CLR-объект)          ← только слой ADO.NET
```

Два важных следствия:

* **Именованных параметров в движке нет.** Движок знает `?`, `?N` и `$N`. Всё, что пишется
  как `$name`, переписывается в `$N` до парсера (`H2ParameterRewriter`). Парсер и `Prepared`
  остаются эталонным кодом.
* **Значение параметра не превращается в `Value` при создании.** `H2Parameter.Value`
  хранит ровно то, что положил вызывающий, а преобразование происходит в момент выполнения,
  когда известен тип колонки, в которую значение попадает. Поэтому `DbType` у параметра —
  не «тип колонки», а подсказка для одного-единственного неоднозначного случая
  (`TimeSpan`, см. ниже).

Типы провайдера: `H2Connection`, `H2Command`, `H2Parameter` +
`H2ParameterCollection`, `H2DataReader`, `H2Transaction`, `H2ProviderFactory`,
`H2Batch`, `H2DataAdapter`, `H2CommandBuilder`, `H2DataSource`, `H2DataSourceBuilder`,
`H2ConnectionStringBuilder`.

---

## Соединение и строка подключения

### Минимальный пример

```csharp
using H2Sharp.Data;

using H2DataSource source = new H2DataSourceBuilder("Data Source=demo;Storage=Memory").Build();
using H2Connection connection = (H2Connection)source.CreateConnection();
connection.Open();

using var command = connection.CreateCommand();
command.CommandText = "SELECT 1";
Console.WriteLine(command.ExecuteScalar());   // 1
```

**Оба storage работают.** `Memory` — база в памяти на время жизни `H2DataSource`;
`File` — постоянная база в MVStore-файле, которая переживает переоткрытие. Файловое
хранилище перенесено целиком и проверено оракулом: файл, записанный H2Sharp, читает
эталонная Java H2 2.5.259, и наоборот (`FileDatabaseOracleTests`,
`AlterColumnOracleTests`, `FileStorageOracleTests`, `FileStoreTests`,
`MVStoreToolCoverageTests`).

```csharp
// Файловая база: данные сохраняются между запусками
using H2DataSource fileSource = new H2DataSourceBuilder(
    "Data Source=./data/app;Storage=File").Build();
using H2Connection fileConnection = (H2Connection)fileSource.CreateConnection();
fileConnection.Open();
```

Значение `Storage` по умолчанию — **`File`** (`H2ConnectionStringBuilder.GetStorage()`),
поэтому вид хранилища лучше задавать явно в каждой строке подключения: так намерение
видно и в конфигурации, и в логах. С файловым storage работают `FILE_LOCK`,
`AUTO_SERVER`, `IFEXISTS`, `FORBID_CREATION`, `LOCK_MODE`, `PAGE_SIZE`; два независимых
владельца одного файла конфликтуют по файловой блокировке (D-029).

### Формат

Принимается только классический `key=value` (`DbConnectionStringBuilder`). JDBC-URL
(`jdbc:h2:mem:...`) и учётные данные не принимаются: ключи `User ID`, `UserId`,
`Password`, `USER`, `PASSWORD` всегда дают `UnsupportedException` при валидации
(`H2ConnectionStringBuilder.ValidateKeys`).

| Ключ | Тип | По умолчанию | Что делает |
|---|---|---|---|
| `Data Source` / `DataSource` | string | `""` | Ключ in-memory базы или путь к файлу |
| `Storage` | `Memory` / `File` | **`File`** | Вид хранилища |
| `Mode` | имя режима | `Regular` | Режим совместимости: `Regular`, `Oracle`, `MySQL`, … (`Mode.GetInstance`) |
| `INIT` | SQL-скрипт | — | Выполняется при открытии базы |
| `IFEXISTS` | bool | false | Требовать уже существующую базу |
| `FORBID_CREATION` | bool | false | Запретить создание |
| `OPEN_NEW` | bool | false | Запросить новую базу |
| `IGNORE_UNKNOWN_SETTINGS` | bool | false | Не падать на незнакомых ключах |
| `IGNORE_CATALOGS` | bool | false | Игнорировать каталог в трёхчастных именах |
| `CASE_INSENSITIVE_IDENTIFIERS` | bool | false | Сравнивать идентификаторы без регистра |
| `LOCK_MODE` | int | `Constants.DefaultLockMode` | Режим блокировок |
| `PAGE_SIZE` | int | `Constants.DefaultPageSize` | Размер страницы |
| `ACCESS_MODE_DATA` | string | `rw` | Режим доступа к данным |
| `AUTO_SERVER` | bool | false | Автосерверный режим |
| `FILE_LOCK` | имя метода | — | Метод файловой блокировки |
| `CIPHER` | string | — | Имя шифра |

Неизвестный ключ без `IGNORE_UNKNOWN_SETTINGS=TRUE` — `ErrorCode.UnsupportedSetting1`.
Проверить ключ программно: `H2ConnectionStringBuilder.IsKnownSetting(name)`.

Проверенные строки из тестов:

```csharp
// режим совместимости (H2ConnectionModeTests)
"Data Source=oracle_case;Storage=Memory;Mode=Oracle"

// INIT-скрипт; кавычки обязательны, внутри них — SQL с точками с запятой (H2InitTests)
"Data Source=init_probe;Storage=Memory;INIT=\"CREATE TABLE T(ID INT);INSERT INTO T VALUES(42)\""

// незнакомые настройки не ломают открытие
"Data Source=agg;Storage=Memory;IGNORE_UNKNOWN_SETTINGS=TRUE"

// файловые базы: локальные временные таблицы (H2LocalTempTableTests)
"Data Source=" + path + ";Storage=File;IGNORE_UNKNOWN_SETTINGS=true"

// файловые базы: режимы блокировки (FileDatabaseOracleTests)
"Data Source=" + path + ";Storage=File;FILE_LOCK=SOCKET"
"Data Source=" + path + ";Storage=File;AUTO_SERVER=TRUE"
```

### Типизированный билдер

```csharp
using H2Sharp.Engine;   // Mode
using H2Sharp.Store;    // StorageKind
using H2Sharp.Data;

var builder = new H2ConnectionStringBuilder
{
    DataSource = "demo",
    Storage = StorageKind.Memory,
    Mode = Mode.GetRegular(),
    IgnoreUnknownSettings = true,
};
using H2DataSource source = new H2DataSourceBuilder(builder.ConnectionString).Build();
using H2Connection connection = (H2Connection)source.CreateConnection();
```

`ToConnectionInfo()` возвращает типизированный `ConnectionInfo` и валидирует ключи;
`LoadFrom(info)` — обратная операция. `IsKnownSetting` пригодится, если строка
приходит из конфигурации и вы хотите отличить опечатку от чужого ключа.

### Жизненный цикл

* Соединение, выданное `H2DataSource.CreateConnection()`, **владеет заблокированной
  строкой подключения**: попытка её заменить даёт
  `"The connection string is owned by the data source and cannot be changed"`. Меняйте
  строку в `H2DataSourceBuilder`, а не в соединении.
* У прямого конструктора `H2Connection` другая крайность: `new H2Connection(null!)` даёт
  `ArgumentNullException`, тогда как `connection.ConnectionString = null` допустимо
  (`[AllowNull]`) и означает «сбросить в пустую строку». Это деталь реализации, а не
  рекомендуемый путь подключения.
* Менять `ConnectionString` напрямую можно только на закрытом соединении.
* `Open()` дважды → `InvalidOperationException("The connection is already open")`.
* `Close()` идемпотентентно: повторный вызов и закрытие никогда не открывавшегося соединения
  ничего не делают — именно поэтому `using` безопасен.
* `Dispose()` сначала закрывает, иначе сессия осталась бы жить без владельца.
* `State` принимает только `Closed` и `Open`.
* `Database` — короткое имя базы, `DataSource` — сама строка подключения, `ServerVersion` —
  `"2.5.259"`. Все три до открытия либо пустые, либо (для `ServerVersion`) бросают
  `InvalidOperationException`.
* `ChangeDatabase()` **не поддерживается**: база задаётся строкой подключения при открытии.
* При открытии внутри области `Transaction.Current` соединение автоматически зачисляется
  в ambient-транзакцию (auto-enlist), см. раздел про транзакции.

---

## H2DataSource — единственный правильный способ подключения

`H2DataSource` — **владелец базы и точка входа в провайдер**. Он один раз создаёт
`Database`, удерживает её (`SetCloseDelay(-1)`) и раздаёт соединения; база живёт, пока жив
data source, и исчезает только при его `Dispose()`. Все соединения, выданные одним data
source, работают с одним экземпляром `Engine.Database`.

Поэтому прикладной код должен создавать соединения **только** через
`H2DataSourceBuilder(...).Build()` → `CreateConnection()`. Прямой
`new H2Connection(...)` создаёт самодостаточную базу на одно соединение и не делит
состояние ни с кем: два таких соединения к одному `mem:`-имени независимы, а к одному
файлу — упираются в файловую блокировку. Это служебный путь, который встречается в
 Unit-тестах самого порта, но не является способом подключения к базе приложения
(см. [APPROVED_DEVIATIONS.md](APPROVED_DEVIATIONS.md), D-029 и D-030).

```csharp
using H2DataSource source = new H2DataSourceBuilder("Data Source=shared;Storage=Memory")
    .Build();

using H2Connection first  = (H2Connection)source.CreateConnection();
using H2Connection second = (H2Connection)source.CreateConnection();
first.Open();
second.Open();

// first и second работают с одним экземпляром Engine.Database
```

Это закреплено тестом `H2ConnectionBasicsTests.DataSourceSharesOneNamedInMemoryDatabase`:
`Assert.Same(first.CurrentDatabase, second.CurrentDatabase)`, но `NotSame` для «сырого»
`H2Connection` с тем же именем. В тестах репозитория для этой цели есть хелпер
`H2TestDataSource.Open(...)` — он тоже строит `H2DataSource`.

Для файлового хранилища это особенно важно: второй data source к тому же файлу
конфликтует по файловой блокировке, поэтому один файл = один `H2DataSource` на процесс.
Данные при этом сохраняются между переоткрытиями и читаются эталонной Java H2.

`H2DataSourceBuilder` умеет централизованно настраивать **каждое** новое соединение:

```csharp
using H2DataSource source = new H2DataSourceBuilder("Data Source=app;Storage=Memory")
    .ConfigureExtensions(ext => ext.RegisterUdf<int, int>("DOUBLE_IT", x => x * 2))
    .ConfigureDataFormat(fmt => fmt.JsonFormat = H2JsonFormat.String)
    .Build();
```

Действия выполняются сразу после создания соединения и **до** его возврата, то есть
соединение ещё закрыто — это единственный момент, когда можно менять опции. Порядок:
сначала все `ConfigureExtensions`, затем все `ConfigureDataFormat`, в порядке регистрации.
Важно: проекция принадлежит соединению, а не базе, поэтому действие должно выставлять
свойства, а не «шарить» один изменяемый объект между соединениями.

Дополнительно у `H2DataSource` есть `RegisterTrigger`, `UnregisterTrigger`,
`SetDatabaseEventListener`, `SetTraceListener` — регистрации уровня базы, они применяются
и к таблицам, созданным позже.

---

## Метаданные GetSchema

`H2Connection.GetSchema()` обёртывает эталонный `DatabaseMeta`. Поддерживаемые коллекции
и число ограничений (`H2Connection.SchemaCollections`):

| Коллекция | Ограничения |
|---|---|
| `MetaDataCollections`, `Restrictions`, `DataTypes`, `ReservedWords` | 0 |
| `Tables` | 4: Catalog, Schema, Table, TableType |
| `Columns` | 4: Catalog, Schema, Table, Column |
| `Indexes` | 4: Catalog, Schema, Table, Index |
| `ForeignKeys` | 3: Catalog, Schema, Table |
| `Procedures` | 3: Catalog, Schema, Procedure |
| `TableTypes`, `Catalogs` | 0 |
| `Schemas` | 2: Catalog, Schema |

```csharp
DataTable collections = connection.GetSchema();
DataTable tables   = connection.GetSchema("Tables",   [null, "PUBLIC", null, null]);
DataTable columns  = connection.GetSchema("Columns",  [null, "PUBLIC", "P", null]);
DataTable schemas  = connection.GetSchema("Schemas",  [null, "PUBLIC"]);
DataTable words    = connection.GetSchema(DbMetaDataCollectionNames.ReservedWords);
```

Имена коллекций сравниваются без регистра. Передать больше ограничений, чем положено —
`ArgumentException`. `Indexes` и `ForeignKeys` **требуют** массив ограничений (проверено в
`H2GetSchemaTests`). `GetSchemaAsync` — это `Task.FromResult(GetSchema(...))`.

На уровне ридера есть `GetSchemaTable()`, `GetColumnSchemaAsync()` и явная реализация
`IDbColumnSchemaGenerator.GetColumnSchema()`. Схема несёт `BaseSchemaName`,
`BaseTableName`, `BaseColumnName`, `IsAutoIncrement`, `DataTypeName`; у вычисленных
выражений базовых имён нет (`DBNull.Value`) — по этому признаку `H2CommandBuilder`
отличает колонку таблицы от вычисленного поля.

---

## Команды

```csharp
using H2Command command = new("CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR(20))", connection);
command.ExecuteNonQuery();
```

* `CommandType` — только `Text`; хранимых процедур у провайдера нет.
* `Prepare()` **не компилирует** запрос: разбор происходит при выполнении. Метод лишь
  проверяет, что соединение открыто и текст непустой.
* `Cancel()` передаёт отмену в `Session.Cancel()` — выполняющийся оператор прервётся
  на ближайшей точке проверки.
* `CommandTimeout` по умолчанию `0` — без ограничения.

### Три способа выполнить

| Метод | Поведение |
|---|---|
| `ExecuteNonQuery()` | Возвращает число затронутых строк первого оператора. Если первый оператор — запрос, он всё равно выполняется, строки вычитываются и отбрасываются, а результат `-1`. |
| `ExecuteScalar()` | Первая колонка первой строки. Для не-запроса — `null`. Для запроса без строк — `null`. Значение «отсоединено» от результата: потоки и ридеры возвращаются копиями, потому что результат закрывается до выхода из метода. |
| `ExecuteReader()` | Ридер поверх результата. На одной команде может быть **только один** открытый ридер. |

### Несколько операторов в одном тексте

Текст разбирается в список `CommandContainer`. Первый оператор «отвечает», остальные
исполняются «для эффекта»: запросы вычитываются и дренируются, обновления считаются.
При `ExecuteReader()` оставшиеся операторы выполняются **лениво**, по мере
`NextResult()` — если вызывающий не дойдёт до них, побочных эффектов не будет.

```csharp
using DbCommand command = connection.CreateCommand();
command.CommandText = "SELECT $a; SELECT $b";
// параметры $a и $b нумеруются сквозняком по всему тексту
using DbDataReader reader = command.ExecuteReader();
reader.Read();                       // 1
reader.NextResult();
reader.Read();                       // 2
```

Пустой текст, пробелы и голые `;` превращаются в `NoOperation` (update count `0`), а не в
ошибку. `CommandBehavior` поддерживает `SingleRow` (лимит 1 строки), `SchemaOnly`
(лимит 0), `SingleResult` (не отдавать остальные наборы), `CloseConnection` (закрыть
соединение вместе с ридером).

Менять `CommandText` при открытом ридере **можно** (как в SqlClient), но попытка
выполнить команду с уже открытым ридером даёт
`InvalidOperationException("The command already has an open reader")`. Повторное
выполнение после закрытия ридера разрешено.

---

## Параметры — главное место, где можно ошибиться

### Правила

Провайдер пишет именованный параметр как **`$name`**. `@name` — переменная сессии H2,
`:name` — обычный синтаксис H2 (например, `JSON_OBJECT('key': value)`); ни то, ни другое
параметром не является и не подменяется.

```csharp
using DbCommand command = connection.CreateCommand();
command.CommandText = "INSERT INTO T VALUES ($id, $name)";
var id = command.CreateParameter();
id.ParameterName = "$id";           // ведущий '$' необязателен: подойдёт и "id"
id.Value = 1;
command.Parameters.Add(id);
var name = command.CreateParameter();
name.ParameterName = "$name";
name.Value = "bolt";
command.Parameters.Add(name);
command.ExecuteNonQuery();
```

Что важно:

1. **Имена регистрозависимы.** `$Id` и `$id` — разные параметры; поиск в коллекции идёт
   по `StringComparison.Ordinal`. Ведущий `$` в `ParameterName` отбрасывается при
   сравнении, поэтому `command.Parameters["Id"]` найдёт параметр `"$Id"`. А вот `@Id` и
   `:Id` не находятся и не нормализуются — так ошибка вызывающего не маскируется.
2. **Значения привязываются по имени, а не по порядку коллекции.** Можно добавить `b`
   раньше `a` — `SELECT $a + $b` всё равно даст верную сумму.
3. **Повтор имени = один индекс.** `SELECT $value + $value` с одним параметром `value`
   работает; `$a` в трёх строках `INSERT ... VALUES ($a,...), ($b,...)` связывается один раз.
4. **Пропущенное значение — ошибка**, а не NULL: `No value was given for parameter '$x'`.
   Голый `$` или `$` перед недопустимым символом — `A '$' parameter must have a name or an index`.
5. **`null` в `Value` — ошибка**: `Parameter '...' has no value; use DBNull.Value for NULL`.
   Для SQL NULL кладите `DBNull.Value`.
6. **Нельзя смешивать** именованные (`$name`) и позиционные (`?`, `?N`, `$1`) параметры
   в одной команде: `Named and positional parameters cannot be mixed in one command`.
   Если в тексте вообще нет `$`, значения коллекции просто идут по порядку на места `?`.
7. **Сканирование токенами, а не замена текста.** `$` внутри строки, `$$...$$`,
   идентификатора `a$b`, комментария `--`, `//` или вложенного `/* ... */` не трогается.
   Проверено тестом `H2NamedParameterTests.DollarInsideLiteralsIdentifiersAndCommentsIsNotAParameter`.
8. `H2Parameter.DbType` по умолчанию `DbType.String`. `ResetDbType()` сбрасывает подсказку.
   Направление — `Input`, `SourceVersion` — `Current`.
9. `command.Parameters` — один и тот же объект при каждом обращении (важно для Dapper).
   `SetParameter(name, value)` при неизвестном имени **добавляет** параметр, а не бросает.

### Позиционные параметры

```csharp
command.CommandText = "INSERT INTO T VALUES(?, ?)";
command.Parameters.Add(new H2Parameter { Value = 1 });
command.Parameters.Add(new H2Parameter { Value = "n1" });
```

Имена здесь не нужны: значения берутся в порядке коллекции. Именно так работает
`H2CommandBuilder` (плейсхолдер `?`, имена `p1`, `p2` — только метки).

### Таблица преобразования значения параметра

`H2ValueMapper.ToValue` (выполняется в момент запуска оператора):

| CLR-значение | Значение движка |
|---|---|
| `null`, `DBNull.Value` | `NULL` |
| enum | базовый числовой тип |
| `bool` | BOOLEAN |
| `byte` | SMALLINT (0..255 не влезает в TINYINT) |
| `sbyte` / `short` / `int` / `long` | TINYINT / SMALLINT / INTEGER / BIGINT |
| `BigInteger` | NUMERIC(scale 0) |
| `float` / `double` / `decimal` | REAL / DOUBLE / NUMERIC |
| `char` / `string` | CHAR / VARCHAR |
| `byte[]` | VARBINARY |
| `JsonDocument`, `JsonElement` | JSON-документ |
| `DateTime` / `DateOnly` / `TimeOnly` | TIMESTAMP / DATE / TIME |
| `TimeSpan` | **INTERVAL DAY TO SECOND**, но TIME при `DbType.Time` |
| `DateTimeOffset` | TIMESTAMP WITH TIME ZONE |
| `Guid` | UUID |
| `Stream` / `TextReader` | BLOB / CLOB (читаются в момент выполнения) |
| `IEnumerable` (массив, `List<T>`, ленивая последовательность) | ARRAY |
| прочее | `InvalidOperationException` |

Движок затем приводит значение к типу колонки назначения — поэтому «лишнее» уточнение
`DbType` обычно не требуется.

### Трюк: `TimeSpan` — время суток или длительность?

В .NET оба понятия выражены `TimeSpan`, а в H2 это разные типы. Решает `DbType`
(тест `H2TimeParameterTests`):

```csharp
p.Value = new TimeSpan(12, 34, 56);
p.DbType = DbType.Time;     // → TIME (время суток), попадёт в колонку TIME
// без DbType.Time          → INTERVAL DAY TO SECOND (длительность)
p.Value = new TimeOnly(10, 0);   // → TIME всегда, без подсказки
```

`H2CommandBuilder.ApplyParameterInfo` выставляет `DbType.Time` автоматически, когда
схема говорит, что колонка объявлена как `TIME`.

### Массивы

Параметр принимает CLR-массив, `List<T>` и любой `IEnumerable`; `string` и `byte[]`
остаются скалярами. Вложенные SQL-массивы — **только зубчатые** (`int[][]`), прямоугольные
`int[,]` дают `NotSupportedException`. Пустые и полностью NULL-последовательности
сохраняют тип элемента благодаря CLR-типу коллекции.

С Dapper есть развилка (тест `H2ArrayTests`):

```csharp
// 1) один нативный SQL ARRAY
var p = new DynamicParameters();
p.Add("numbers", new List<int> { 10, 20 }, DbType.Object);
connection.Execute("INSERT INTO T VALUES ($id, $numbers)", p);

// 2) раскрытие списка в IN (...) — БЕЗ DbType.Object
connection.Query<int>("SELECT ID FROM T WHERE ID IN $ids ORDER BY ID", new { ids = new[] { 1, 2 } });
```

Явный `DbType.Object` говорит Dapper «передай один параметр», без него Dapper
раскрывает последовательность в набор плейсхолдеров. Для nullable-массивов один раз
настройте типизированный путь чтения:

```csharp
Dapper.SqlMapper.AddTypeMap(typeof(int?[]), DbType.Object, useGetFieldValue: true);
```

### JSON

`GetValue()`/`ExecuteScalar()` по умолчанию отдают UTF-8 `byte[]` (как JDBC H2 2.5).
SQL NULL → `DBNull.Value`; JSON `null` — четырёхбайтовый документ `null`.

В параметры можно класть `byte[]`, `JsonDocument`, `JsonElement` (с `DbType.Object`
при работе через Dapper). **Обычная `string` становится JSON-строкой-значением**, даже
если внутри неё лежит объект — это правило преобразования H2. Для текста используйте
`CAST(document AS VARCHAR)`.

### Переменные сессии и двоеточие

```sql
SET @v = 10;
SELECT @v + $x;                          -- @v не параметр
SELECT CAST(JSON_OBJECT('value': $id) AS VARCHAR);  -- ':' не параметр
```

---

## Чтение результатов

`GetValue(i)` возвращает CLR-объект, `null` SQL-значения — `DBNull.Value`.
`GetFieldType(i)` берётся из `H2ValueMapper.GetFieldType` и не зависит от содержимого
конкретной строки:

| SQL-тип | CLR-тип по умолчанию |
|---|---|
| BOOLEAN / TINYINT / SMALLINT / INTEGER / BIGINT | `bool` / `sbyte` / `short` / `int` / `long` |
| NUMERIC, DECFLOAT | `decimal` |
| REAL / DOUBLE | `float` / `double` |
| CHAR, VARCHAR, CLOB, ENUM | `string` |
| BINARY, VARBINARY, BLOB | `byte[]` |
| JSON | `byte[]` |
| UUID | `Guid` |
| DATE | `DateTime` (`Unspecified`) |
| TIME | `TimeSpan` |
| TIMESTAMP | `DateTime` (`Unspecified`) |
| TIMESTAMP WITH TIME ZONE | `DateTimeOffset` |
| INTERVAL DAY TO SECOND | `TimeSpan` |
| ARRAY | типизированный CLR-массив |
| ROW | `object?[]` |

`GetDataTypeName(i)` возвращает объявленное имя типа H2 (например,
`"INTERVAL DAY TO SECOND"`).

```csharp
using DbDataReader reader = command.ExecuteReader();
while (reader.Read())
{
    object value = reader.GetValue(0);          // DBNull.Value для NULL
    string label = reader.GetName(0);           // алиас колонки, как JDBC getColumnLabel
    int ordinal  = reader.GetOrdinal("NAME");   // сначала алиасы, потом базовые имена
}
```

`GetOrdinal` ищет **сначала по алиасам, затем по базовым именам**, без регистра, и бросает
`IndexOutOfRangeException`, если не нашёл. `GetName` возвращает именно алиас;
базовое имя доступно через `GetSchemaTable()`/`GetColumnSchema()`.

### Явные геттеры

`GetFieldValue<T>` понимает больше, чем базовая реализация:

```csharp
reader.GetFieldValue<string>(0)        // текст для JSON/CLOB/BLOB
reader.GetFieldValue<byte[]>(0)        // байты для BINARY/VARBINARY/BLOB/JSON/CLOB
reader.GetFieldValue<JsonDocument>(0)  // независимый документ — его надо Dispose()
reader.GetFieldValue<int[]>(0)         // SQL ARRAY
reader.GetFieldValue<List<int>>(0)     // SQL ARRAY как список
reader.GetFieldValue<Stream>(0)        // == GetStream(0)
reader.GetFieldValue<TextReader>(0)    // == GetTextReader(0)
```

Для LOB/JSON также работают `GetString`, `GetBytes`, `GetChars`, `GetStream`,
`GetTextReader`. `ExecuteScalar()` закрывает результат до возврата, поэтому потоки и
ридеры там приходят **отсоединёнными копиями** (`MemoryStream`, `StringReader`,
независимый `JsonDocument`).

### Массивы с NULL-элементами

```csharp
// колонка INTEGER ARRAY со значениями {1, NULL, 3}
reader.GetFieldValue<int?[]>(0);   // OK: [1, null, 3]
reader.GetFieldValue<int[]>(0);    // InvalidCastException
```

`GetValue()` сам вернёт `int?[]`, а `GetFieldType()` при этом сообщает тип
**не-nullable** элемента — не полагайтесь на `GetFieldType` при материализации
массивов через Dapper, используйте `GetFieldValue<T>`.

### RecordsAffected и несколько наборов

У ридера поверх запроса `RecordsAffected == -1`; у ридера поверх обновления — число
изменённых строк. `NextResult()` лениво выполняет следующий оператор из текста команды.

---

## Настройка проекции значений IH2DataFormat

Интерфейс реализован **явно**, поэтому обычный IntelliSense соединения не засорён
настройками. Нужно привести тип:

```csharp
using H2DataSource source = new H2DataSourceBuilder("Data Source=fmt;Storage=Memory").Build();
var connection = (H2Connection)source.CreateConnection();
((IH2DataFormat)connection).BlobFormat = H2BlobFormat.Stream;
((IH2DataFormat)connection).JsonFormat = H2JsonFormat.String;
((IH2DataFormat)connection).ArrayFormat = H2ArrayFormat.List;
connection.Open();      // снимок настроек фиксируется здесь
```

Правила (закреплены в `H2DataFormatTests`):

* менять можно **только на закрытом, не уничтоженном** соединении:
  `InvalidOperationException` после `Open()`, `ObjectDisposedException` после `Dispose()`;
* настройка принадлежит **соединению**, а не базе: два соединения к одной базе могут
  читать JSON по-разному;
* влияет только на проекцию ADO.NET (`GetValue`, `GetValues`, `ExecuteScalar`,
  `GetFieldType`, `GetColumnSchema`, `GetSchemaTable`). Явные геттеры
  (`GetString`, `GetBytes`, `GetStream`, `GetTextReader`, `GetFieldValue<T>`) сохраняют
  эталонное поведение;
* движок, SQL, хранимые значения и параметры не затрагиваются.

| Свойство | Значения | По умолчанию |
|---|---|---|
| `BlobFormat` | `ByteArray`, `Stream` | `ByteArray` |
| `ClobFormat` | `String`, `TextReader` | `String` |
| `JsonFormat` | `ByteArray`, `String`, `TextReader`, `Stream`, `JsonDocument` | `ByteArray` |
| `ArrayFormat` | `Array`, `List` | `Array` |
| `DateFormat`, `TimeTzFormat`, `TimestampFormat`, `TimestampTzFormat` | `DateTimeUnspecified`, `DateTimeUtc`, `DateTimeLocal`, `DateTimeOffset` | stage-зависимо, см. XML-документацию интерфейса |
| `TimeFormat` | `TimeSpan`, `DateTimeUnspecified`, `DateTimeUtc`, `DateTimeLocal`, `DateTimeOffset` | `TimeSpan` |

Подводные камни: `BlobFormat.Stream` и `ClobFormat.TextReader` отдают **живой** поток LOB —
его надо потребить до перехода к следующей строке; `JsonFormat.JsonDocument` создаёт
независимый документ **на каждое чтение**, вызывающий обязан его `Dispose()`;
`ArrayFormat.List` применяется только к внешнему массиву, а массивы с компонентом
BINARY/VARBINARY/BLOB/JSON остаются CLR-массивами.

Чтобы не кастовать в каждом месте, настройте все соединения централизованно:

```csharp
using H2DataSource source = new H2DataSourceBuilder("Data Source=app;Storage=Memory")
    .ConfigureDataFormat(f => f.JsonFormat = H2JsonFormat.String)
    .Build();
```

---

## Транзакции

### Обязательно связывайте команду с транзакцией

Это самое частое отличие от SqlClient: если на соединении открыта транзакция, а у
команды `Transaction == null`, выполнение даёт
`InvalidOperationException("The connection has an open transaction")`.

```csharp
using DbTransaction transaction = connection.BeginTransaction();
using (H2Command insert = new("INSERT INTO T VALUES (2, 'x')", connection))
{
    insert.Transaction = transaction;        // ОБЯЗАТЕЛЬНО
    insert.ExecuteNonQuery();
}
transaction.Commit();
```

Через Dapper то же самое делается параметром `transaction:`:

```csharp
connection.Execute("INSERT INTO T(ID, V) VALUES (1, 10)", transaction: tx);
```

Транзакция с другого соединения при выполнении даёт
`"The transaction belongs to another connection"`. `BeginTransaction()` при уже открытой
транзакции — `"The connection already has a transaction"`.

### Поведение

* `BeginTransaction(IsolationLevel.Unspecified)` берёт текущий уровень сессии; любой
  явно указанный уровень выставляется на сессии и сохраняется для следующих транзакций.
* `Commit()`/`Rollback()` снимают транзакцию с соединения; повторное завершение —
  `InvalidOperationException`. `Dispose()` без завершения **откатывает**.
* `SupportsSavepoints == true`: `Save(name)`, `Rollback(name)`. `Release(name)` — no-op
  (в эталоне `parseReleaseSavepoint` возвращает `NoOperation`).
* DML действительно изолирован и откатывается (`H2TransactionSemanticsTests`).
* **DDL коммитит транзакцию.** Это поведение H2 2.5.x: `CREATE TABLE`, `DROP TABLE` и
  большинство DDL фиксируют открытую транзакцию вместе со своей работой, поэтому
  последующий `Rollback()` их не отменяет. Не рассчитывайте на откат миграций.
* Триггер не может коммитить/откатывать транзакцию оператора, внутри которого сработал
  (`blockCompletion`).

### Ambient-транзакции

Соединение, открытое внутри `TransactionScope`, зачисляется в него автоматически. Уже
открытое соединение можно зачислить явно:

```csharp
using (var scope = new TransactionScope())
{
    connection.EnlistTransaction(Transaction.Current);
    // ...
    scope.Complete();
}
```

Пока соединение зачислено, `BeginTransaction()` отказывает. `Close()` в зачисленном
соединении лишь отсоединяет его: сессию удерживает зачисление до уведомления о завершении.

---

## Фабрика провайдера и DbBatch

```csharp
DbProviderFactory factory = H2ProviderFactory.Instance;

// Правильный путь: фабрика создаёт data source, а он — соединения.
using DbDataSource source = factory.CreateDataSource("Data Source=app;Storage=Memory");
using DbConnection connection = source.CreateConnection();

// Остальные объекты провайдера тоже доступны через фабрику:
using DbCommand command = factory.CreateCommand();
using DbParameter parameter = factory.CreateParameter();
using DbBatch batch = factory.CreateBatch();
```

`H2ProviderFactory` — синглтон без состояния. Он умеет создавать соединение, команду,
параметр, `H2ConnectionStringBuilder`, `H2DataAdapter`, `H2CommandBuilder`, `H2Batch` и
`H2DataSource` (`CreateDataSource(connectionString)`), но **не** умеет перечислять
источники данных (`CanCreateDataSourceEnumerator == false`).

`CreateConnection()` у фабрики существует ради контракта `DbProviderFactory`; для прикладного
кода это не точка входа — соединение должно приходить от `H2DataSource`, иначе база не
будет разделяться. `CreateDataSource(...)` внутри строит `H2DataSourceBuilder(...).Build()`,
поэтому это тот же рекомендуемый путь.

### DbBatch

```csharp
using H2Batch batch = (H2Batch)connection.CreateBatch();
for (int i = 0; i < 3; i++)
{
    DbBatchCommand item = batch.CreateBatchCommand();
    item.CommandText = "INSERT INTO T VALUES(?, ?)";
    item.Parameters.Add(new H2Parameter { Value = i });
    item.Parameters.Add(new H2Parameter { Value = "n" + i });
}
int affected = batch.ExecuteNonQuery();   // 3
```

Как это работает (`H2Batch.GroupByText`):

* команды с **одинаковым текстом** (сравнение `Ordinal`) группируются и уходят в движок
  одним `ExecuteBatch` с набором параметров — это настоящий батч, а не цикл;
* команды с разным текстом выполняются по одной, в порядке добавления;
* `ExecuteScalar()` и `ExecuteReader()` используют **только первую** команду, остальные
  выполняются для эффекта;
* `Prepare()` и `Cancel()` — пустые заглушки; async-варианты — `Task.FromResult(...)`;
* при ошибке одного набора успешные сохраняются, а первое исключение бросается после
  подсчёта (`H2BatchTests.AFailedSetReportsAndKeepsTheSuccessfulOnes`).

---

## DbDataAdapter и H2CommandBuilder

```csharp
using H2DataSource source = new H2DataSourceBuilder("Data Source=adapter;Storage=Memory").Build();
using H2Connection connection = (H2Connection)source.CreateConnection();
connection.Open();
// таблица создана заранее, есть PRIMARY KEY

using H2DataAdapter adapter = new("SELECT ID, NAME FROM T", connection);
using H2CommandBuilder builder = new(adapter);

DataTable table = new();
adapter.Fill(table);
table.Rows.Add(1, "a", DBNull.Value);
adapter.Update(table);              // INSERT сгенерирован автоматически

table.Rows[0]["NAME"] = "b";
adapter.Update(table);              // UPDATE

table.Rows[0].Delete();
adapter.Update(table);              // DELETE
```

Что именно задаёт `H2CommandBuilder`:

* `QuotePrefix`/`QuoteSuffix` = `"` — идентификаторы H2 в двойных кавычках, регистр
  сохраняется (`"lower"."Name"`);
* `GetParameterPlaceholder` = `?`, `GetParameterName` = `p1`, `p2`, … — имена параметров
  являются метками и в текст SQL не попадают;
* `GetSchemaTable` дополняет схему ридера флагами `IsKey` (из первичного ключа таблицы),
  `IsReadOnly` (generated-колонки), `IsAutoIncrement` (identity);
* identity и generated-колонки исключаются из `INSERT` и из `SET` в `UPDATE`;
* таблица **без первичного ключа** не получает ключевых колонок, и `UPDATE`/`DELETE`
  отказывает, вместо того чтобы сгенерировать `WHERE`, который заденет лишние строки;
* `ApplyParameterInfo` выставляет `DbType.Time` для TIME-колонок (см. трюк с `TimeSpan`).

`H2DataAdapter` дополнительно объявляет типизированные события `RowUpdating` и
`RowUpdated` (`H2RowUpdatingEventArgs` / `H2RowUpdatedEventArgs`).

---

## Точки расширения .NET

Интерфейсы `IH2Extensions`, `IH2DataFormat` и `IH2Test` реализованы **явно** —
приводите соединение к интерфейсу. Все регистрации делаются **до `Open()`**: открытие
делает неизменяемый снимок конфигурации, чтобы сессия не перестраивалась под
выполняющимся оператором.

### UDF и сервисы

```csharp
using H2DataSource source = new H2DataSourceBuilder("Data Source=udf;Storage=Memory").Build();
var connection = (H2Connection)source.CreateConnection();
((IH2Extensions)connection).RegisterUdf<int, int>("DOUBLE_IT", x => x * 2);
((IH2Extensions)connection).RegisterUdf<string?, string>("PREFIX", s => "x:" + s);
connection.Open();
// SELECT DOUBLE_IT(3) → 6 ; SELECT PREFIX('a') → 'x:a'
```

Доступны `RegisterUdf` арности 0–4 с обычными `Func<...>` (без собственных delegate-типов),
`RegisterAggregate(name, IH2AggregateProvider)`, `RegisterHashAlgorithm(name, Func<HashAlgorithm>)`,
`RegisterRandomGenerator(Func<long?, Random>)`, `RegisterUuidGenerator(Func<int, Guid>)`.

Это **не** `CREATE ALIAS`: зарегистрированные функции живут в сессии, не становятся
объектами схемы и не видны из другого соединения/базы. Пакет `H2Sharp.JavaLegacy`
добавляет `UseJavaLegacy()` — JDK-совместимую последовательность `RAND(seed)` и `ORA_HASH`:

```csharp
using H2Sharp.JavaLegacy;
((IH2Extensions)connection).UseJavaLegacy();
```

### Тестовые швы IH2Test

```csharp
((IH2Test)connection).SystemClockProvider = () => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
((IH2Test)connection).RandomGeneratorProvider = seed => new Random(seed ?? 0);
((IH2Test)connection).UuidProvider = () => Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
((IH2Test)connection).TimeZoneProviderProvider = () => /* TimeZoneProvider */;
connection.Open();
```

Так тесты сравнивают порт с JVM-оракулом, не получая разные `CURRENT_TIMESTAMP`,
`RAND()` и `RANDOM_UUID()` на каждом прогоне (подробнее —
[REGRESSION_TEST_PROTOCOL.md](REGRESSION_TEST_PROTOCOL.md)).

---

## Асинхронность

`OpenAsync`, `ExecuteNonQueryAsync`, `ExecuteScalarAsync`, `ExecuteReaderAsync`,
`GetSchemaAsync`, `IsDBNullAsync`, `GetFieldValueAsync` реализованы, но **синхронны под
капотом**: реализация провайдера не имеет `await`-точек. `OpenAsync()` вызывает
синхронный `Open()` (в том числе для файлового хранилища), `SyncTask.Run` выполняет работу
сразу и оборачивает результат в `Task.FromResult`, исключение — в `Task.FromException`,
а отменённый токен — в `Task.FromCanceled` **до начала работы**.

Следствия:

* `await` не снимает нагрузку с вызывающего потока;
* отмена уже начавшегося оператора через токен не работает — для этого есть
  `H2Command.Cancel()` (уходит в `Session.Cancel()`);
* `ExecuteReaderAsync` не даёт выигрыша даже на файловой базе: ввод-вывод выполняется
  синхронно внутри `Open()`/выполнения оператора, а обёртка лишь возвращает готовый
  `Task`;
* `H2Batch.PrepareAsync`/`Cancel` и `H2DataSource`-методы тоже не вводят реальной
  асинхронности;
* комментарий в `H2Connection.OpenAsync` про «только in-memory базы» описывает ранний
  этап порта и устарел — файловое хранилище работает, но открытие осталось синхронным.

---

## Ограничения и известные отказы

Порт активно развивается: покрытие кода тестами ≈92% и растёт, SQL-поверхность,
ADO.NET-слой и файловое хранилище MVStore перенесены и проверены оракулом. Оставшиеся
расхождения — это конкретные дефекты реализации, а не свойства базы; они фиксируются в
[MIGRATION_STATUS.md](../MIGRATION_STATUS.md) и тестах. [ROADMAP.md](ROADMAP.md)
описывает ранний снимок и по статусу устарел.

Что на самом деле ограничено или отсутствует:

* **`TODOException` — честный отказ, а не «тихо другой ответ».** Например,
  `H2ValueMapper` пока не может отдать CLR-значение для интервала `YEAR TO MONTH`
  (у него нет `TimeSpan`-эквивалента) и для неизвестного типа движка.
* `CommandType.StoredProcedure` не поддерживается; `Prepare()` ничего не кэширует;
  `CommandTimeout` не ограничивает выполнение; `ChangeDatabase()` бросает
  `NotSupportedException`.
* Учётные данные в строке подключения запрещены; пула соединений нет; `DbDataSourceEnumerator`
  не реализован.
* `GetSchema()` требует открытого соединения; `ServerVersion` до открытия бросает
  исключение, а не возвращает строку.
* DDL коммитит транзакцию (см. выше).

---

## Шпаргалка: симптом → причина → действие

| Симптом | Причина | Что делать |
|---|---|---|
| `No value was given for parameter '$x'` | в тексте есть `$x`, параметр не добавлен | добавить `H2Parameter` с именем `x` или `$x` |
| `... has no value; use DBNull.Value for NULL` | `Value = null` | `Value = DBNull.Value` |
| `Named and positional parameters cannot be mixed` | в одной команде `$name` и `?` / `$1` | привести все плейсхолдеры к одному стилю |
| `$Id` не находится при `new { Id = 1 }` (Dapper) | имена регистрозависимы | соблюдать регистр; Dapper передаёт имена свойств как есть |
| `@p` / `:p` не подставились | это не префиксы параметров в H2 | использовать `$p` |
| `The connection has an open transaction` | у команды не выставлен `Transaction` | `cmd.Transaction = tx` или `transaction: tx` в Dapper |
| `The command already has an open reader` | повторный `ExecuteReader` на той же команде | закрыть/`Dispose()` предыдущий ридер или взять новую команду |
| `CREATE TABLE` осталась после `Rollback()` | DDL коммитит транзакцию в H2 2.5 | не откатывать DDL; создавать схему до транзакции |
| `InvalidCastException` на `GetFieldValue<int[]>` | в массиве есть NULL-элементы | `GetFieldValue<int?[]>` |
| `TimeSpan` записался как `INTERVAL` вместо `TIME` | не объявлен `DbType.Time` | `p.DbType = DbType.Time` или используйте `TimeOnly` |
| JSON-объект из строки превратился в JSON-строку | `string` — скалярное значение по правилам H2 | `JsonDocument`/`JsonElement` с `DbType.Object`, либо `byte[]` |
| Dapper развернул массив в `IN (...)` вместо одного ARRAY | не указан `DbType.Object` | `DynamicParameters.Add(..., DbType.Object)` |
| `InvalidOperationException` при смене `JsonFormat` | соединение уже открыто | настраивать до `Open()`, лучше через `ConfigureDataFormat` |
| `NotSupportedException` на `int[,]` | прямоугольные массивы не поддерживаются | зубчатые `int[][]` |
| `UnsupportedSetting1` | неизвестный ключ строки подключения | проверить `IsKnownSetting`, либо `IGNORE_UNKNOWN_SETTINGS=TRUE` |
| Таблицы «не видны» из второго соединения | соединения созданы напрямую через `new H2Connection` | все соединения брать у одного `H2DataSource` |
| Файл заблокирован при открытии | два владельца (`H2DataSource`) на один и тот же файл | один data source на файл; строку задавать в билдере |
| `The connection string is owned by the data source...` | попытка сменить строку у выданного соединения | менять строку в `H2DataSourceBuilder`, не в соединении |
| `InvalidOperationException` при `new H2Connection(null!)` | null в прямом конструкторе — ошибка | не использовать прямой конструктор; через билдер строка всегда валидна |

---

## Готовые рецепты

### Чистый ADO.NET, параметры и ридер

```csharp
using System.Data.Common;
using H2Sharp.Data;

using H2DataSource source = new H2DataSourceBuilder("Data Source=recipes;Storage=Memory").Build();
using H2Connection connection = (H2Connection)source.CreateConnection();
connection.Open();

using (DbCommand create = connection.CreateCommand())
{
    create.CommandText = "CREATE TABLE ITEM(ID INT PRIMARY KEY, NAME VARCHAR(20))";
    create.ExecuteNonQuery();
}

using (DbCommand insert = connection.CreateCommand())
{
    insert.CommandText = "INSERT INTO ITEM VALUES ($id, $name)";
    DbParameter id = insert.CreateParameter();
    id.ParameterName = "$id";
    id.Value = 1;
    insert.Parameters.Add(id);
    DbParameter name = insert.CreateParameter();
    name.ParameterName = "$name";
    name.Value = "bolt";
    insert.Parameters.Add(name);
    insert.ExecuteNonQuery();
}

using DbCommand query = connection.CreateCommand();
query.CommandText = "SELECT NAME AS LABEL FROM ITEM WHERE ID = $id";
DbParameter key = query.CreateParameter();
key.ParameterName = "$id";
key.Value = 1;
query.Parameters.Add(key);
using DbDataReader reader = query.ExecuteReader();
while (reader.Read())
{
    Console.WriteLine($"{reader.GetName(0)} = {reader.GetString(0)}");
}
```

### Dapper

```csharp
using Dapper;
using H2Sharp.Data;

using H2DataSource source = new H2DataSourceBuilder("Data Source=dapper;Storage=Memory").Build();
using var connection = source.CreateConnection();
connection.Open();
connection.Execute("CREATE TABLE T(ID INT, NAME VARCHAR(20))");
connection.Execute("INSERT INTO T VALUES ($id, $name)", new { id = 1, name = "bolt" });
string name = connection.QuerySingle<string>("SELECT NAME FROM T WHERE ID = $id", new { id = 1 });
```

Dapper 2.1.66 понимает `$` в фильтрации параметров и раскрытии списков, поэтому
`$Id` и `IN $Ids` работают. EF Core и linq2db провайдеры тоже генерируют `$`-плейсхолдеры.

### Транзакция с откатом

```csharp
using DbTransaction tx = connection.BeginTransaction();
try
{
    using H2Command insert = new("INSERT INTO T VALUES (2, 'x')", connection)
    {
        Transaction = tx,
    };
    insert.ExecuteNonQuery();
    tx.Commit();
}
catch
{
    tx.Rollback();
    throw;
}
```

### Соединение для всего приложения

```csharp
using H2DataSource source = new H2DataSourceBuilder("Data Source=app;Storage=Memory")
    .ConfigureExtensions(ext => ext.RegisterUdf<int, int>("DOUBLE_IT", x => x * 2))
    .ConfigureDataFormat(f => f.JsonFormat = H2JsonFormat.String)
    .Build();

using H2Connection connection = (H2Connection)source.CreateConnection();
connection.Open();
```

### Детерминированные тесты

```csharp
((IH2Test)connection).UuidProvider =
    () => Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
((IH2Test)connection).RandomGeneratorProvider = seed => new Random(seed ?? 0);
connection.Open();
// SELECT RANDOM_UUID(), RAND(1) теперь воспроизводимы
```

---

## Куда смотреть дальше

* Исходники: `src/H2Sharp/Data/` — `H2ParameterRewriter.cs` (переписывание параметров),
  `H2ValueMapper.cs` (CLR ↔ Value), `H2DataReader.cs` (чтение), `H2Command.cs` (выполнение),
  `H2Connection.cs` (жизненный цикл), `IH2DataFormat.cs` (проекция).
* Проверенные сценарии: `tests/H2Sharp.Data.Tests/` — `H2NamedParameterTests`,
  `H2TimeParameterTests`, `H2ArrayTests`, `H2JsonReadingTests`, `H2DataFormatTests`,
  `H2TransactionSemanticsTests`, `H2BatchTests`, `H2CommandBuilderTests`,
  `H2GetSchemaTests`, `H2ExtensionTests`, `H2TestSeamsTests`, `DapperSmokeTests`.
* Правила портирования и принятая .NET-граница: [`../AGENTS.md`](../AGENTS.md),
  [ADR_DOTNET_EXTENSIONS.md](ADR_DOTNET_EXTENSIONS.md), [PORTING.md](PORTING.md).

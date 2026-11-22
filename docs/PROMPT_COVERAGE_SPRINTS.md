# Промпт: спринты покрытия H2Sharp (для исполнителя без импровизации)

Ты — исполнитель спринтов покрытия в репозитории **H2Sharp** (порт H2 2.5 Java→C#, ветка `main`,
корень `C:\Work\h2sharp`, shell — Git Bash). Один спринт = один новый тестовый файл (или
дополнение указанного), измерение, запись в `MIGRATION_STATUS.md`, коммит, push.

**Ты не проектируешь и не импровизируешь.** Ты берёшь готовую цель из очереди, пишешь указанные
тесты, прогоняешь команды и коммитишь. Если что-то не сходится — останавливаешься и пишешь отчёт.

---

## 1. Жёсткие правила (нарушение = провал спринта)

1. **Не меняй `src/`.** Спринт — только `tests/` + `MIGRATION_STATUS.md`. Если тест падает из-за
   дефекта порта — СТОП, опиши дефект в ответе, ничего не коммить.
2. **Не выдумывай код, если есть эталон** `reference/h2/h2` (H2 2.5). Поведение сверяй по эталону,
   а не по памяти и не по H2 1.x.
3. **Не ослабляй тесты:** никаких `Skip`, удаления проверок, «подгонки» ожиданий под фактическое
   поведение без сверки с эталоном.
4. **Не трогай** `.gitignore`, `.zcode/`, `artifacts/`; не коммить временные файлы.
5. **Не убивай чужие процессы `dotnet`.**
6. Правки файлов — только инструментами `edit`/`write`. Никаких PowerShell/regex-правок исходников.
7. Покрытие меряй **только в Debug** (в Release инлайнинг мажет строки).
8. **Никогда не смешивай отчёты покрытия разных сборок.** После любой правки `src/` номера строк
   сдвигаются, и объединение старых и новых отчётов даёт фантомные строки. Замер спринта —
   всегда полный пересъём всех пяти наборов в новую папку `sNNN`.
9. Один спринт = одна цель. Не смешивай две цели в один коммит.
10. В конце спринта — отчёт по шаблону из раздела 8.

---

## 2. Окружение и база

Шесть наборов (гейт — все шесть, последовательно):

| Имя | Проект | Команда |
|---|---|---|
| Core | `tests/H2Sharp.Tests/H2Sharp.Tests.csproj` | `dotnet test ... --filter "Category!=BlockedByPort"` |
| MvStore | `tests/H2Sharp.MvStore.Tests/H2Sharp.MvStore.Tests.csproj` | то же |
| Data | `tests/H2Sharp.Data.Tests/H2Sharp.Data.Tests.csproj` | то же |
| Engine | `tests/H2Sharp.Engine.Tests/H2Sharp.Engine.Tests.csproj` | то же |
| Specs | `tests/H2SharpSql.Specs/H2SharpSql.Specs.csproj` | то же |
| Native | `tests/H2Sharp.Native.Tests/H2Sharp.Native.Tests.csproj` | `H2SHARP_NATIVE_TESTS=1 dotnet test ... --filter "Category!=BlockedByPort"` |

Утилиты покрытия (только Python stdlib):

```bash
python -I tools/coverage/merge_cov.py --results-dir artifacts/TestResults/sNNN --min-lines 300 --top 15
python -I tools/coverage/uncovered.py --results-dir artifacts/TestResults/sNNN "ПУТЬ\\К\\Файлу.cs"
```

Базовая точка на момент написания (коммит `acdd0fa`): **92.66% строк (80424/86798)**,
**88.81% ветвей (33125/37298)**. Текущий спринт — `s185`, затем `s186`, `s187`, …

**Доступ к internal-членам:** `InternalsVisibleTo` есть у `H2Sharp.Tests` и `H2Sharp.Data.Tests`.
У `H2Sharp.MvStore.Tests` его **нет** — из него доступны только `public`-члены.

---

## 3. Процедура спринта (выполняй буквально)

**Шаг 0.** `git status --short` — рабочее дерево должно быть без изменений в `src/`/`tests/`
(кроме новых файлов прошлого спринта, которые уже закоммичены).

**Шаг 1.** Возьми первую невыполненную цель из раздела 6. Номер спринта = следующий `sNNN`.

**Шаг 2.** Посмотри непокрытые строки цели:

```bash
python -I tools/coverage/uncovered.py --results-dir artifacts/TestResults/s184 "ПУТЬ\\К\\Файлу.cs"
```

(для следующего спринта — уже `s185`, `s186`, …; номер — из шага 1).

**Шаг 3.** Создай тестовый файл по шаблону из раздела 5 (имя файла указано в цели). Каждый тест
маленький, с конкретными `Assert`. Тест обязан падать, если поведение изменится, — проверяй
значения, а не «не бросил исключение».

**Шаг 4.** Собери и прогони только новый класс:

```bash
dotnet test tests/ПРОЕКТ/ПРОЕКТ.csproj --configuration Debug --filter "FullyQualifiedName~ИМЯ_КЛАССА"
```

Красный → раздел 7.

**Шаг 5.** Пересними покрытие всех пяти наборов (Native в покрытие не входит) в новую папку:

```bash
rm -rf artifacts/TestResults/sNNN
mkdir -p artifacts/TestResults/sNNN
dotnet test tests/H2Sharp.Tests/H2Sharp.Tests.csproj --configuration Debug --collect:"XPlat Code Coverage" --results-directory artifacts/TestResults/sNNN/Core --filter "Category!=BlockedByPort"
dotnet test tests/H2Sharp.MvStore.Tests/H2Sharp.MvStore.Tests.csproj --configuration Debug --collect:"XPlat Code Coverage" --results-directory artifacts/TestResults/sNNN/MvStore --filter "Category!=BlockedByPort"
dotnet test tests/H2Sharp.Data.Tests/H2Sharp.Data.Tests.csproj --configuration Debug --collect:"XPlat Code Coverage" --results-directory artifacts/TestResults/sNNN/Data --filter "Category!=BlockedByPort"
dotnet test tests/H2Sharp.Engine.Tests/H2Sharp.Engine.Tests.csproj --configuration Debug --collect:"XPlat Code Coverage" --results-directory artifacts/TestResults/sNNN/Engine --filter "Category!=BlockedByPort"
dotnet test tests/H2SharpSql.Specs/H2SharpSql.Specs.csproj --configuration Debug --collect:"XPlat Code Coverage" --results-directory artifacts/TestResults/sNNN/Specs --filter "Category!=BlockedByPort"
```

**Шаг 6.** Проверь цель:

```bash
python -I tools/coverage/uncovered.py --results-dir artifacts/TestResults/sNNN "ПУТЬ\\К\\Файлу.cs"
```

Число покрытых строк должно вырасти минимум на величину из цели. **Не выросло** → тест не дошёл
до строк: удали новые тесты, отметь цель как недостижимую в отчёте, переходи к следующей цели.

**Шаг 7.** Гейт шести наборов (последовательно, без `--no-build`, Debug):

```bash
dotnet test tests/H2Sharp.Tests/H2Sharp.Tests.csproj --configuration Debug --filter "Category!=BlockedByPort"
dotnet test tests/H2Sharp.MvStore.Tests/H2Sharp.MvStore.Tests.csproj --configuration Debug --filter "Category!=BlockedByPort"
dotnet test tests/H2Sharp.Data.Tests/H2Sharp.Data.Tests.csproj --configuration Debug --filter "Category!=BlockedByPort"
dotnet test tests/H2Sharp.Engine.Tests/H2Sharp.Engine.Tests.csproj --configuration Debug --filter "Category!=BlockedByPort"
dotnet test tests/H2SharpSql.Specs/H2SharpSql.Specs.csproj --configuration Debug --filter "Category!=BlockedByPort"
H2SHARP_NATIVE_TESTS=1 dotnet test tests/H2Sharp.Native.Tests/H2Sharp.Native.Tests.csproj --configuration Debug --filter "Category!=BlockedByPort"
```

Все шесть зелёные — иначе раздел 7 (набор не «известно красный», а провал спринта).

**Шаг 8.** Общий замер:

```bash
python -I tools/coverage/merge_cov.py --results-dir artifacts/TestResults/sNNN --min-lines 300 --top 15
```

**Шаг 9.** Запиши спринт в конец `MIGRATION_STATUS.md` по шаблону раздела 9.

**Шаг 10.** Закоммить и запушь только тесты и статус:

```bash
git add tests/.../НовыйФайл.cs MIGRATION_STATUS.md
git commit -m "test(область): краткое описание (спринт NNN)"
git push
```

---

## 4. Что нельзя покрывать (недостижимые ветки — не трогать)

Это защитные/гоночные/коррупционные пути; попытка их «покрыть» — потеря времени и риск
сфабриковать тест. Пропускай их без сожалений:

- `MvStore/Tx/Transaction.cs`: 210–211, 227, 387–391, 408–412, 497–504, 520–527, 555–559,
  583–590, 622–625, 676–678, 695–717, 744, 758, 772–774.
- `MvStore/Db/MVPrimaryIndex.cs`: 133–135, 207–213, 249–251, 277–279, 291–293, 448–463,
  518–530, 707–731.
- `MvStore/Chunk.cs` и `MvStore/FileStore.cs`: ветки `Panic`, «файл повреждён», отрицательные
  позиции — только если цель спринта явно про повреждённый файл (по умолчанию — нет).
- `TODOException`-заглушки непортированных видов (`Schema.add/rename/remove`, `MergeUsing`,
  `MVPrimaryIndex` TODO) — не вызывать: это зафиксированные долги, а не цели покрытия.

Отдельно: **долг `Query.GetCost` (long вместо эталонного double) отложен пользователем.**
Он записан разделом в `MIGRATION_STATUS.md` и маркером `TODO(debt)` в коде. Не исправлять в этих
спринтах.

---

## 5. Шаблоны тестов

### 5.1. Core / Data / Specs — через `SessionLocal`

```csharp
/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Кратко: что именно покрывают тесты.
 */
using System;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Result;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ИмяКлассаTests
{
    private readonly SessionLocal session;
    private readonly Database database;

    public ИмяКлассаTests()
    {
        database = new Database(
            ConnectionInfo.Memory("имя_теста_" + Guid.NewGuid().ToString("N")));
        session = new SessionLocal(database, "sa");
    }

    private void Execute(string sql)
    {
        CommandContainer container = Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
        _ = container.ExecuteUpdate(false);
    }

    private string Scalar(string sql)
    {
        CommandContainer container = Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
        using ResultInterface result = container.ExecuteQuery(10, 0, false);
        Assert.True(result.Next());
        return result.CurrentRow()![0]!.GetString()!;
    }

    [Fact]
    public void ИмяТеста()
    {
        // arrange / act / assert
    }
}
```

Ошибки движка проверяй так:

```csharp
H2SharpException e = Assert.Throws<H2SharpException>(() => Execute("..."));
Assert.Equal(Api.ErrorCode.ИмяКода, e.H2ErrorCode);
```

### 5.2. MvStore — через `StoreTestBase`

```csharp
/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.MvStore;
using H2Sharp.MvStore.Tx;
using H2Sharp.MvStore.Type;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class ИмяКлассаTests : StoreTestBase
{
    [Fact]
    public void ИмяТеста()
    {
        using MVStore s = new MVStore.Builder().Open();
        // ...
    }
}
```

### 5.3. Data — через `H2Connection`

```csharp
using System;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class ИмяКлассаTests
{
    [Fact]
    public void ИмяТеста()
    {
        using H2Connection connection = new("Data Source=имя;Storage=Memory");
        connection.Open();
        // ...
    }
}
```

---

## 6. Очередь целей

Выполняй по порядку. Если цель уже закрыта (в `uncovered.py` меньше 5 строк) — переходи к следующей.

### Цель 1 (спринт s185). `MvStore/Tx/TransactionStore.cs`

- Файл теста: **дополнить** `tests/H2Sharp.MvStore.Tests/TransactionTests.cs` (класс `TransactionTests`).
- Покрыть строки: **386–388** (`GetOpenTransactions` сам вызывает `Init()`), **451**
  (`CloseImmediately`).
- Тесты (добавить в конец класса):

```csharp
[Fact]
public void OpenTransactionsInitializesTheStore()
{
    using MVStore s = new MVStore.Builder().Open();
    var ts = new TransactionStore(s);
    // Init() не вызываем: GetOpenTransactions инициализирует store сам.
    Assert.Empty(ts.GetOpenTransactions());
}

[Fact]
public void CloseImmediatelySkipsTheCommit()
{
    using MVStore s = new MVStore.Builder().Open();
    var ts = new TransactionStore(s);
    ts.Init();
    ts.CloseImmediately();
    ts.Dispose();
}
```

- Ожидание: `TransactionStore.cs` было `540/603`, станет ≈ `544/603` (+4 строки).

### Цель 2 (спринт s186). `Data/H2Connection.cs`

- Файл теста: **новый** `tests/H2Sharp.Data.Tests/H2ConnectionExtensionTests.cs`.
- Покрыть:
  - **136, 158, 169, 180, 191, 202, 213** — геттеры `IH2DataFormat`
    (`ClobFormat`, `ArrayFormat`, `DateFormat`, `TimeFormat`, `TimeTzFormat`,
    `TimestampFormat`, `TimestampTzFormat`);
  - **484–486, 490–492, 496–498** — `RegisterUdf` с 2, 3 и 4 аргументами
    (регистрация только на **закрытом** соединении: создать → зарегистрировать → `Open()`);
  - **674–675** — `GetSchema(null!)` → `ArgumentException`;
  - **730–731** — `GetSchema("NOPE")` → `ArgumentException`;
  - **742** — `GetSchemaAsync("TABLES")` (перегрузка со строкой).
- Тесты:

```csharp
[Fact]
public void DataFormatGettersAnswer()
{
    using H2Connection connection = new("Data Source=fmt;Storage=Memory");
    connection.Open();
    IH2DataFormat format = connection;
    Assert.True(Enum.IsDefined(format.BlobFormat));
    Assert.True(Enum.IsDefined(format.ClobFormat));
    Assert.True(Enum.IsDefined(format.JsonFormat));
    Assert.True(Enum.IsDefined(format.ArrayFormat));
    Assert.True(Enum.IsDefined(format.DateFormat));
    Assert.True(Enum.IsDefined(format.TimeFormat));
    Assert.True(Enum.IsDefined(format.TimeTzFormat));
    Assert.True(Enum.IsDefined(format.TimestampFormat));
    Assert.True(Enum.IsDefined(format.TimestampTzFormat));
}

[Fact]
public void MultiArgumentUdfsAreCallable()
{
    using H2Connection connection = new("Data Source=udf_multi;Storage=Memory");
    ((IH2Extensions)connection).RegisterUdf<int, int, int>("ADD", (a, b) => a + b);
    ((IH2Extensions)connection).RegisterUdf<int, int, int, int>("ADD3", (a, b, c) => a + b + c);
    ((IH2Extensions)connection).RegisterUdf<int, int, int, int, int>("ADD4", (a, b, c, d) => a + b + c + d);
    connection.Open();

    // Выполни SELECT ADD(2,3), SELECT ADD3(1,2,3), SELECT ADD4(1,2,3,4)
    // и проверь результаты 5, 6, 10 (образец — tests/H2Sharp.Data.Tests/H2ExtensionTests.cs).
}

[Fact]
public void SchemaCollectionGuardsAnswer()
{
    using H2Connection connection = new("Data Source=schema_guards;Storage=Memory");
    connection.Open();
    Assert.Throws<ArgumentException>(() => connection.GetSchema(null!));
    Assert.Throws<ArgumentException>(() => connection.GetSchema("NOPE"));
    Assert.NotNull(connection.GetSchemaAsync("TABLES").GetAwaiter().GetResult());
}
```

- Обязательно сверь сигнатуры `RegisterUdf` по `src/H2Sharp/Data/IH2Extensions.cs` и
  образец вызова SQL — по `tests/H2Sharp.Data.Tests/H2ExtensionTests.cs`. Не выдумывай перегрузки.
- Ожидание: `H2Connection.cs` было `498/555`, станет ≈ `518/555`.

### Цель 3 (спринт s187). `Util/Utils.cs`

- Файл теста: **новый** `tests/H2Sharp.Tests/UtilsCoverageTests.cs` (пространство `H2Sharp.Tests`).
- Покрыть (сверяй сигнатуры по самому файлу):

| Строки | Что вызвать | Ожидание |
|---|---|---|
| 66–67 | `Utils.IndexOf(new byte[]{1,2,3}, Array.Empty<byte>(), 1)` | `1` |
| 198–199 | `Utils.CopyBytes(new byte[]{1,2}, 0)` | пустой массив |
| 205–209 | `Utils.CopyBytes(new byte[]{1,2}, 2)` | копия `{1,2}` |
| 237–238 | `Utils.CloneByteArray(null)` | `null` |
| 533–557 | `Utils.ParseBoolean("1"/"0"/"no"/"yes"/"true"/"false", def, false)` | `true/false/...` |
| 589–591 | `Utils.ParseBoolean("maybe", true, false)` | `true` (default) |
| 739–740 | `Utils.JavaHashCode(null)` | `0` |
| 848–851 | создать `Utils.CreateSingleThreadExecutor("t")`, `Shutdown()` его, затем `Utils.FlushExecutor(ex)` | без исключения |
| 864–865 | `Utils.ShutdownExecutor(null)` | без исключения |
| 948–949 | после `Shutdown()` вызвать `ex.Submit(() => {})` | `InvalidOperationException` |

- Дополнительно проверь `Utils.ParseBoolean("maybe", false, true)` → исключение (сверь тип по коду).
- Ожидание: `Utils.cs` было `408/450`, станет ≈ `420/450`.

### Цель 4 (спринт s188). `Util/Geometry/EWKBUtils.cs`

- Файл теста: **дополнить** существующий `tests/H2Sharp.Tests/EWKBUtilsErrorPathTests.cs`
  (класс из спринта 173).
- Публичные методы: `EWKB2EWKB(byte[])`, `EWKB2EWKB(byte[], int)`, `ParseEWKB(byte[], GeometryUtils.Target)`,
  `Type2DimensionSystem(int)`, `GetDimensionSystem(byte[])`, `Envelope2WKB(double[]?)`.
- Для каждой непокрытой строки (`168–169`, `229–239`, `290–294`, `392–393`, `404–405`, `410–411`,
  `426–427`, `438–439`, `447–448`, `452–453`, `464–465`, `482–483`, `488–489`, `503`, `539–540`):
  прочитай метод на этой строке и передай **заведомо короткий/неизвестный** буфер или тип;
  ожидание — `ArgumentException` (сверь по коду; некоторые строки — ветки форматирования
  ошибки, их можно покрыть через невалидный EWKB).
- Пример: `Assert.Throws<ArgumentException>(() => EWKBUtils.EWKB2EWKB(new byte[] { 1 }));`
- Ожидание: `EWKBUtils.cs` было `307/342`, станет ≥ `320/342`.

### Цель 5 (спринт s189). `MvStore/Page.cs`

- Файл теста: **новый** `tests/H2Sharp.MvStore.Tests/PageMemoryTests.cs` (наследник `StoreTestBase`).
- Покрыть достижимое: **439–447** (память при обновлении ключа), **497–500** (память при
  удалении ключа), **732–733** (повторная запись страницы), **739–740** (счёт удалённых).
- Сценарий: `MVMap<int,int>` на `new MVMap.Builder<int,int>()`, серия `Put`/обновление/`Remove`;
  проверяй содержимое и `GetMemory()` страницы. Строки 521–566 (повреждённый файл) — не трогать.
- Ожидание: `Page.cs` было `890/983`, станет ≥ `900/983`.

### Цель 6 (спринт s190). `Expressions/Conditions/CompareLike.cs` и `Comparison.cs`

- Файл теста: **новый** `tests/H2Sharp.Tests/ConditionCoverageTests.cs`.
- SQL-сценарии (Core-набор, `SessionLocal`):
  - `CREATE TABLE T(A VARCHAR); CREATE INDEX IA ON T(A);`
  - `SELECT * FROM T WHERE A LIKE 'ab%'` — префиксное условие индекса (408–411);
  - `CREATE VIEW VL AS SELECT * FROM T WHERE NOT A LIKE 'ab%'` и чтение
    `SELECT VIEW_DEFINITION FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_NAME='VL'` — рендеринг
    `NOT` над LIKE (NeedParentheses, 191);
  - `SELECT CASE WHEN A LIKE 'a%' THEN 1 ELSE 2 END FROM T` — `GetWhenValue` (439–440);
  - LIKE с ESCAPE: `SELECT * FROM T WHERE A LIKE 'a!_%' ESCAPE '!'`.
- Прямые вызовы `GetSubexpressionCount()`/`GetSubexpression(i)` допустимы только если объект
  условия доступен без приватных членов; иначе пропусти строки 869–877.
- Ожидание: `CompareLike.cs` было `437/484`, станет ≥ `450/484`.

### Волна 2 — s191–s200: прямые API-спринты

Правило волны: перед написанием теста открой `uncovered.py` по цели и **прочитай метод на каждой
непокрытой строке**. Если строка — `throw new TODOException(...)`, это долг: пропусти её.
Если в сценарии сказано «прочитай имя метода/условие» — сделай это, не угадывай.

#### Цель 7 (s191). `MvStore/Type/ObjectDataType.cs` — 802/894, непокрыто 92

- Файл теста: **новый** `tests/H2Sharp.Tests/ObjectDataTypeDirectTests.cs` (Core: нужен
  `internal DetectType`).
- Сценарии:
  * для каждого значения, которое принимает `ObjectDataType` (bool, sbyte, short, int, long,
    float, double, char, string, `byte[]`, `Guid`, `BigInteger`, `DateTime`, массивы — сверь
    список по `SwitchType`/`DetectType`):
    ```csharp
    var ot = new ObjectDataType();
    object? inner = ot.DetectType(value);       // internal, потому Core
    var buff = new WriteBuffer();
    ((DataType<object?>)inner!).Write(buff, value);
    int pos = 0;
    object? back = ((DataType<object?>)inner!).Read(buff.ToArray(), ref pos);
    Assert.Equal(value.GetType(), back!.GetType());
    Assert.Equal(0, ((DataType<object?>)inner!).Compare(value, back));
    ```
    Это покрывает `Read`-однострочники внутренних типов (410, 436, 462, 545, 599, 651, 705, 758,
    823, 879, 921, 953).
  * `ot.DetectType(DateTime)` и `ot.DetectType(new int[1])` — `IsDate`/`IsArray` (301, 303).
  * `Compare` двух `byte[]`: одинаковые (326–328), разные (331), разной длины; `Compare`
    объектов без `IComparable` (1347–1349).
  * `CreateStorage(3)` (352); `GetMemory`/`Write` через внешний `ObjectDataType` (362, 365).
  * `ot.Write(buff, null); ot.Read(...)` — null-тип (388).
- Ожидание: было `802/894`, станет ≥ **830/894**.
- Пропустить: 184–185, 251–253 (неизвестный тег — только повреждённый файл).

#### Цель 8 (s192). `Tables/TableTypes.cs` — 943/1040, непокрыто 97

- Файл теста: **новый** `tests/H2Sharp.Tests/TableTypesDirectTests.cs` (Core).
- Сценарии (имена методов прочитай на строках):
  * аксессоры `IndexColumn`/`Column`/`Table` — 475–476, 628–630, 654–655, 723–735
    (ветки «первая колонка», «продолжить/вернуть a/вернуть b»);
  * конверсия `ValueRow` при наличии session (818–820): создай таблицу с несколькими колонками
    и приведи `ValueRow` через `Column.Convert(session, columns, row)` (сигнатуру сверь);
  * `SetKey` из значения (904–906): строка с `_ROWID_`-значением; проверь, что NULL даёт
    `long.MinValue` (вставь через `INSERT INTO T(_ROWID_, A) VALUES (NULL, 1)` и прочитай ключ);
  * временность таблицы (1255–1256): `CREATE LOCAL TEMPORARY TABLE` и
    `CREATE GLOBAL TEMPORARY TABLE`, прочитай `GetCreateSql()`/`IsGlobalTemporary()`.
- Ожидание: ≥ **975/1040**.
- Пропустить: 1279 (`Table.getId` TODO), 1335 (`Table.rename` TODO).

#### Цель 9 (s193). `MvStore/Db/MVSecondaryIndex.cs` — 241/290, непокрыто 49

- Файл теста: **новый** `tests/H2Sharp.Tests/SecondaryIndexDirectTests.cs` (Core, по образцу
  `SpatialIndexDirectTests`).
- Сценарии:
  * уникальный вторичный индекс: `CREATE TABLE T(A INT, B INT); CREATE INDEX ... UNIQUE` —
    дубликат в коммите (163–164, 252–258);
  * незакоммиченный конкурент: вторая сессия со `SET AUTOCOMMIT FALSE` вставляет то же значение,
    первая получает конфликт/`ConcurrentUpdate1` (240–242);
  * `Find` по равенству и диапазону; `Remove` существующей строки (271–284);
  * остатки: `NeedRebuild`, `GetRowCount`, `GetCost` на пустом и заполненном индексе (312–313).
- Ожидание: ≥ **265/290**.

#### Цель 10 (s194). `Util/StringUtils.cs` (1376/1414) + `MvStore/WriteBuffer.cs` (162/197)

- Файл теста: **новый** `tests/H2Sharp.Tests/StringUtilsWriteBufferTests.cs` (Core).
- Сценарии:
  * `StringUtils`: цифровое/нецифровое/пустое имя (2442–2458 — найди публичный метод, который
    вызывает этот хелпер); identifier quoting (265–266); добавление символа (784–786,
    1627–1629); null-путь (2163–2164);
  * `WriteBuffer`: round-trip `PutVarInt`/`GetVarInt` и `PutVarLong`/`GetVarLong` на границах
    (127, 128, 16383, 16384, `int.MaxValue`); `PutStringData`/`GetString` с юникодом;
    рост буфера при записи больше capacity (471); `Put`/`Get` байтов.
- Ожидание: `StringUtils.cs` ≥ **1395/1414**, `WriteBuffer.cs` ≥ **180/197**.
- Пропустить: OOM-ветки (442–445, 458–461, 473–477).

#### Цель 11 (s195). `Data/H2ValueMapper.cs` (575/620) + `Value/Value.Conversion.Families.cs` (1040/1083)

- Файл теста: **новый** `tests/H2Sharp.Data.Tests/H2ValueMapperCastTests.cs` (Data).
- Сценарии (через `H2Connection` + параметры/чтение):
  * `sbyte` и `float` как параметры и результаты (81, 86, 194):
    `SELECT CAST(? AS TINYINT)`/`SELECT CAST(? AS REAL)`;
  * CLOB из строки/потока больше in-memory порога — временный файл (285–288, 320–343):
    найди порог в коде и подай строку длиннее;
  * конверсии: NULL (130), CAST в BINARY из нестроки (330–331), CLOB из строки (438–439),
    ENUM-ошибка (875), DECFLOAT `FormatException` (1128–1130).
- Ожидание: `H2ValueMapper.cs` ≥ **600/620**, `Value.Conversion.Families.cs` ≥ **1060/1083**.
- Пропустить: `GetInternalError`-ветки (494, 562, 630, 696, 757, 872, 910, 1119, 1165).

#### Цель 12 (s196). `Tables/InformationSchemaTable.cs` (1757/1799) + `Engine/MetaRecord.cs` (72/108)

- Файл теста: **новый** `tests/H2Sharp.Tests/InformationSchemaRowsTests.cs` (Core).
- Сценарии:
  * CONSTANTS: `CREATE CONSTANT C VALUE 1;` + `SELECT * FROM INFORMATION_SCHEMA.CONSTANTS`
    (1044–1048);
  * LOCKS: вторая сессия, `SET LOCK_TIMEOUT 1000`, удерживаемая блокировка, затем
    `SELECT * FROM INFORMATION_SCHEMA.LOCKS` (1207–1216);
  * INVALID-статус view (1137–1139) — если недостижимо, пропусти;
  * `MetaRecord`: переоткрытие файловой БД с мета-строками ограничений (34–42) — best effort,
    не обещай.
- Ожидание: `InformationSchemaTable.cs` ≥ **1775/1799**; MetaRecord — best effort.

#### Цель 13 (s197). `Expressions/Aggregate/Aggregate.cs` — 1023/1100, непокрыто 77

- Файл теста: **новый** `tests/H2Sharp.Tests/AggregateEdgeTests.cs` (Core).
- Сценарии:
  * `SELECT MODE(A) FROM T` с дубликатами (792–800);
  * `COUNT(DISTINCT A)`, `SUM(DISTINCT A)` — distinct-ветки;
  * агрегаты, которые уже портированы (сверь по `createAggregateData`): `ANY_VALUE`,
    `ARRAY_AGG`, `HISTOGRAM` — по одному запросу на агрегат.
- Ожидание: ≥ **1050/1100**.
- Пропустить: 377–379 (TODO непортированных агрегатов), `GetInternalError`-ветки.

#### Цель 14 (s198). `Constraints/ConstraintReferential.cs` — 466/505, непокрыто 39

- Файл теста: **новый** `tests/H2Sharp.Tests/ReferentialActionsTests.cs` (Core).
- Сценарии SQL на данных:
  * `ON DELETE CASCADE`, `ON UPDATE CASCADE`, `ON DELETE SET NULL`, `ON DELETE SET DEFAULT`
    (434–462);
  * `SET NULL` на `GENERATED ALWAYS`-колонке → `GeneratedColumnCannotBeUpdatableByConstraint2`
    (559–561);
  * `COMMENT` в `GetCreateSql` (112–115) — прочитай, каким DDL он задаётся;
  * `SET REFERENTIAL_INTEGRITY FALSE/TRUE` (366–368) — если команда портирована.
- Ожидание: ≥ **485/505**.

#### Цель 15 (s199). `Expressions/Functions/DateTimeFunction.cs` — 589/623, непокрыто 34

- Файл теста: **новый** `tests/H2Sharp.Tests/DateTimeFunctionEdgeTests.cs` (Core).
- Сценарии SQL:
  * `EXTRACT` неподдерживаемого поля (330–331, 531–532);
  * `DATEADD`/`DATEDIFF` с неподдерживаемым полем (778, 980);
  * TIMESTAMP WITH TIME ZONE через границу зоны (947–963, 1069–1076);
  * `EXTRACT(DAY FROM ...)` (902, goto-ветка).
- Ожидание: ≥ **605/623**.

#### Цель 16 (s200). `MvStore/MVStoreTool.cs` — 454/500, непокрыто 46

- Файл теста: **новый** `tests/H2Sharp.MvStore.Tests/MVStoreToolErrorTests.cs` (MvStore;
  приём порчи файла возьми из `MVStoreToolDamageTests`).
- Сценарии:
  * дамп файла с испорченным footer (309–313, 337–340);
  * `Info` на файле с неверным block size (463–464);
  * repair неисправимого файла → сообщение Fail (526–532).
- Ожидание: ≥ **470/500**.

### Волна 3 — s201–s207: Parser, Tokenizer, Select, Query, скрипты

Правило волны: один спринт — один файл и один новый тестовый файл
`tests/H2Sharp.Tests/Parser...Tests.cs`. SQL запускай через `session.PrepareCommand(sql)`;
ошибки — `Assert.Throws<H2SharpException>` с `H2ErrorCode`. **Перед SQL прочитай метод по строке
из `uncovered.py`** и убедись, что форма именно эта; иначе пропусти строку.
Если сценарий опирается на TODO-заглушку — пропусти, это долг.

#### Цель 17 (s201). Parser: негативные DDL-формы

- Файл теста: **новый** `tests/H2Sharp.Tests/ParserNegativeDdlTests.cs`.
- Сценарии (каждую форму сверь с кодом на указанной строке):
  * `DROP INDEX IF EXISTS NOPE` и `DROP INDEX NOPE` (1633–1639, 3105);
  * `DROP TABLE IF EXISTS NOPE`, `DROP VIEW IF EXISTS NOPE`;
  * `CREATE TABLE T(A INT, A INT)` → `DuplicateColumnName1` (4367–4368);
  * несовпадение схемы в DDL (1608–1609, 4629–4754) — прочитай условие `SchemaNameMustMatch`;
  * `ALTER TABLE T ADD CONSTRAINT C CHECK (...)` (4558–4561), `COMMENT` (4463–4465).
- Ожидание: `Parser.cs` ≥ **6540/7229**.
- Пропустить: `TODOException`-строки (1729–1750, 1929–1935, 4253–4255, 5726, 7388–7414,
  7528–7532).

#### Цель 18 (s202). Parser: row-value, массивы, `* EXCEPT`

- Файл теста: **новый** `tests/H2Sharp.Tests/ParserRowValueTests.cs`.
- Сценарии:
  * `SELECT (A, B) = (1, 2) FROM T` (4089–4093, 6830);
  * `SELECT A[1] FROM T` (A — ARRAY; 4253–4264 — если TODO, пропусти);
  * `SELECT * EXCEPT (A) FROM T` (5061–5063);
  * `SELECT T.A FROM T`, `SELECT * FROM T WHERE T.A = 1` (4316–4341);
  * доменное значение (7082–7084 — прочитай, какая форма).
- Ожидание: `Parser.cs` ≥ **6570/7229**.

#### Цель 19 (s203). Parser: границы query-синтаксиса

- Файл теста: **новый** `tests/H2Sharp.Tests/ParserQueryEdgeTests.cs`.
- Сценарии:
  * `SELECT * FROM T FOR UPDATE` (5677–5680);
  * `LEADING`/`LIMIT`-выражения (5711–5727 — прочитай, что поддержано; параметр может быть TODO);
  * `VALUES (1), (2)` как запрос; `WITH ... SELECT`;
  * `SELECT 1 WHERE FALSE`;
  * длинный строковый литерал > `Constants.MaxStringLength` (6308–6315) — если порог достижим;
  * `(A, B) IN ((1,2), (3,4))` (497).
- Ожидание: `Parser.cs` ≥ **6600/7229**.

#### Цель 20 (s204). Tokenizer: некорректные литералы

- Файл теста: **новый** `tests/H2Sharp.Tests/TokenizerErrorTests.cs`.
- Сценарии (каждый — исключение на `session.PrepareCommand(...)`):
  * `SELECT X'1G'` — `HexStringWrong1` (1734–1735);
  * `SELECT 1e`, `SELECT 1E+` — Numeric/Approximate numeric (1907–1908, 1960–1961, 2031–2032);
  * `SELECT 'abc` (незакрытая строка), `SELECT "abc` (незакрытый идентификатор);
  * комментарии `SELECT 1 /* c */`, `SELECT 1 -- c\n`, `SELECT 1 // c`;
  * не-ASCII пробелы/суррогаты (1687–1723) — best effort.
- Ожидание: `Tokenizer.cs` ≥ **1450/1496**.

#### Цель 21 (s205). Select + LocalResult: view-SQL и lazy-пути

- Файл теста: **новый** `tests/H2Sharp.Tests/SelectSqlLazyTests.cs`.
- Сценарии:
  * `CREATE VIEW V AS SELECT DISTINCT ON (A) A FROM T` + `VIEW_DEFINITION` (776–778);
  * `CREATE VIEW VG AS SELECT A, COUNT(*) FROM T GROUP BY A` + `VIEW_DEFINITION` (810–813);
  * `SELECT A FROM T ORDER BY A FETCH FIRST 1 ROWS WITH TIES` (1514–1525);
  * `SELECT * FROM T FOR UPDATE` (1796–1810);
  * повторное выполнение group-запроса (1688–1700);
  * `LocalResult`: `SELECT DISTINCT A FROM T ORDER BY A` (326–335), LOB-строка (483–486),
    `SetMaxMemoryRows` (162–164).
- Ожидание: `Select.cs` ≥ **1245/1332**, `LocalResult.cs` ≥ **515/537**.

#### Цель 22 (s206). Query: мелкие пути

- Файл теста: **дополнить** `tests/H2Sharp.Tests/QueryAccessorTests.cs`.
- Объект: `(Select)container.GetPreparedCommand()`.
- Сценарии:
  * `SetWithTies(true)` + `IsWithTies()`; `SetNeverLazy(true)`; `SetInPredicateResult()`;
    `SetInPredicateResultSortTypes(new[]{1})`;
  * `IsStandardDistinct()`, `HasOrder()`, `IsAnyDistinct()`, `IsQuery()`, `IsConstantQuery()`,
    `GetIfSingleRow()`;
  * `SELECT ... FOR UPDATE` → `GetForUpdate()` не null (override в Select);
  * `SetDistinct()` на не-distinct запросе (150–155).
- Ожидание: `Query.cs` ≥ **595/632**.
- Пропустить: 161–162 (`not initialized`), 1037–1038.

#### Цель 23 (s207). Скриптовая конформантность

- Файл: **дополнить** `tests/H2Sharp.Tests/ScriptConformanceTests.cs` (список закреплённых
  скриптов); раннер — `ScriptRunner.Run(path)`.
- Действие: найди в `reference/h2/h2/src/test/org/h2/test/scripts/**` скрипт, которого нет в
  списке и который **не** заблокирован D-004/D-029/D-037/XML-отклонениями; прогони; проходит —
  добавь в список; падает — убери и выбери другой.
- Ожидание: +1–3 скрипта; покрытие Parser/команд вырастет на 30–80 строк. Если ни один не
  проходит — отметь «blocked» и заверши волну 3.

После волны 3 исполнитель продолжает по `docs/COVERAGE_PLAN.md` (волна 4 — длинный хвост:
2–4 файла за спринт по самым дешёвым строкам).

---

## 7. Если тест красный

1. Прочитай сообщение и стек. Найди строку в `src/`, сверь поведение с эталоном
   `reference/h2/h2`.
2. Ожидание в тесте было неверным → исправь **ожидание** (не код), перезапусти.
3. `TODOException` / `NotSupportedException` / `BlockedByPort` → удали этот тест, отметь в отчёте
   «blocked», переходи к следующей цели.
4. Падение указывает на **дефект порта** (порт отвечает иначе, чем эталон) → **СТОП**:
   не меняй `src/`, не коммить; в ответе опиши: файл, строку, ожидание эталона, фактическое
   поведение, минимальный SQL/вызов для воспроизведения.
5. Флейк MvStore (фоновый воркер): перезапусти только этот набор один раз. Повторный флейк —
   отметь в отчёте, не коммить «зелёное» на слово.

---

## 8. Формат ответа по завершении спринта

```
Спринт sNNN — <цель>.
Тесты: <файл>, <N> тестов.
Покрытие файла: было X/Y строк, стало A/B (+D).
Итог: 92.xx% строк (…/…), 88.xx% ветвей.
Наборы: Core …/…, MvStore …/…, Data …/…, Engine …/…, Specs … (+… skipped), Native … (+… skipped).
Коммит: <hash>, запушен.
Недостижимо/пропущено: <список строк и причина>.
```

Если спринт остановлен на дефекте — вместо коммита:

```
Спринт sNNN остановлен: дефект порта в <файл>:<строка>.
Ожидание эталона: …
Фактически: …
Воспроизведение: <SQL/вызов>.
Изменения не закоммичены: <какие файлы>.
```

---

## 9. Шаблон записи в `MIGRATION_STATUS.md`

Добавляй в **конец** файла, дата — сегодняшняя:

```markdown
### Спринт NNN: <файл> — <что покрыто> (YYYY-MM-DD)

* Новый `<ИмяКлассаTests>` (N тестов) в <проект> покрывает: <перечень путей в 2–4 строках>.
* `<Файл>.cs`: X → **A/B** строк; итог **92.xx%** строк (…/…), **88.xx%** ветвей (…/…).
* Проверено: H2Sharp.Tests N/N, MvStore N/N, Data N/N, Engine N/N,
  Specs N/N (+N skipped), Native N/N (+N skipped), Category!=BlockedByPort.
```

---

## 10. Финальный чек-лист перед push

- [ ] Изменены только `tests/...` и `MIGRATION_STATUS.md`.
- [ ] Новый тест падал бы при изменении поведения (проверяет значения, а не «не бросил»).
- [ ] Покрытие цели выросло минимум на число из цели.
- [ ] Все шесть наборов зелёные (`Category!=BlockedByPort`), без `--no-build`.
- [ ] Замер сделан полным пересъёмом в новую `sNNN`, старые отчёты не копировались.
- [ ] `git add` только нужных файлов; `.zcode/`, `artifacts/` не в коммите.
- [ ] Коммит на русском, в скобках номер спринта; `git push` выполнен.

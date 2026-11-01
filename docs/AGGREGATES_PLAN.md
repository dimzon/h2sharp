# Пользовательские агрегаты (Фазы 1–2 реализованы 2026-11-01)

## Решение

Контракт агрегатов моделируется по lifecycle SQL Server CLR
(`Init` / `Accumulate` / `GetResult`), но исполнение остаётся H2-совместимым:
`DISTINCT`, `FILTER` и `OVER` обрабатывает движок, а результат может быть
запрошен повторно.

Отличия от `org.h2.api.Aggregate` приняты осознанно:

- нет `init(Connection)`: соединение в агрегат не передаётся;
- `GetResult` — аналог `Terminate`, но пересчитываемый; движок вызывает его
  больше одного раза (окна, пустая группа, повторное чтение);
- `Merge` вынесен в `IH2MergeableAggregate` и текущим движком не вызывается:
  partial/parallel execution в порте нет;
- встроенные агрегаты остаются на `Value`-уровне (`Aggregate` /
  `AggregateData*`), а не переводятся на публичный object-контракт.

## Публичный контракт

`src/H2Sharp/Data/Extensions/` (`namespace H2Sharp.Data.Extensions`):

```csharp
public interface IH2Aggregate
{
    void Init();
    TypeInfo GetResultType(IReadOnlyList<TypeInfo> inputTypes);
    void Accumulate(object? value);   // несколько аргументов — object?[]
    object? GetResult();
}

public interface IH2MergeableAggregate : IH2Aggregate
{
    void Merge(IH2MergeableAggregate other);
}

public interface IH2AggregateProvider
{
    AggregateOptions Options { get; }
    IH2Aggregate CreateAggregate();
}

public sealed class AggregateOptions
{
    public bool Deterministic { get; init; }
    public bool InvariantToDuplicates { get; init; }
    public bool InvariantToOrder { get; init; }
}
```

Провайдер — основной контракт. `Options` читается один раз при регистрации;
`CreateAggregate()` обязан возвращать новый экземпляр на каждую группу/окно.

`AggregateProvider` (делегат) и `AggregateProvider<T>` — адаптеры, а
`RegisterAggregate(name, Func<...>, options)` и `RegisterAggregate<T>` —
extension-методы. `IH2Aggregate` и `AggregateBase` **не** наследуют
`IDisposable`: конкретный агрегат реализует `IDisposable` только если владеет
unmanaged/освобождаемыми ресурсами. Движок проверяет `is IDisposable` и
диспозит такие экземпляры на границах жизни данных.

Базовые классы:

- `AggregateBase` — скелет; `Init` пустой, остальное абстрактно;
- `CollectingAggregateBase` — хранит значения плоским `List<object?>`,
  конструктор принимает число аргументов, результат вычисляет `Compute`.
  Сортировку и прочую логику пользователь делает сам.

`AggregateInput`, `RowIndex`, `OrderedAggregateBase`, `SetOrderBy` не вводились:
они либо не нужны, либо дублируют эталонные механизмы и создают лишние
аллокации.

## Регистрация и исполнение

- `IH2Extensions.RegisterAggregate(string name, IH2AggregateProvider provider)`.
  Регистрация только на закрытом, не disposed соединении. Делегатная форма —
  extension-метод над `AggregateProvider`.
- `AggregateDefinition` хранит имя, провайдера и снимок `Options`; тип и
  арность проверяет `GetResultType`.
- `ExtensionConfiguration` / `ExtensionRuntime` хранят снапшот реестра;
  `HasAggregate` / `FindAggregate`.
- `Parser.ReadFunction`: встроенный агрегат → зарегистрированный агрегат →
  зарегистрированная UDF → builtin-функция → window → compatibility.
  Встроенный агрегат сохраняет своё имя; зарегистрированные расширения могут
  заполнять/переопределять scalar builtins, как уже делают `ORA_HASH` и
  `ENCRYPT`.
- `ClrAggregate : AbstractAggregate` (internal,
  `Expressions/Aggregate/UserDefined/`) реализует эталонные ветви:
  `Optimize`, `IsEverything`, `GetCost`, `GetUnenclosedSql`,
  `CreateAggregateData`, `GetAggregatedValue`, `UpdateAggregate`,
  `UpdateFromExpressions`, `UpdateGroupAggregates`, `GetNumExpressions`,
  `RememberExpressions`.
- Преобразование значений — `AggregateValueConverter`: SQL NULL → `null`
  (не `DBNull`), результат приводится к объявленному `TypeInfo`.

## Семантика

- `DISTINCT`: движок собирает `Value` / `ValueRow`, а при получении результата
  проигрывает их через `Accumulate` в свежий экземпляр. `Accumulate` не
  вызывается на каждой исходной строке.
- `FILTER`: проверяется до `Accumulate`.
- `OVER`: используется общий оконный механизм `AbstractAggregate` /
  `DataAnalysisOperation`.
- Пустая группа: создаётся свежий экземпляр, вызывается `GetResult`.
- `NULL`-аргументы передаются в `Accumulate` как `null`; `IGNORE NULLS` не
  вводится.
- Ошибки пользовательского кода оборачиваются в
  `ErrorCode.ExceptionInFunction1`, как у UDF.
- `GetResultType` вызывается на пробном экземпляре во время оптимизации; `Init`
  вызывается на пробном экземпляре и на каждом экземпляре данных.
- `Deterministic` из `AggregateOptions` разрешает ответ детерминированного
  визитора; `InvariantToDuplicates` отключает сбор `DISTINCT`; `InvariantToOrder`
  зарезервирован, отдельного ordered-aggregate пути нет.
- `IDisposable`-агрегаты диспозятся в `SelectGroups.Reset()`, для временных
  `DISTINCT`/пустой группы — в `ClrAggregate.GetAggregatedValue`, для
  ordered-window локальных данных — в `finally` соответствующего цикла.

## Примеры и тесты

Опциональная сборка `H2Sharp.Extensions` содержит примеры-утилиты:

- `StringAgg` (`CollectingAggregateBase`) — конкатенация non-NULL значений с
  разделителем; по умолчанию `H2_STRING_AGG`;
- `GeometricMean` (`AggregateBase`) — геометрическое среднее неотрицательных
  чисел; `H2_GEOMETRIC_MEAN`;
- `HarmonicMean` — гармоническое среднее неотрицательных чисел;
  `H2_HARMONIC_MEAN`;
- `Product` — произведение non-NULL чисел; `H2_PRODUCT`;
- `WeightedAverage` — взвешенное среднее по двум аргументам (значение, вес);
  `H2_WEIGHTED_AVERAGE`;
- `CountIf` — количество TRUE, NULL/FALSE не считаются; `H2_COUNT_IF`;
- `AggregateExtensions.RegisterH2Aggregates()` и отдельные `Register*` методы —
  регистрация провайдеров (`StringAggProvider`, `GeometricMeanProvider` и т.д.)
  на закрытом соединении.

`tests/H2Sharp.Data.Tests/H2AggregateTests.cs`:

- `GeometricMean` на `AggregateBase`;
- `Median` и `CUSTOM_STRAGG` на `CollectingAggregateBase`;
- `DISTINCT`, `FILTER`, `OVER`, пустая группа, `NULL`, многoаргументный
  вызов, неверная арность, тип результата, повторный `GetResult`;
- регистрация/закрытость соединения, независимость соединений, коллизии имён.

`tests/H2Sharp.Data.Tests/H2AggregatePackageTests.cs`:

- `RegisterH2Aggregates`, `StringAgg`, `GeometricMean`, `HarmonicMean`,
  `Product`, `WeightedAverage`, `CountIf`;
- `DISTINCT`, `FILTER`, `NULL`, пустая группа, пользовательское имя, ошибки
  отрицательных аргументов и весов.

`tests/H2Sharp.Data.Tests/H2AggregateProviderTests.cs`:

- регистрация через провайдер, делегат и generic;
- снимок `Options` при регистрации;
- `InvariantToDuplicates` из провайдера;
- `IDisposable`-агрегат диспозится один раз в группе, `DISTINCT`, пустой
  группе, `OVER`, ordered `OVER` и при исключении.

`tests/H2Sharp.Tests/AggregateOracleTests.cs`:

- Java-fixture `Oracle$Median` через `CREATE AGGREGATE`;
- сравнение обычного вызова, `DISTINCT`, `FILTER`, `OVER`, пустой группы с
  `JavaAggregate`.

## Фаза 3 — только внутренний рефакторинг

Встроенные агрегаты не переводятся на `IH2Aggregate`. Допустимый рефакторинг —
выделение общего реестра фабрик `AggregateData` и вынесение ветвей
`CreateAggregateData` / `GetAggregatedValue` по типам с сохранением:

- `QuickAggregateQuery` и index-пути `MIN`/`MAX`;
- `GetSumType` / `GetAvgType`, точности и интервалов;
- `AggregateDataCollecting` для `DISTINCT` / `ORDER BY`;
- `LISTAGG`, JSON, `HISTOGRAM`, `MODE`, `RANK`-семейства.

Критерий приёмки — существующие тесты без изменений плюс характеризационные
тесты на типы результатов, пустую группу, `DISTINCT`, `OVER` и выбор
quick/index-пути.

## Вне scope

- `CREATE/DROP AGGREGATE`, `INFORMATION_SCHEMA`, `SCRIPT`, персистентность
  регистрации;
- MSSQL-сериализация (`Read`/`Write`, `MaxByteSize`, `Format.UserDefined`);
- `IsInvariantTo*`, типизированный `Accumulate`;
- DI-фабрики, partial/parallel merge.

## Проверка

```powershell
dotnet test tests/H2Sharp.Data.Tests --filter "FullyQualifiedName~H2AggregateTests"
dotnet test tests/H2Sharp.Tests --filter "FullyQualifiedName~AggregateOracleTests"
dotnet test H2Sharp.sln --filter "Category!=BlockedByPort"
```

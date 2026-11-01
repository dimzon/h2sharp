# Пользовательские агрегаты: план (утверждён 2026-11-01)

## Контекст

MSSQL CLR-агрегаты (`[SqlUserDefinedAggregate]`, `Init/Accumulate/Merge/Terminate`)
и H2-эталон (`org.h2.api.AggregateFunction` / `JavaAggregate`) показывают общий
паттерн. В порте пользовательских агрегатов нет: `AggregateData` internal,
`AggregateType` только встроенные. Исследование подтвердило готовые точки
расширения:

- `AbstractAggregate` / `DataAnalysisOperation` — каркас выражения агрегата;
- `SelectGroups` — состояние на группу (opaque-аккумулятор);
- `Parser.ReadFunction` — порядок резолва (built-in → UDF);
- `ExtensionConfiguration` / `ExtensionRuntime` — реестр расширений (UDF и др.);
- специальные пути встроенных: `QuickAggregateQuery`, `AggregateDataCollecting`
  (DISTINCT/ORDER BY), `GetSumType/GetAvgType`.

Требования пользователя:

1. Сначала инфраструктура, потом примеры, и лишь затем аккуратный перенос
   существующих агрегатов.
2. Публичные типы — в пространстве имён `H2Sharp.Data.Extensions`.
3. Проектировать с учётом производительности: без лишнего боксинга и создания
   объектов (горячие пути).

## Фаза 1. Инфраструктура

### Публичный контракт — `src/H2Sharp/Data/Extensions/` (`namespace H2Sharp.Data.Extensions`)

```csharp
public readonly record struct AggregateInput(
    IReadOnlyList<object?> Values,   // CLR-значения аргументов; буфер движка, не хранить после Add
    int RowIndex);

public interface IH2Aggregate
{
    TypeInfo GetResultType(IReadOnlyList<TypeInfo> inputTypes);
    void Init();
    void Add(AggregateInput input);
    object? GetResult();
    void Merge(IH2Aggregate other) { }
}

public interface IH2OrderedAggregate : IH2Aggregate
{
    void SetOrderBy(SortOrder order);
}

public sealed class AggregateOptions
{
    public bool Deterministic { get; set; }
    public bool IgnoreNulls { get; set; }
    public bool InvariantToDuplicates { get; set; }
    public bool InvariantToOrder { get; set; }
    public TypeInfo? ResultType { get; set; }
}
```

Базовые классы (в `H2Sharp.Data.Extensions`):

- `AggregateBase` — скелет (состояние + `AddValue`);
- `SimpleAggregateBase` — однопроходный накопитель (`Accumulate(object? value)`);
- `OrderedAggregateBase` (реализует `IH2OrderedAggregate`) — значения в
  гарантированном порядке;
- `CollectingAggregateBase` (реализует `IH2OrderedAggregate`) — собирает все
  входы, сортирует при ordered, вычисляет через `Compute(IReadOnlyList<AggregateInput>)`.

### Производительность в дизайне

- буфер значений переиспользуется движком (один массив на строку); контракт
  запрещает хранить ссылку после `Add`;
- `RowIndex` — value-тип, без боксинга; `GetResultType` вызывается один раз;
- базовые классы держат состояние в полях без лишних обёрток;
- `CollectingAggregateBase` хранит список входов (или лёгкий список значений) без
  record-обёртки на каждый элемент;
- при ordered: компаратор/сортировка настраиваются один раз через `SetOrderBy`;
- анти-паттерны документируются (конкатенации в цикле, `List<object>` в горячем пути).

### Регистрация и исполнение

- `IH2Extensions.RegisterAggregate<T>(name, Func<IH2Aggregate> factory, AggregateOptions options)`
  и `RegisterRawAggregate` (в `H2Sharp.Data`);
- `AggregateDefinition` в `ExtensionConfiguration`, снапшот в `ExtensionRuntime`
  (`HasAggregate/FindAggregate`);
- `ClrAggregate : AbstractAggregate` (internal, `Expressions/Aggregate/UserDefined/`):
  фабрика → `AggregateInput` → `GetResult` → ToValue; DISTINCT/FILTER/OVER
  наследуются; ordered пробрасывает `orderBySort`;
- `Parser.ReadFunction` — ветка после built-in агрегатов.

### Инфраструктурные тесты

Регистрация/видимость/закрытость соединения; smoke ручной реализации интерфейса;
базовые классы на тривиальных примерах.

## Фаза 2. Примеры

1. **string_agg**: `Concat : OrderedAggregateBase` —
   `SELECT CUSTOM_STRAGG(VAL, ',') FROM T GROUP BY GRP`; варианты
   DISTINCT/FILTER/NULL/пустая группа.
2. **Нестандартное**: `GeometricMean : SimpleAggregateBase` (агрегат, которого нет
   среди встроенных) + пользовательский агрегат на `CollectingAggregateBase`
   (например, `Median`-подобный) для полноты покрытия категорий.

Тесты: `tests/H2Sharp.Data.Tests/H2AggregateTests.cs`.

## Фаза 3. Аккуратный перенос встроенных

- Только после зелёных фаз 1-2; строго по одному агрегату с сохранением всех
  специализаций (`GetSumType/GetAvgType`, DISTINCT, OVER, QuickAggregateQuery,
  список `Aggregates`).
- Очередность: простые (SUM/MIN/MAX/COUNT/EVERY/ANY_VALUE/BIT*/BOOL_*) →
  AVG/STDDEV/VAR → MEDIAN/LISTAGG-стиль (только если Фаза 2 покажет уверенность);
  JSON/ENVELOPE/RANK-семейство остаются встроенными.
- Критерий приёмки: бинарная эквивалентность со старыми путями (существующие
  Specs/Data-тесты и оракул-тесты без изменений).

## Документация

`MIGRATION_STATUS.md`: модель MSSQL CLR → контракт `Data.Extensions`, фазы,
производительность, ограничения.

## Проверка

- Фаза 1: `dotnet test H2Sharp.sln` (зелёный).
- Фаза 2: `dotnet test tests/H2Sharp.Data.Tests --filter "FullyQualifiedName~H2AggregateTests"`.
- Фаза 3: полные Data/Specs + core-оракул без изменений; покрытие `ClrAggregate`
  и базовых классов в свежем coverage-наборе.

# Анализ покрытия (2026-11-02)

Прогон выполнен в Debug (`Release` инлайнит строки и искажает номера), поэтому
цифры сравнимы только с такими же Debug-прогонами.

## Как считалось

Использован `tools/coverage/run_coverage.ps1` и
`tools/coverage/merge_cov.py`. Отчёты лежат вне репозитория, в
`artifacts/TestResults`.

```powershell
.\tools\coverage\run_coverage.ps1
py -3 tools/coverage/merge_cov.py --results-dir artifacts/TestResults --min-lines 200 --top 25
```

`H2SharpSql.Specs` теперь тоже собирает coverlet: **884 passed, 68 skipped**,
и его отчёт входит в общий merge.

## Итоговые цифры

| Метрика | Покрыто | Всего | Процент |
|---|---:|---:|---:|
| Строки | 65 569 | 81 596 | **80.36%** |
| Ветви | 26 739 | 35 412 | **75.51%** |

По сборкам:

| Сборка | Строки | Ветви |
|---|---:|---:|
| `H2Sharp` | 80.35% | 75.53% |
| `H2Sharp.Crypto` | 93.57% | 86.84% |
| `H2Sharp.Extensions` | 85.65% | 77.50% |
| `H2Sharp.Hashes` | 100.00% | 100.00% |
| `H2Sharp.JavaLegacy` | 58.23% | 53.33% |

## Тесты в прогоне

| Сьют | Результат |
|---|---|
| `H2Sharp.Data.Tests` | 650 passed |
| `H2Sharp.MvStore.Tests` | 89 passed |
| `H2Sharp.Engine.Tests` | 9 passed |
| `H2Sharp.Tests` | 1557 passed |
| `H2SharpSql.Specs` | 884 passed, 68 skipped |
| `H2Sharp.Native.Tests` | 86 passed, 23 skipped; 12 тяжёлых тестов исключены (см. ниже) |

## Native-тесты: почему не весь набор

Полный native-прогон с coverlet не завершается за разумное время. Причина —
не зависание, а отдельные очень тяжёлые тесты и высокая цена coverlet на
каждый запуск testhost.

Проверка по одному без coverage (таймаут 60 секунд):

- 121 тест найден, 107 завершились;
- 14 не уложились в 60 секунд;
- контрольные повторные запуски показали:
  - `TestJsonUtils` — 55.5s;
  - `TestTransactionIsolation` — 68.7s;
  - `TestMvccMultiThreaded2` — 70s;
  - `TestBigResult` — 134.2s;
  - `TestCache`, `TestCachedQueryResults`, `TestAnalyzeTableTx` — более
    300 секунд каждая.

Это не дефекты и не deadlock:

- `TestCache` делает 10 000 + 10 000 вставок и затем 100 проходов по таблицам
  при маленьком кэше;
- `TestCachedQueryResults` выполняет 100 000 транзакций на 5 потоках;
- `TestAnalyzeTableTx` открывает 10 000 отдельных соединений и делает в них
  вставки.

Отдельный запуск одного теста без coverage стоит ~2 секунды, а с
`--collect:"XPlat Code Coverage"` — около 40 секунд на процесс. Поэтому
one-by-one с coverlet не используется.

Для coverage native-набор прогнан одним процессом с фильтром, исключающим
12 тяжёлых тестов:

```text
TestCache
TestMvccMultiThreaded2
TestCachedQueryResults
TestAnalyzeTableTx
TestBigResult
TestCases
TestIndex
TestLIRSMemoryConsumption
TestMemoryUsage
TestMultiThread
TestOptimizations
TestTempTables
```

Результат: **86 passed, 23 skipped**. Следовательно, 80.36% — это нижняя
оценка: покрытие, которое дают исключённые тяжёлые тесты, в него не входит.
Сравнивать с baseline 80.04% (2026-10-30) напрямую нельзя ещё и потому, что
после него добавились новые файлы провайдеров и реестров.

## Новый код

Покрытие файлов, добавленных/изменённых в последних изменениях:

| Файл | Строки |
|---|---:|
| `Data/Extensions/AggregateBase.cs` | 100.0% |
| `Data/Extensions/AggregateProvider.cs` | 100.0% |
| `Data/Extensions/AggregateRegistry.cs` | 97.7% |
| `Expressions/Aggregate/BuiltInClrAggregates.cs` | 89.8% |
| `Expressions/Aggregate/UserDefined/ClrAggregate.cs` | 88.3% |
| `Expressions/Functions/FunctionRegistry.cs` | 99.2% |
| `H2Sharp.Extensions/AggregateExtensions.cs` | 100.0% |
| `H2Sharp.Extensions/AggregateProviders.cs` | 100.0% |
| `H2Sharp.Extensions/StringAgg.cs` | 93.5% |
| `H2Sharp.Extensions/GeometricMean.cs` | 79.3% |
| `H2Sharp.Extensions/HarmonicMean.cs` | 62.1% |
| `H2Sharp.Extensions/Product.cs` | 86.7% |
| `H2Sharp.Extensions/WeightedAverage.cs` | 78.3% |
| `H2Sharp.Extensions/CountIf.cs` | 78.9% |
| `Extensions/AggregateValueConverter.cs` | 66.7% |
| `Data/Extensions/CollectingAggregateBase.cs` | 61.5% |
| `Data/Extensions/AggregateOptions.cs` | 75.0% |

Низкие цифры у `HarmonicMean`, `WeightedAverage`, `CountIf`,
`AggregateValueConverter` и `CollectingAggregateBase` — это в основном
непокрытые ветви валидации/ошибок и редкие преобразования типов. Это
кандидаты на точечные тесты, а не на переписывание.

## Наименее покрытые крупные файлы

Файлы от 200 строк, покрытие ниже:

| Файл | Покрытие |
|---|---:|
| `MvStore/Db/MVSortedTempResult.cs` | 0.0% |
| `Command/Query/Optimizer.cs` | 36.5% |
| `MvStore/Db/MVSpatialIndex.cs` | 42.0% |
| `Indexes/IndexCondition.cs` | 47.2% |
| `Expressions/Functions/MathFunction.cs` | 52.0% |
| `MvStore/Db/ValueDataType.cs` | 55.6% |
| `Command/Dml/ScriptCommand.cs` | 55.8% |
| `MvStore/MVStoreTool.cs` | 58.6% |
| `Util/DateTimeTemplate.cs` | 59.4% |
| `Command/Dml/Merge.cs` | 60.1% |
| `Result/LocalResult.cs` | 63.3% |
| `Expressions/SimpleCase.cs` | 63.4% |
| `Command/Dml/MergeUsing.cs` | 63.8% |
| `Expressions/Functions/BitFunction.cs` | 64.4% |
| `MvStore/RTree/SpatialDataType.cs` | 64.8% |

По каталогам заметнее всего проседают `Result` (62.45%), `Values` (62.73%),
`Tools` (61.99%), `Indexes` (65.89%) и `Message` (67.17%).

## Выводы

1. Подключение `H2SharpSql.Specs` подняло общий процент с 76.88% до **80.36%**
   по строкам и с 72.67% до **75.51%** по ветвям.
2. Новый реестр функций закрыт тестами хорошо: `FunctionRegistry` — 99.2%.
3. Реестр агрегатов и провайдеры закрыты лучше среднего: 88.3–100%.
4. Основной резерв покрытия — не новый код, а старые крупные ветви:
   оптимизатор, `MVSortedTempResult`, spatial/MVStore, `SET`/`MERGE`.
5. Native-набор стоит разделить на `Fast` и `Heavy` категории, иначе
   coverage-прогон всегда будет упираться в `TestCache`,
   `TestCachedQueryResults` и `TestAnalyzeTableTx`.
6. Для полного покрытия native нужно либо отдельное расписание для тяжёлых
   тестов, либо их собственный coverage-прогон без общего таймаута.

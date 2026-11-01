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

`H2SharpSql.Specs` в coverage не входит: у проекта нет coverlet-collector.
Он прогонялся отдельно: **884 passed, 68 skipped**.

## Итоговые цифры

| Метрика | Покрыто | Всего | Процент |
|---|---:|---:|---:|
| Строки | 62 735 | 81 596 | **76.88%** |
| Ветви | 25 734 | 35 412 | **72.67%** |

По сборкам:

| Сборка | Строки | Ветви |
|---|---:|---:|
| `H2Sharp` | 76.87% | 72.69% |
| `H2Sharp.Crypto` | 93.57% | 86.84% |
| `H2Sharp.Extensions` | 85.65% | 77.50% |
| `H2Sharp.Hashes` | 100.00% | 100.00% |
| `H2Sharp.JavaLegacy` | 50.63% | 43.33% |

## Тесты в прогоне

| Сьют | Результат |
|---|---|
| `H2Sharp.Data.Tests` | 650 passed |
| `H2Sharp.MvStore.Tests` | 89 passed |
| `H2Sharp.Engine.Tests` | 9 passed |
| `H2Sharp.Tests` | 1557 passed |
| `H2Sharp.Native.Tests` | 86 passed, 23 skipped; 12 тяжёлых тестов исключены (см. ниже) |
| `H2SharpSql.Specs` | 884 passed, 68 skipped; в coverage не участвует |

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

Результат: **86 passed, 23 skipped**. Следовательно, 76.88% — это нижняя
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
| `Expressions/Aggregate/UserDefined/ClrAggregate.cs` | 86.9% |
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
| `Command/Parser.Set.cs` | 41.0% |
| `MvStore/Db/MVSpatialIndex.cs` | 42.0% |
| `Command/Dml/Set.cs` | 45.9% |
| `Command/Dml/Merge.cs` | 46.1% |
| `Indexes/IndexCondition.cs` | 46.5% |
| `Command/Dml/ScriptCommand.cs` | 50.5% |
| `Expressions/SimpleCase.cs` | 50.7% |
| `Expressions/Functions/MathFunction.cs` | 50.9% |
| `Engine/Database.Settings.cs` | 53.9% |
| `MvStore/Db/ValueDataType.cs` | 55.6% |
| `Util/DateTimeTemplate.cs` | 55.9% |
| `MvStore/MVStoreTool.cs` | 58.6% |
| `Result/LocalResult.cs` | 62.7% |

По каталогам заметнее всего проседают `Indexes` (61.63%), `Result` (62.21%),
`Values` (62.73%), `Message` (67.17%) и `Expressions` (69.54%).

## Выводы

1. Новый реестр функций закрыт тестами хорошо: `FunctionRegistry` — 99.2%.
2. Реестр агрегатов и провайдеры закрыты лучше среднего: 86.9–100%.
3. Основной резерв покрытия — не новый код, а старые крупные ветви:
   оптимизатор, `SET`, `MERGE`, `MVSortedTempResult`, spatial/MVStore.
4. Native-набор стоит разделить на `Fast` и `Heavy` категории, иначе
   coverage-прогон всегда будет упираться в `TestCache`,
   `TestCachedQueryResults` и `TestAnalyzeTableTx`.
5. Для полного покрытия native нужно либо отдельное расписание для тяжёлых
   тестов, либо их собственный coverage-прогон без общего таймаута.

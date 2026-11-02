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

`H2SharpSql.Specs` теперь тоже собирает coverlet: **896 passed, 68 skipped**,
и его отчёт входит в общий merge.

## Итоговые цифры

| Метрика | Покрыто | Всего | Процент |
|---|---:|---:|---:|
| Строки | 66 618 | 81 596 | **81.64%** |
| Ветви | 27 206 | 35 412 | **76.83%** |

По сборкам:

| Сборка | Строки | Ветви |
|---|---:|---:|
| `H2Sharp` | 81.65% | 76.85% |
| `H2Sharp.Crypto` | 93.57% | 86.84% |
| `H2Sharp.Extensions` | 85.65% | 77.50% |
| `H2Sharp.Hashes` | 100.00% | 100.00% |
| `H2Sharp.JavaLegacy` | 58.23% | 53.33% |

## Тесты в прогоне

| Сьют | Результат |
|---|---|
| `H2Sharp.Data.Tests` | 654 passed |
| `H2Sharp.MvStore.Tests` | 89 passed |
| `H2Sharp.Engine.Tests` | 9 passed |
| `H2Sharp.Tests` | 1557 passed |
| `H2SharpSql.Specs` | 896 passed, 68 skipped |
| `H2Sharp.Native.Tests` | 86 passed, 23 skipped в file-профиле + 11 тяжёлых тестов в memory-профиле (см. ниже) |

## Прирост от последних тестов

| Область | Было | Стало |
|---|---:|---:|
| Общие строки | 81.18% | **81.64%** |
| Общие ветви | 76.42% | **76.83%** |
| `MathFunction.cs` | 58.7% | **80.8%** |
| `MVSortedTempResult.cs` | 0.0% | **51.4%** |
| `MVPlainTempResult.cs` | 0.0% | **51.2%** |
| `MVTempResult.cs` | 0.0% | **66.7%** |
| `SimpleCase.cs` | 63.4% | **64.8%** |
| `BitFunction.cs` | 64.4% | **70.5%** |

Добавлены тесты на типовые ветви `ROUND`/`TRUNC`/`MOD`/`ROUNDMAGIC`, на
многооперандный `CASE` и на внешние сортированные/обычные temp-результаты
через файловую БД с `MAX_MEMORY_ROWS`.

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

Результат: **86 passed, 23 skipped**.

### Memory-профиль для тяжёлых тестов

Добавлен opt-in `H2SHARP_NATIVE_MEMORY=1`: `TestBase` в этом режиме
устанавливает `Config.Memory = true`. Это только тестовый профиль; по
умолчанию поведение не меняется.

Полный native-прогон в memory-профиле занимает **5m11s** вместо часов:
**94 passed, 4 failed, 23 skipped**. Четыре падения — тесты, которые проверяют
именно файловое поведение (`TestSpaceReuse`, `TestReadOnly`,
`TestSynonymForTable`, `TestMultiThread`), поэтому memory-профиль не годится
как замена file-профиля.

Зато 11 из 12 тяжёлых тестов в memory-профиле проходят и резко ускоряются:

| Тест | File-профиль | Memory-профиль |
|---|---:|---:|
| `TestCache` | >300s | 2.3s |
| `TestCachedQueryResults` | >300s | 17.5s |
| `TestAnalyzeTableTx` | >300s | 3.6s |
| `TestBigResult` | 134.2s | 3.4s |
| `TestCases` | >60s | 4.6s |
| `TestIndex` | >60s | 9.3s |
| `TestMemoryUsage` | >60s | 2.7s |
| `TestOptimizations` | >60s | 9.5s |
| `TestTempTables` | >60s | 8.2s |
| `TestLIRSMemoryConsumption` | >60s | 49.8s |
| `TestMvccMultiThreaded2` | 70s | 71.9s |

`TestMultiThread` в memory-профиле падает (ожидает файловую таблицу), поэтому
остаётся вне coverage. Эти 11 тестов добавлены в coverage отдельными
memory-прогонами. Итоговые 81.18% — всё ещё не полное file-покрытие native,
но существенно ближе к нему, чем 80.36% без тяжёлых тестов.

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
| `MvStore/Db/MVSpatialIndex.cs` | 42.0% |
| `MvStore/Db/MVSortedTempResult.cs` | 51.4% |
| `Indexes/IndexCondition.cs` | 52.1% |
| `Command/Dml/ScriptCommand.cs` | 57.0% |
| `MvStore/MVStoreTool.cs` | 58.6% |
| `Util/DateTimeTemplate.cs` | 59.4% |
| `Command/Dml/Merge.cs` | 60.1% |
| `MvStore/Db/ValueDataType.cs` | 60.6% |
| `Command/Dml/MergeUsing.cs` | 63.8% |
| `Expressions/Functions/BitFunction.cs` | 70.5% |
| `Expressions/SimpleCase.cs` | 64.8% |
| `MvStore/RTree/SpatialDataType.cs` | 64.8% |
| `MvStore/RTree/MVRTreeMap.cs` | 65.7% |
| `MvStore/Tx/TransactionMap.cs` | 67.5% |
| `MvStore/Db/MVPrimaryIndex.cs` | 69.5% |

По каталогам заметнее всего проседают `Values` (62.73%), `Tools` (61.99%),
`Result` (66.83%), `Message` (67.17%) и `Indexes` (69.48%).

## Выводы

1. После трёх итераций тестов: общий процент **81.64% / 76.83%**; за последнюю
   итерацию добавлены `MathFunction` (58.7% → 80.8%), `BitFunction`
   (64.4% → 70.5%), внешние temp-результаты (`MVSortedTempResult`
   0% → 51.4%) и дополнительные формы `CASE`.
2. Новый реестр функций закрыт тестами хорошо: `FunctionRegistry` — 99.2%.
3. Реестр агрегатов и провайдеры закрыты лучше среднего: 88.3–100%.
4. Основной резерв покрытия — не новый код, а старые крупные ветви:
   spatial/MVStore, `IndexCondition`, `SET`/`MERGE`, `DateTimeTemplate`.
5. Memory-профиль (`H2SHARP_NATIVE_MEMORY=1`) ускоряет тяжёлые native-тесты
   в десятки раз, но не заменяет file-профиль: 4 теста проверяют файловое
   поведение и в memory-режиме падают.
6. Native-набор стоит разделить на `Fast`, `Heavy` и `FilePersistence`
   категории, чтобы coverage-прогон не упирался в `TestCache`,
   `TestCachedQueryResults` и `TestAnalyzeTableTx`.

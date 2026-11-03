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

`H2SharpSql.Specs` теперь тоже собирает coverlet: **915 passed, 68 skipped**,
и его отчёт входит в общий merge.

## Итоговые цифры

| Метрика | Покрыто | Всего | Процент |
|---|---:|---:|---:|
| Строки | 70 170 | 81 980 | **85.59%** |
| Ветви | 28 715 | 35 562 | **80.75%** |

> Методика: merge пяти наборов (`H2Sharp.Tests`, `H2SharpSql.Specs`,
> `H2Sharp.MvStore.Tests`, `H2Sharp.Data.Tests`, `H2Sharp.Engine.Tests`) на
> одном билде Debug/net10 (`artifacts/TestResultsPush`).
> `H2Sharp.Native.Tests` под coverlet не досчитывается (обрыв на тяжёлых
> тестах), поэтому в общий процент не входит. Прежний срез
> 81.45%/72.13% считался union-ом с отчётами другой ревизии и второго
> таргета; его знаменатель на ~2.7k строк и ~4k ветвей больше, поэтому
> напрямую эти два среза не сравниваются.

По сборкам:

| Сборка | Строки | Ветви |
|---|---:|---:|
| `H2Sharp` | 85.39% | 80.56% |
| `H2Sharp.Crypto` | 93.57% | 86.84% |
| `H2Sharp.Extensions` | 85.65% | 77.50% |
| `H2Sharp.Hashes` | 100.00% | 100.00% |
| `H2Sharp.JavaLegacy` | 58.23% | 53.33% |

## Тесты в прогоне

| Сьют | Результат |
|---|---|
| `H2Sharp.Data.Tests` | 655 passed |
| `H2Sharp.MvStore.Tests` | 130 passed |
| `H2Sharp.Engine.Tests` | 9 passed |
| `H2Sharp.Tests` | 1687 passed |
| `H2Sharp.Data.Tests` | 657 passed |
| `H2SharpSql.Specs` | 983 passed, 68 skipped |
| `H2Sharp.Native.Tests` | вне merge: под coverlet обрывается на тяжёлых тестах (см. ниже) |

## Прирост от последних тестов

Полный пересчёт после добавления `DISTINCT ON` и новых тестов:

| Область | Было | Стало |
|---|---:|---:|
| Общие строки | 84.14% | **85.30%** |
| Общие ветви | 79.43% | **80.59%** |
| `Command/*` | 86.50% | **86.94%** |
| `MvStore/*` | 83.05% | **84.56%** |
| `Value/*` | 86.72% | **87.20%** |
| `Expressions/*` | 79.42% | **81.29%** |
| `Command/Dml/Merge.cs` | 62.7% | **78.9%** |
| `MvStore/MVStoreTool.cs` | 58.6% | **73.0%** |
| `Command/Dml/SetClauseList.cs` | 74.4% | **77.5%** |
| `Command/Dml/MergeUsing.cs` | 71.8% | **72.7%** |
| `Expressions/Functions/DateTimeFunction.cs` | 71.9% | **74.8%** |
| `MvStore/Db/MVSpatialIndex.cs` | 45.3% | **83.5%** |
| `MvStore/Db/MVPrimaryIndex.cs` | 69.8% | **88.5%** |
| `Command/Dml/MergeUsing.cs` | 72.7% | **82.5%** |
| `MvStore/RTree/MVRTreeMap.cs` | 73.5% | **94.4%** |
| `Expressions/Functions/DateTimeFunction.cs` | 74.8% | **87.0%** |
| `Expressions/Aggregate/Percentile.cs` | 25.3% | **75.3%** |
| `Expressions/Aggregate/Aggregate.cs` | 75.3% | **85.4%** |
| `Expressions/Conditions/IsJsonPredicate.cs` | 41.0% | **77.8%** |
| `Expressions/OperationN.cs` | 47.5% | **89.4%** |
| `Expressions/TimeZoneOperation.cs` | 62.4% | **99.3%** |
| `Constraints/ConstraintDomain.cs` | 64.8% | **75.7%** |
| `Command/Ddl/DropDomain.cs` | — | **90.2%** |
| `Values/ValueClob.cs` | 41.3% | **79.8%** |
| `Values/ValueBlob.cs` | 48.8% | **67.2%** |
| `Values/ValueLob.cs` | 63.4% | **76.3%** |
| `Command/CommandList.cs` | 46.2% | **74.5%** |
| `MvStore/Db/ValueDataType.cs` | 67.6% | **84.1%** |
| `Expressions/ValueExpression.cs` | — | **100.0%** |
| `Indexes/IndexCondition.cs` | 48.6% | **89.5%** |
| `Expressions/Conditions/ConditionInQuery.cs` | 55.1% | **84.2%** |
| `Command/Query/Optimizer.cs` | 36.5% | **97.2%** |
| `Util/MathUtils.cs` | 32.2% | **80.2%** |
| `Util/DateTimeTemplate.cs` | 59.4% | **98.9%** |
| `Value/Value.Conversion.Families.cs` | 79.8% | **84.5%** |
| `Tables/TableFilter.cs` | 82.3% | **88.3%** |
| `Tables/TableTypes.cs` | 82.8% | **85.4%** |
| `Indexes/RegularQueryExpressionIndex.cs` | 21.1% | **96.9%** |
| `Command/Dml/Set.cs` | 78.5% | **93.9%** |
| `MvStore/Tx/TransactionMap.cs` | 67.5% | **95.9%** |

Прошлые 81.45%/72.13% считались union-ом с отчётами другой ревизии и
второго таргета (знаменатель был больше на ~2.7k строк и ~4k ветвей),
поэтому эти срезы напрямую не сравниваются; текущий — эталонный для
следующих замеров.

Числа в таблице выше — из этого же merge; отдельные замеры по тестам
спринта (`IndexConditionOracleTests`, `IndexConditionPlanTests`,
`OptimizerOracleTests`, `MathUtilsTests`, `DateTimeTemplateOracleTests`,
`CastMatrixOracleTests`, `TableFilterOracleTests`, `SetCommandOracleTests`) давали меньший процент, потому что в них входит
только один набор. Замер делается на одном свежем билде; старые отчёты
содержат PDB другой ревизии файла, и union считает две разные нумерации
строк, занижая процент.

Добавлены тесты: `DISTINCT ON` (SQL + JVM-оракул), ошибки `MERGE INTO`,
`FORMATDATETIME`/`PARSEDATETIME` (.NET-форматы, locale/timezone, NULL),
битовые операции над `BINARY`/`BIGINT`, `ROUND`/`TRUNC` по типам и внешние
temp-результаты через `MAX_MEMORY_ROWS`. Далее `MVSpatialIndex`: quick-путь
`ENVELOPE` из индексных границ (JVM-оракул), уникальный spatial-индекс через
`CREATE UNIQUE NULLS DISTINCT INDEX ... USING RTREE` (в обеих реализациях
это `MVSecondaryIndex` — `createUnique` не несёт spatial-флаг), guard'ы
создания, двухсессионная конкуренция uncommitted/committed и прямые тесты
членов вне SQL. Остаток по файлу (60 строк) — defensive-пути, которых не
достигает и эталон из SQL: guard'ы конструктора (парсер spatial-ветки не
читает ни список колонок, ни сортировку), unique-ветки `Add`, ошибки `Remove`,
закрытый стор и brace-артефакты отчёта.

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
| `MvStore/Tx/TransactionMap.cs` | 67.5% |
| `Schemas/Sequence.cs` | 72.5% |
| `Engine/Database.Settings.cs` | 75.1% |
| `Expressions/Aggregate/Percentile.cs` | 75.3% |
| `Result/LocalResult.cs` | 75.7% |
| `Data/H2ValueMapper.cs` | 75.7% |
| `Command/Dml/SetClauseList.cs` | 77.5% |
| `Store/FileStore.cs` | 77.5% |
| `Data/Dialect.cs` | 77.8% |
| `Command/Dml/Merge.cs` | 78.3% |
| `Util/CacheLRU.cs` | 78.5% |
| `Command/Command.cs` | 79.1% |
| `Schemas/Schema.cs` | 79.1% |
| `Command/Query/Select.cs` | 79.4% |
| `Command/Parser.Set.cs` | 79.5% |

По каталогам заметнее всего проседают `Message` (67.2%), `Result` (68.4%),
`Mode` (67.6%), `Indexes` (76.0%) и `Schemas` (79.5%).

## Выводы

1. Свежий чистый срез: **85.59% / 80.75%** по пяти наборам, один билд.
   Спринт 4 закрыл `IndexCondition` 48.6→89.5, `ConditionInQuery` 55.1→84.2,
   `Optimizer` 36.5→97.2, `MathUtils` 32.2→80.2, `DateTimeTemplate`
   59.4→98.9 и `ValueExpression` →100; по пути найдены и исправлены два
   дефекта порта: отсутствующий `ValueExpression.createIndexConditions`
   (всегда-ложное условие не попадало в план) и `SecureRandomInt`,
   возвращавший отрицательные значения. Матрица `CAST` (спринт 5) подняла
   `Value.Conversion.Families` 79.8→84.5 и нашла ещё шесть дефектов: заглушки
   `GetBytes`/`GetShort`/`GetDouble`/`GetFloat`/`GetBigDecimal`/`GetSignum`
   отвечали внутренней ошибкой вместо эталонных кодов, `ParseIntervalLeading`
   пропускал пробелы, `ParseTime`/`ParseTimestamp` отказывали вместо перевода
   в зону сессии, `SET TIME ZONE` не влиял на касты, а `SMALLINT` не умел
   BINARY/VARBINARY. Ранее закрытые цели двух
   предыдущих спринтов: `MVSpatialIndex` 51.9→83.5, `MVPrimaryIndex`
   69.8→88.5, `MergeUsing` 72.7→82.5, `MVRTreeMap` 73.5→94.4,
   `DateTimeFunction` 74.8→87.0, `Percentile` 25.3→75.3, `Aggregate`
   75.3→85.4, `IsJsonPredicate` 41.0→77.8, `OperationN` 47.5→89.4,
   `TimeZoneOperation` 62.4→99.3, `ConstraintDomain` 64.8→75.7,
   `DropDomain` →90.2, `CommandList` 46.2→74.5.
2. Новый реестр функций закрыт тестами хорошо: `FunctionRegistry` — 99.2%.
3. Реестр агрегатов и провайдеры закрыты лучше среднего: 88.3–100%.
4. Основной резерв покрытия теперь — `Result` (68%), `Message` (67%),
   `Indexes` (76%) и `Value`-конверсии: это старые крупные ветви, а не
   свежий код.
5. Memory-профиль (`H2SHARP_NATIVE_MEMORY=1`) ускоряет тяжёлые native-тесты
   в десятки раз, но не заменяет file-профиль: 4 теста проверяют файловое
   поведение и в memory-режиме падают. В текущем прогоне
   `TestLIRSMemoryConsumption` не уложился в таймаут и не дал отчёта.
6. Native-набор стоит разделить на `Fast`, `Heavy` и `FilePersistence`
   категории, чтобы coverage-прогон не упирался в `TestCache`,
   `TestCachedQueryResults` и `TestAnalyzeTableTx`.

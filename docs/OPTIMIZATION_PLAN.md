# План оптимизации H2Sharp

Версия эталона — H2 2.5.x; все оптимизации — внутренние детали реализации, не меняющие
наблюдаемую SQL-семантику. Каждая фича выполняется как отдельный спринт: замер «было»,
реализация, тесты, замер «стало», коммит с цифрами. Пропуск/упрощение фичи — только с
визой пользователя.

Источники находок:

* **A1** — [PERF_AUDIT_ALLOCATIONS.md](PERF_AUDIT_ALLOCATIONS.md) (2026-11-04);
* **A2** — [PERF_AUDIT_OFFHEAP_AND_HOT_PATHS.md](PERF_AUDIT_OFFHEAP_AND_HOT_PATHS.md) (2026-11-05);
* **A3** — [PERF_AUDIT_DOTNET8_10_2026-11-05.md](PERF_AUDIT_DOTNET8_10_2026-11-05.md) (2026-11-05).

A1 и A2 перепроверены по коду. A3 сверен построчно; его статус — в разделе
«Триаж A3»: часть пунктов повторяет A1/A2, часть противоречит эталону и поэтому
в план не входит. Разделы ниже ссылаются на пункты этих аудитов.

## Метрики

| Метрика | Источник | Назначение |
|---|---|---|
| `Allocated/op` | BenchmarkDotNet `MemoryDiagnoser` | эффект сокращения аллокаций |
| `Gen0/Gen1/Gen2/op` | `MemoryDiagnoser` | давление на сборщик |
| `HeapSizeBytes` (LOH) | `GC.GetGCMemoryInfo()` | эффект off-heap: LOH не растёт |
| `PrivateMemorySize64` | `Process` | полное потребление (off-heap виден здесь) |
| latency, ns/op | BenchmarkDotNet | регрессии по времени |

Для off-heap фич обязательны обе пары: `Allocated` **и** `PrivateMemorySize64` — иначе
не отличить «перенесли в NativeMemory» от «забыли освободить».

## Инфраструктура

`tools/perf/H2Sharp.Benchmarks` — консольный проект BenchmarkDotNet (Release).
Бенчмарки: `ParsePrepareBench` (варьируемый текст — против кэша prepared-команд),
`SelectRowsBench` (plain + ORDER BY — прямой курсор и LocalResult),
`LikeBench`, `BulkInsertBench`, `BlobInsertBench`.
`tools/perf/compare.ps1` — прогон двух наборов JSON и таблица «было/стало/Δ%»,
гейт регрессий времени (>5%).
Бенчмарки не входят в `dotnet test`; запускаются отдельно.

## Формат коммита

Сообщение коммита каждой фичи обязано содержать блок цифр:

```
S1: LocalResult — ноль аллокаций на строку

Было:  2.1.1  Allocated 1 024 B/op, Gen0 0.021/op
Стало: 2.1.1  Allocated     0 B/op, Gen0 0.000/op
Δ:     -100% allocated, тесты зелёные (filter Category!=BlockedByPort)
```

## Триаж A3 (2026-11-05)

A3 перепроверен построчно по текущему коду и эталону. Номера строк в основном точные,
но выводы расходятся с A1/A2 и с `reference/h2`: A3 в значительной части перечисляет
«как ускорить код эталона», а не дефекты порта. Поэтому в план вошли только
подтверждённые новые пункты; дубликаты сведены к уже существующим спринтам,
опровергнутые — в «Отклонено», оптимизации поверх эталона — в «Кандидаты».

| Пункт A3 | Вердикт | Куда |
|---|---|---|
| §1.3 `H2ValueMapper` | Подтверждён; это же A1 §2.5, приоритет Средний, не Критический | SP17 |
| §2.4 `H2ParameterRewriter` | Подтверждён; путь .NET-специфичный (named parameters), в эталоне аналога нет | SP18 |
| §1.2 `HexToRaw` | Подтверждён; эталон тоже делает `substring(i, i+4)`, правка внутренняя | SP19 |
| §4.5 `ThrowIf*` | Подтверждён; дубликат A1 §5 (214 мест) | SP20 |
| §3.3, §1.5 | Подтверждены, холодные/тривиальные | SP21 |
| §3.2 | Дубликат A1 §4; пути холодные | SP12 (дополнен) |
| §4.1 typed `MVMap<long,…>` | Эталон тоже держит `MVMap<Object, VersionedValue<Object>>` (`TransactionStore.java:648`) и боксит `Long` на границе; типизация — структурное отклонение | SP22 (виза) |
| §1.1 `CharSliceReader` | Эталон делает тот же `Substring` (`Value.java:795-801`); новый тип вне `org.h2` | SP23 (виза) |
| §4.3 `ValueTask` | Невыполнимо: переопределения `DbCommand`/`DbBatch`/`DbDataReader` возвращают `Task<T>`; A1 §6 уже отклонил переделку `SyncTask` | Отклонено |
| §4.2 `ReadNumeric` | Ошибка: делегаты — `static readonly` поля (`H2DataReader.cs:269-309`), аллокации на ячейку нет; компаратор в `IndexCondition` создаётся и в эталоне (`IndexCondition.java:192-197`) | Отклонено |
| §4.4 `ToArray` в циклах | Эталон делает то же: `SessionLocal.java:723`, `Parser.java:1592,1694`, `new Value[cnt]` в `LocalResult` | Отклонено |
| §1.4 `Slice().ToArray()` | A1 §6: копия нужна — значение владеет байтами дольше страницы | Отклонено |
| §3.1 `Aggregate` freeze | Статический `GetAggregateType(string)` в продакшене не вызывается (только `BuiltinSprintOracleTests.cs:43`); эффекта нет | Отклонено |
| §1.6, §2.1–2.3 | Верно по коду, но это оптимизация эталонных циклов, не дефект порта; нужен замер | Кандидаты |

### Кандидаты, ждущие бенчмарка

Пункты ниже не входят в спринты, пока замер не покажет стоимость. Они оптимизируют
циклы, унаследованные из эталона, поэтому не являются дефектами порта.

| Пункт A3 | Место | Что оптимизируется |
|---|---|---|
| §2.2 | `Util/DateTimeUtils.cs`, `Util/IntervalUtils.cs` | несколько `IndexOf`-сканов на каждый TIMESTAMP/TIME/INTERVAL; эталон — то же (`DateTimeUtils.java:321-330`) |
| §2.3 | `Util/StringUtils.cs` (`XmlText`, `JavaEncode`, `ArraySplit`) | посимвольные ветки; `JavaEncode`/`ArraySplit` уже имеют span/run-оптимизации, `SearchValues` добавит SIMD |
| §2.1 | `MvStore/DataUtils.cs` | посимвольный разбор map-заголовков при open/repair; эталон — то же |
| §1.6 | `Util/StringUtils.cs` (`UrlDecode`) | `new byte[length]` + старая перегрузка `GetString`; эталон — то же (`StringUtils.java:459-461`) |

## Фазы и спринты

### Фаза 0 — корректность (до любой оптимизации парсера/токенайзера)

Пункт **§6 аудита**: боевой `Parser` зовёт `Tokenizer.TokenizeForTest` со
зашитыми флагами (8 мест), тогда как эталон строит `Tokenizer` из режима БД.
Сначала привести боевой путь к эталонной модели — иначе оптимизации S2/§3.2/§2.7
закрепятся на неверном входном режиме (LOWER, keyword-as-name).

| # | Фича | Гейт |
|---|---|---|
| SP1 | §6: Parser строит Tokenizer из режима БД | `JavaOracleFact` на LOWER-режим; ParsePrepareBench без регрессии |

### Фаза 1 — горячие пути без визы (порядок из §7.1 аудита)

| # | Пункт аудита | Фича | Ключевая метрика | Риск |
|---|---|---|---|---|
| SP2 | §2.1 | `H2DataReader.GetBytes` — no-copy (`GetBytesNoCopy`) + `Span` | Allocated/op на BLOB | низкий |
| SP3 | §2.2 | `ValueBlob.CharLength`/`GetString`, `ValueClob` — подсчёт UTF-16 единиц без декода + кэш текста | Allocated/op, LOH | средний (тест U+10000) |
| SP4 | §2.3 | LongCache-зеркало JVM в `MVSortedTempResult` | Allocated на DISTINCT со spill | низкий |
| SP5 | §2.4 | кэш компаратора в `SortOrder` (`RowComparer`, `GetRowValueComparator`) | Allocated на оконные функции | низкий |
| SP6 | §2.5 | static `NullRowVisitor` в `TableFilter` | Allocated на LEFT JOIN | низкий |
| SP7 | §3.1 | SIMD Fletcher-32 (`Vector256<ushort>`) | ns/op на запись страницы | средний (гейт побайтовой идентичности + JavaOracleFact) |
| SP8 | §2.6 | заголовок/подвал чанка в `Span<byte>` | Allocated на сохранение чанка | средний (гейт байтовой идентичности) |
| SP9 | §2.7 | парсер: без `ToUpperInvariant`, `ReadIfWords(ReadOnlySpan<string>)` + статические массивы | Allocated на prepare | средний (36 мест) |
| SP10 | §2.8 | `Token`: union-layout (проверить допустимость overlap ссылок) + ёмкость `List<Token>` | Allocated на парсинг | средний (проверка TypeLoad) |
| SP11 | §3.2 | пять `SearchValues<char>`-мест токенайзера/`QuoteStringSql` | ns/op, Allocated на парсинг | низкий |
| SP12 | A2 §4, A3 §3.2 | FrozenDictionary-остаток: AggregateRegistry, MetaType, CaseFolding, ObjectDataType, ErrorMessages, H2ConnectionStringBuilder | Allocated на старт/первый вызов | низкий |
| SP17 | A1 §2.5, A3 §1.3 | `H2ValueMapper`: `ArrayPool<byte>` + одно точное копирование вместо `MemoryStream.ToArray()` и 64 KiB массива | Allocated/op на stream-параметр BLOB | низкий |
| SP18 | A3 §2.4 | `H2ParameterRewriter`: один `IndexOfAny` вместо двух полных сканов SQL-текста | Allocated/ns на statement с параметрами | низкий |
| SP19 | A3 §1.2 | `HEXTORAW`: разбор 4 hex-символов по `ReadOnlySpan<char>` без `Substring` | Allocated/op функции | низкий |
| SP20 | A3 §4.5, A1 §5 | `ThrowIf*`-остаток: адреса A3 §4.5 и 214 мест A1 §5 | IL size, ns/op в ADO hot path | низкий |
| SP21 | A3 §3.3, §1.5 | уборка: билдер `DataType` — локальная в `static` ctor; `Substring` в meta-скане MVStore → `AsSpan` | без метрики (холодные пути) | низкий |

> **Статус на 2026-11-05 (сверено по коду).** SP1, SP2, SP4–SP6, SP8, SP9, SP11, SP12
> закрыты; SP7 отозван — обоснование записано в `MvStore/DataUtils.cs` (векторная форма
> не доказуема побайтово на целевых платформах). SP10: `Token` уже struct и ёмкость
> списка выставлена; union-layout не делался — .NET запрещает overlap ссылок с
> примитивами в explicit layout, то есть пункт закрыт как невыполнимый. SP3: подсчёт
> UTF-16 единиц без декода уже есть (`ValueBlob.CharLength` → `Utf8.CodeUnitCount`);
> кэш текста в `LobDataInMemory` — не перенос, а новая структура (в
> `LobDataInMemory.java` только `byte[] small`), поэтому он вынесен в Фазу 2 (SP24).

### Фаза 2 — с визой пользователя (требуют записи в APPROVED_DEVIATIONS.md)

| # | Пункт | Причина визы |
|---|---|---|
| SP13 | §2.9 regex-кэш (`RegexpFunction`) | улучшение поверх эталона (паритет, не расхождение) |
| SP14 | §1.1 off-heap `WriteBuffer`/`Chunk.Buffer` | меняется модель учёта памяти MVStore |
| SP15 | §1.2 native-арена LOB | только в момент подключения блочного хранилища (LOB_STORE_PLAN) |
| SP16 | `MemoryFilePath` (из моего аудита, в документе отсутствует) | меняется модель памяти in-memory файла |
| SP22 | A3 §4.1 typed `MVMap<long,…>` / `TransactionMap` без `object` | структурное отклонение от `MVMap<Object, VersionedValue<Object>>` эталона; нужен бенчмарк, доказывающий эффект, и запись в APPROVED_DEVIATIONS.md |
| SP23 | A3 §1.1 `CharSliceReader` | новый тип вне `org.h2`; эталон делает тот же `Substring` (`Value.java:795-801`) |
| SP24 | A2 §2.2 кэш текста в `LobDataInMemory` | новая структура поверх эталона: в `LobDataInMemory.java` только `byte[] small`; строка удерживается вместе с байтами |

> **Статус на 2026-11-05.** SP23 закрыт (D-043: `CharSliceReader`, окно без копии);
> SP22 и SP24 остаются с визой.

### Отклонено/не предлагать (обоснование — в аудитах)

- §2.10 lock в `CacheLongKeyLIRS` — семантика видимости LIRS.
- §1.3 сырые байты страниц в off-heap — переопределение модели памяти MVStore.
- §5 (ObjectDataType боксинг, Value как reference type, LINQ-проверка) — контракт эталона.
- A3 §4.3 (`ValueTask` вместо `Task.FromResult`) — переопределения ADO.NET возвращают
  `Task<T>`; `ValueTask` в них не поставить, а A1 §6 уже отклонил переделку `SyncTask`.
- A3 §4.2 (`ReadNumeric` — «6 лямбд на ячейку») — делегаты объявлены как `static readonly`
  (`H2DataReader.cs:269-309`), аллокации на ячейку нет.
- A3 §4.4 (`ToArray` в циклах) — эталон делает то же: `SessionLocal.java:723`,
  `Parser.java:1592,1694`, `new Value[cnt]` в `LocalResult`.
- A3 §1.4 (`ValueDataType.Slice().ToArray()`) — копия нужна: значение владеет байтами
  дольше страницы (A1 §6).
- A3 §3.1 (`Aggregate` freeze) — статический `GetAggregateType(string)` в продакшене
  не вызывается; только тест `BuiltinSprintOracleTests.cs:43`.

## Приёмка спринта

1. Базовый замер на текущем состоянии (до фичи) сохранён в `artifacts/perf/baseline`.
2. Реализация не ломает `dotnet test --filter "Category!=BlockedByPort"` (релевантные проекты).
3. Повторный замер сохранён в `artifacts/perf/head`; Δ в коммите.
4. Для фич, меняющих аллокационную дисциплину, добавлен гейт аллокаций в стиле
   `tests/H2Sharp.Tests/*AllocationTests.cs` (`GC.AllocatedBytesForCurrentThread`
   как граница) и `JavaOracleFact`-тест там, где меняется наблюдаемое поведение.
5. Для фич с байтовой семантикой (§3.1, §2.6) — побайтовая идентичность + оракул.

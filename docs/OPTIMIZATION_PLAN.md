# План оптимизации H2Sharp

Версия эталона — H2 2.5.x; все оптимизации — внутренние детали реализации, не меняющие
наблюдаемую SQL-семантику. Каждая фича выполняется как отдельный спринт: замер «было»,
реализация, тесты, замер «стало», коммит с цифрами. Пропуск/упрощение фичи — только с
визой пользователя.

Источник находок — [PERF_AUDIT_ALLOCATIONS.md](PERF_AUDIT_ALLOCATIONS.md) и
[PERF_AUDIT_OFFHEAP_AND_HOT_PATHS.md](PERF_AUDIT_OFFHEAP_AND_HOT_PATHS.md)
(перепроверены по коду: все цитаты подтверждены). Разделы ниже ссылаются на пункты
этих аудитов.

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
| SP12 | §4 | FrozenDictionary-остаток: AggregateRegistry, MetaType, CaseFolding, ObjectDataType | Allocated на старт/первый вызов | низкий |

### Фаза 2 — с визой пользователя (требуют записи в APPROVED_DEVIATIONS.md)

| # | Пункт | Причина визы |
|---|---|---|
| SP13 | §2.9 regex-кэш (`RegexpFunction`) | улучшение поверх эталона (паритет, не расхождение) |
| SP14 | §1.1 off-heap `WriteBuffer`/`Chunk.Buffer` | меняется модель учёта памяти MVStore |
| SP15 | §1.2 native-арена LOB | только в момент подключения блочного хранилища (LOB_STORE_PLAN) |
| SP16 | `MemoryFilePath` (из моего аудита, в документе отсутствует) | меняется модель памяти in-memory файла |

### Отклонено/не предлагать (обоснование — в аудитах)

- §2.10 lock в `CacheLongKeyLIRS` — семантика видимости LIRS.
- §1.3 сырые байты страниц в off-heap — переопределение модели памяти MVStore.
- §5 (ObjectDataType боксинг, Value как reference type, LINQ-проверка) — контракт эталона.

## Приёмка спринта

1. Базовый замер на текущем состоянии (до фичи) сохранён в `artifacts/perf/baseline`.
2. Реализация не ломает `dotnet test --filter "Category!=BlockedByPort"` (релевантные проекты).
3. Повторный замер сохранён в `artifacts/perf/head`; Δ в коммите.
4. Для фич, меняющих аллокационную дисциплину, добавлен гейт аллокаций в стиле
   `tests/H2Sharp.Tests/*AllocationTests.cs` (`GC.AllocatedBytesForCurrentThread`
   как граница) и `JavaOracleFact`-тест там, где меняется наблюдаемое поведение.
5. Для фич с байтовой семантикой (§3.1, §2.6) — побайтовая идентичность + оракул.

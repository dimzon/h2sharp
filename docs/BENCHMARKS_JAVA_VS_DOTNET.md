# Бенчмарки Java vs .NET (эталон H2 2.5.x против порта H2Sharp)

Набор кросс-языковых бенчмарков: одна и та же нагрузка исполняется против
эталонной H2 на JVM (`reference/h2`, v2.5.x) и против порта H2Sharp на .NET.
Это инструментарий измерения, а не код движка: он не участвует в портировании
и не подпадает под правило «не изобретай» из AGENTS.md.

## Быстрый старт

```powershell
# один раз: собрать эталонный jar
(cd reference/h2/h2 && mvn -B -q -DskipTests package)

# прогнать обе стороны и напечатать таблицу
powershell tools/perf/bench-cross.ps1 -Label first
```

Требования: JDK (`javac`/`java` на PATH), .NET SDK, эталонный jar.
Результаты сохраняются в `artifacts/perf/cross/<label>-java.json` и
`<label>-dotnet.json`.

## Методология

Обе стороны измеряются **одним и тем же протоколом** — поэтому в измерениях нет
BenchmarkDotNet (он остаётся в `tools/perf/H2Sharp.Benchmarks` для регрессионного
гейта .NET-против-.NET).

Протокол на сценарий:

1. Открыть in-memory БД (`jdbc:h2:mem:` на Java, `Data Source=...;Storage=Memory`
   на .NET).
2. Setup вне тайминга: `CREATE TABLE`, наполнение данными.
3. 3 warmup-итерации.
4. 10 измеренных итераций, `System.nanoTime` / `Stopwatch.GetTimestamp`.
5. Вывод одной JSON-строки: `{"benchmark":"...","meanNs":...,"opsPerSec":...,"allocBytesPerOp":...}`
   (аллокации — только на .NET-стороне, `GC.GetAllocatedBytesForCurrentThread`).

Сценарий определён **парой драйверов с одинаковым SQL-текстом**: Java —
`tools/perf/java-bench/CrossBench.java`, C# — `tools/perf/H2Sharp.CrossBench`.
Тела сценариев — транслитерации существующих BenchmarkDotNet-бенчмарков
(`BulkInsertBench`, `SelectRowsBench`, `LikeBench`, `ParsePrepareBench`).

## Сценарии

| Сценарий | Нагрузка | Источник-эталон в BDN |
|---|---|---|
| `bulk_insert` | вставка 1000 строк в `TEST(A INT, B INT, C VARCHAR(50))` батчами по 500 | `BulkInsertBench` |
| `select_scan` | полный скан `SELECT A, B, C FROM TEST` по 10 000 строк с полным дренажем | `SelectRowsBench` |
| `like_filter` | `SELECT A FROM TEST WHERE C LIKE 'name1%'` по 10 000 строк | `LikeBench` |
| `parse_prepare` | разбор+план 16 вращающихся запросов без чтения строк | `ParsePrepareBench` |

| `select_order_by` | `SELECT A, B, C FROM TEST ORDER BY C` по 10 000 строк (LocalResult + сортировка) | `SelectRowsBench` |
| `update_rows` | `UPDATE TEST SET B = B + 1` по всем 10 000 строкам (перезапись с undo-логом) | — (новый) |
| `inner_join` | `SELECT J1.A FROM J1 JOIN J2 ON J1.A = J2.A WHERE J2.B > 50` по двум таблицам по 10 000 строк | — (новый) |
| `window_sum` | `SELECT ID, SUM(VAL) OVER (ORDER BY ID ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) FROM W` по 100 000 строк | — (новый; сценарий из OFFHEAP-аудита §7) |
| `blob_insert` | вставка BLOB 1 MiB как параметра в `LOB_T(B BLOB)` | `BlobInsertBench` |

## Как добавить сценарий

1. Добавить класс-сценарий в `CrossBench.java` и в `Program.cs` (.NET): `setup`
   (вне тайминга) и `runOnce` (измеряемая операция, возвращает контрольную сумму).
2. Зарегистрировать его в `main`/`Main` рядом с остальными.
3. SQL-тексты и размеры данных должны совпадать на обеих сторонах.
4. Прогнать `bench-cross.ps1` и убедиться, что новый сценарий появился в таблице.

## Результаты прогона расширенного набора (2026-11-06, label `ext`)

Машина: Windows 11 x64, JDK 25 (Adoptium), .NET 10, дефолтные настройки обеих сред.

| Сценарий | Java, ns/op | .NET, ns/op | Отношение Java/.NET |
|---|---|---:---|
| `bulk_insert` | 8 567 000 | 10 858 000 | 0.79x |
| `select_scan` | 1 304 000 | 319 000 | 4.08x |
| `select_order_by` | 834 000 | 512 000 | 1.63x |
| `like_filter` | 2 998 000 | 5 057 000 | 0.59x |
| `parse_prepare` | 797 000 | 156 000 | 5.10x |
| `inner_join` | 417 000 | 218 000 | 1.91x |
| `update_rows` | 19 707 000 | 78 524 000 | 0.25x |
| `window_sum` | 32 252 000 | 123 941 000 | 0.26x |
| `blob_insert` | 141 000 | 300 000 | 0.47x |

Цифры информационные: между прогонами разброс заметный (например, `like_filter`
на .NET в соседних прогонах давал 3.3 и 4.8 ms), поэтому выводы делать только по
нескольким устойчивым прогонам.

Направления по первому прогону:

- **Порт быстрее эталона:** `select_scan` (~4x), `parse_prepare` (~5x),
  `inner_join` (~1.9x), `select_order_by` (~1.6x) — трассы чтения/разбора.
- **Порт медленнее эталона:** `update_rows` и `window_sum` (~4x), `bulk_insert`
  (~1.3x), `like_filter` (~1.7x), `blob_insert` (~2.1x) — трассы записи,
  сортировки/агрегации с материализацией и LOB. `update_rows`/`window_sum` —
  кандидаты на следующий разбор (undo-лог и оконная материализация).
- Расхождение `parse_prepare` отчасти объясняется разными транзакционными
  протоколами по умолчанию (JDBC-автокоммит на каждое выполнение против поведения
  порта в in-memory режиме) — это семантика среды, а не дефект.

## Методическая поправка (SP-32): JIT-режим и свежесть сборки

Два искажения были обнаружены при разборе «5× проигрыша» и устранены в методике:

1. **Tiered compilation.** Дефолтный .NET JIT держит горячие методы в tier-0
   (quick JIT без инлайна), а JVM запускается на полном C2. На коротких
   бенчмарках это занижает .NET-сторону в 1.7–2×. `bench-cross.ps1` теперь
   запускает .NET с `DOTNET_TieredCompilation=0`.
2. **Свежесть сборки.** `dotnet run --no-build` для CrossBench использует свою
   копию `H2Sharp.dll`; после правок в `src` её нужно пересобирать через
   проект CrossBench, иначе A/B недостоверны.

## Итоговые цифры после SP-32 (методика с TC=0)

| Сценарий | Java, мс | .NET, мс | Отношение |
|---|---|---|---|
| parse_prepare | 0.47 | 0.027 | **17.3× порт быстрее** |
| select_scan | 1.11 | 0.19 | 5.8× |
| like_filter | 2.74 | 0.83 | 3.3× |
| inner_join | 0.30 | 0.11 | 2.8× |
| select_order_by | 0.69 | 0.26 | 2.7× |
| bulk_insert | 4.58 | 3.19 | 1.4× |
| raw_mapupdate | 0.97 | 1.49 | 1.5× медленнее |
| raw_put (tx) | 5.83 | 15.0 | 2.6× медленнее |
| raw_mapput | 2.26 | 7.19 | 3.2× медленнее |
| update_rows (SQL) | 12.0 | 47.1 | 4.0× медленнее |
| window_sum | 25.1 | 130.9 | 5.2× медленнее |

Что сделано по движку: `Page.Copy` через явные конструкторы вместо
`MemberwiseClone` (−23% на обновлении, −15% на вставке), long-поиск в
`Page.BinarySearch`. Попытка «fast-path Put» оказалась регрессом
(`GetType()`+`ConcurrentBag` дороже экономии) и откачена.

Остаток разрыва на записи — структура COW (2 массива на вставку, clone children
на каждом уровне, undo-слой) плюс GC-давление; аллокации .NET ~2× Java из-за
`long[]` (8 байт/элемент) против `Long[]` (4 байта со сжатыми ссылками).

## Горячие пути: профиль .NET-стороны (2026-11-06)

Профилирование (`dotnet-trace`, CPU-sampling) сценариев, где порт медленнее
эталона, показало: **доминирует аллокационный хрун и GC, а не алгоритмы** (пути
структурно повторяют Java).

| Сценарий | Доля GC (PollGC+Finalizers+MemberwiseClone) | Топ методов |
|---|---|---|
| `update_rows` | ~40% | `CursorPos.Decide` (23%), `MVMap.Operate` (13%), `Monitor.Enter_Slowpath` (14%), `GC.PollGC`/`RunFinalizers` |
| `bulk_insert` | ~30% | `DynamicResolver+DestroyScout.Finalize` (20%), `Page.Copy`/`MemberwiseClone` (19%), `Monitor.Enter_Slowpath` (11%) |
| `like_filter` | ~25% | `GC.PollGC` (18%), `MemberwiseClone` (6%), `CursorPos.Decide`/`Operate` |
| `window_sum` | ~18% | `GC.PollGC`+`GetMemoryPressure` (20%), `SortOrder.Sort` (6%), `UpdateOrderedAggregate` (4%) |
| `blob_insert` | низкая | `AllocateUninitializedArray` (4%), `MethodBaseInvoker` (4%), парсер |

Аллокации/op (из `allocBytesPerOp`): `update_rows` ~36 МБ на 10k строк
(3.6 КБ/строка), `window_sum` ~33 МБ на 100k строк, `bulk_insert` ~3.8 МБ на
1000 строк. Точечные источники: `PageLeaf.SetValue` клонирует массив значений на
каждое обновление (`MvStore/Page.cs:1507-1517`), `PageLeaf.InsertLeaf` пересоздаёт
массивы на каждую вставку (`Page.cs:1526-1542`), копии страниц на пути COW
(`Page.Copy`, `MvMap.ReplacePage`), двойная запись (undo-карта + карта данных)
как в Java.

**Разгон среды:** дефолтный .NET GC — workstation; дефолтный JVM GC — G1.
Прогон `update_rows` под ServerGC (`COMPlus_gcServer=1`) даёт ~60→24.5 мс
(аллокации те же 36 МБ), что почти закрывает разрыв с Java (~16.7 мс).
Для честного сравнения с JVM запускать .NET-сторону под ServerGC, либо
фиксировать режим обеих сторон в протоколе.

**Спринт-1 (код):** убран временный буфер и лишняя копия при записи LOB-блоков
(`MvStore/Db/LobStorageMap.Impl.cs`, `StoreStream`) — прямой ввод в целевой массив
блока. Семантика не менялась: `H2Sharp.Data.Tests` 719/719, `H2Sharp.MvStore.Tests`
152/152, аллокационные гейты 86/86 зелёные. Эффект на этой шкале — в пределах
шума (см. ниже); настоящий выигрыш — в сокращении аллокаций страничного слоя
(`SetValue`/`InsertLeaf`), это следующий спринт.

**ServerGC (протокол):** `bench-cross.ps1 -ServerGC` запускает .NET-сторону
с серверным GC (`COMPlus_gcServer=1`) — ближе к дефолтному G1 у JVM.
Контролируемый A/B на `update_rows` (3 раунда, чередование): workstation
~61–66 мс, servergc ~52–113 мс (median ~52) — выигрыш небольшой и шумный.
Раннее одиночное измерение 24.5 мс под ServerGC оказалось артефактом тихого
момента и не воспроизводится. Вывод: на этой машине режим GC — вторичный фактор;
доминирует фоновый шум (см. ниже).

**Шум машины:** BDN-прогоны одного и того же baseline в разные моменты дня дают
разброс +10…60% на CPU-бенчмарках (parse/like/transactionmap), при идентичных
аллокациях и плане питания; java-процессы, запущенные вне прогонов,
присутствуют (сотни секунд CPU). Сравнения «было/стало» на этой машине требуют
прогона обеих сторон в одной сессии и нескольких повторов с медианой.

## Профилирование записи: что нашлось (SP-32…SP-34)

Профили trapped `dotnet-trace` + разбор стеков через speedscope дал три находки:

1. **`MemberwiseClone` 23% CPU** — не из `Page.Copy` (исправлен раньше), а из
   **`Array.Clone()` для массивов ссылочных элементов** (`NonLeaf.SetChild`
   → `children.Clone()`, `Leaf.SetValue` → `values.Clone()`, `Page.SetKey`).
   В CoreCLR клон массива ссылок идёт через MemberwiseClone-путь. Замена на
   `GC.AllocateUninitializedArray` + `Array.Copy` дала: обновление 1.31 → **0.89 мс
   (паритет с Java)**, вставка 6.44 → **5.79 мс (быстрее Java 6.23)**.
2. **GC-паузы 4.07 мс на 10k строк** при 28 МБ мусора за прогон (gen0=4,
   gen2=0); бюджеты gen0 не помогают — стоимость в самом объёме аллокаций.
3. **TX-путь**: put 2091 Б/op + commit 331 Б/op; фазовый замер —
   .NET put 1038 нс + commit 107; Java put 781 + commit 166. Остаток tx-разрыва
   (1.72×) — `Record<object,object>` (бокс ключа) и `VersionedValueUncommitted`
   на каждую операцию плюс COW-копии страниц.

Методическая ошибка сценариев `raw_mapput`/`raw_put` (фиксированные ключи →
измерялись обновления) исправлена: диапазон ключей сдвигается на прогон.

## Оговорки честности

- **Дефолтные настройки сред:** JVM и .NET работают с дефолтными GC
  (на .NET — Concurrent Workstation). Если цель — сравнить конкретные режимы,
  запускать обе стороны с одинаковыми явными флагами.
- **Шум измерений:** 10 итераций достаточно для относительного сравнения на одной
  машине, но не для абсолютных цифр. Прогонять несколько раз (`-Label run1`,
  `run2`, ...) и смотреть на устойчивость.
- **Не сравнивать абсолютные числа между машинами** — только прогоны на одной
  машине в одном состоянии.
- **Это не гейт:** `bench-cross.ps1` не завершается с ошибкой при регрессе
  (в отличие от `compare.ps1`). Гейт по кросс-языковому соотношению можно добавить
  позже, если он понадобится.

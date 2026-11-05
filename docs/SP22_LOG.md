# SP22 — журнал спринтов: типизированные транзакционные карты

Цель и полный дизайн — [SP22_TYPED_TRANSACTION_MAPS.md](SP22_TYPED_TRANSACTION_MAPS.md).
Каждый спринт: код, тесты, результаты, следующий спринт. Задача закрывается,
когда primary/secondary работают на `MVMap<K, VersionedValue<object>>` без бокса ключа,
формат переживает reopen, гейты зелёные.

## Спринт SP22.1 — негенерный доступ для undo/rollback (сделан)

**Коммит:** `b5afefd`.

**Проблема.** Rollback и undo-лог оперируют стёртыми типами (`object`-ключ,
`VersionedValue<object>`); `MVMap<K, VersionedValue<V>>` такой вызов не примет.

**Решение.** `IObjectMapAccess` — негенерный доступ (`GetObject`/`OperateObject`/
`WriteKey`/`ReadKey`/`WriteValue`/`ReadValue`/`GetKeyMemory`/`GetValueMemory`),
реализуемый `MVMap<K,V>`; ключ кастуется к K (для `K=long` — unbox без бокса).
`RollbackDecisionMaker`, `RollbackListener`, `Record.Type`, commit-путь
`TransactionStore` переведены на интерфейс.

**Результаты.** Поведение и формат не изменились. MVStore 152/152, ADO 719/719,
transaction/rollback 5/5 (+3 skip).

## Спринт SP22.2 — generic builder (сделан)

**Коммит:** `a43b033`.

**Решение.** `TxMapBuilder<K>`/`TMVMap<K>`; `TransactionStore.OpenVersionedMap<K>`
открывает `MVMap<K, VersionedValue<object>>` с настоящим `DataType<K>`; при reopen
регистр разворачивает `ObjectMapDataType` обратно в underlying-тип и кастует к K.
Object-перегрузки сохранены; typed-путь пока не вызывался.

**Результаты.** MVStore 152/152, ADO 719/719, transaction 5/5 (+3 skip).

## Спринт SP22.3a — TransactionMap на типизированном ключе (сделан)

**Коммит:** этот.

**Решение.**
* `ITransactionMapData<K>` — дата-плоскость транзакционной карты; две реализации:
  `TypedMapData<K>` над `MVMap<K, VersionedValue<object>>` и `ObjectMapData<K>` над
  прежней `MVMap<object, VersionedValue<object>>` (R-tree, system maps, карты без
  типа ключа). Кэш боксов малых long переехал в `ObjectMapData`.
* `TransactionMap<K,V>` хранит `ITransactionMapData<K>`; ключи в `Get`/`Put`/
  `Operate`/`Append`/снапшотах идут типизированными (без `BoxKey`), итераторы
  получают `ITransactionCursor<K>` (`Cursor<K,V>` реализует интерфейс;
  object-плоскость — через `ObjectTransactionCursor<K>`).
* `Snapshot` негенерный (корень как object — один снапшот-сет межтиповой);
  `ITransactionMap.FlushAndGetRoot()` возвращает object; `MarkStatementStart`
  разделён: без аргументов и `MarkStatementStartTyped(HashSet<ITransactionMap>)`.
* `Transaction.OpenMap<K,V>`: непустой `keyType` → typed-плоскость (`MVPrimaryIndex`
  с `LongDataType`, `MVSecondaryIndex` с `RowDataType`); null → object-плоскость
  (R-tree, system maps, карты тестов).
* `IObjectMapAccess` и `Record.Type` работают с обеими плоскостями без изменений
  (stage 1). Формат файла не меняется: ключ и значение пишутся теми же
  `DataType`, что писал object-адаптер.

**Результаты (Debug, net10.0, новый worktree; оракул — собранный jar эталона):**
* `H2Sharp.MvStore.Tests` — 152/152.
* `H2Sharp.Data.Tests` — 719/719.
* `H2Sharp.Tests` — 1949/1950; native transaction/LOB — 3/3; allocation-гейты 5/5.
* **Новый гейт:** `GetImmediate(1_000_000)` — **0 B/op** (было 24 B на
  `TransactionMap<long,…>`); `Put` для id > 127 остаётся с одним боксом в undo-лог —
  как `Long.valueOf` в эталоне.

**Единственное падение полного набора:** `ScriptCommandOracleTests.
RunScriptRoundTripsTheScript` — `RUNSCRIPT FROM …` отказывает парсером
(`expected "statement"`, 42001-259). Воспроизводится на **main без SP22-правок**
(проверено точечным прогоном), то есть пришло с параллельными коммитами парсера
(`73e667b`/`872157c`) и к типизированным картам не относится. Зафиксировано как
дефект main отдельным вопросом, в ветке SP22 не чинится.

**Примечание по окружению.** Свежий worktree не содержит gitignored-клон
`reference/h2` — оракул-тесты без него скипаются/падают с «H2 jar not found».
Скопирован из основного checkout'а; walk-up `JavaOracle` поднимается только по
родителям, поэтому worktree обязан иметь свой `reference/h2`.

## Спринт SP22.3b — бенчмарк ключевого пути (сделан)

**Коммит:** `2e4e0b2`.

**Решение.** `tools/perf/H2Sharp.Benchmarks/TransactionMapBench`: карта на
100 000 строк, `GetImmediate` для id 42 (малый, из кэша боксов) и id 99 999
(большой), `Put` поверх большого id. Baseline снят тем же бенчмарком на main
(до SP22, object-плоскость), after — на ветке (typed-плоскость).

**Результаты (Release, net10.0):**

| Метод | Было (main) | Стало (SP22) | Δ |
|---|---|---|---|
| GetSmallId | 44.7 ns, 0 B | 31.3 ns, 0 B | время −30% |
| GetLargeId | 23.2 ns, **24 B** | 16.7 ns, **0 B** | время −28%, бокс −24 B/op |
| PutLargeId | 1477 ns, 2340 B | 1583 ns, 2314 B | −26 B/op (бокс ключа), время в пределах шума (±395 ns) |

`Put` сохраняет один бокс в undo-лог — `Record` хранит `object`-ключ, и эталон
боксит так же через `Long.valueOf`. Сырые отчёты — `artifacts/perf/sp22-before`
(main) и `artifacts/perf/sp22-after` (ветка).

## Спринт SP22.4 — совместимость формата (сделан)

**Решение.** Два консольных прозвона вне репозитория (один ссылается на H2Sharp
из main, другой — из ветки): `write` создаёт файловую БД с таблицей
(INT PRIMARY KEY, VARCHAR, BLOB) и тремя строками, включая LOB; `read` открывает
её и печатает строки с `LENGTH(X)`. Матрица: old→new, new→old, new→new.

**Результаты.** Все три направления читают одинаковые данные
(`1|one|3`, `2|two|4`, `3|three|null`): метаданные карты (ключ регистрации типа —
тот же хэш реального `DataType`) и байты страниц совпадают. Формат не меняется,
reopen в обе стороны работает.

## Спринт SP22.5 — итоги (сделан)

* Primary и secondary индексы работают на `MVMap<long/SearchRow,
  VersionedValue<object>>` — бокс ключа на чтении убран полностью.
* R-tree и system maps осознанно остаются на object-плоскости (`ObjectMapData`);
  типизация R-tree — отдельная механическая работа вне SP22.
* Гейты: `GetImmediate(1_000_000)` 0 B/op (было 24 B); Put несёт один бокс
  undo-лога — паритет с эталоном.
* Полные наборы: MVStore 152/152, ADO 719/719, H2Sharp.Tests 1949/1950
  (единственное падение — `RunScriptRoundTripsTheScript`, воспроизводится на
  main без SP22, дефект парсера из параллельных коммитов), native
  transaction/LOB 3/3.
* Issue #88: закрыть после мержа ветки — бокс на чтении убран, кэш малых long
  остаётся для object-плоскости.

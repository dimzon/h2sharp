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

## Следующий спринт

**SP22.3b** — primary/secondary на typed-плоскости под allocation-гейтами
(частично уже переведены в 3a), бенчмарк id > 127, затем SP22.4 (reopen) и
SP22.5 (итоги).

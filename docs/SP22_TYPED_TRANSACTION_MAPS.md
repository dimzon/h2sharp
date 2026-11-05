# SP22: типизированные транзакционные карты

Цель: `TransactionMap<K,V>` и лежащая под ней `MVMap` снова типизированы по ключу
(`MVMap<K, VersionedValue<V>>`), как в эталоне (`TransactionMap.java`,
`TransactionStore.openVersionedMap`), вместо `MVMap<object, VersionedValue<object>>`
и бокса `long` на каждом обращении. Задача — #88, ветка SP22.

## Почему это не одна правка

1. **ObjectMapDataType.** Порт слил `TransactionStore` в один object-keyed класс;
   `ObjectMapDataType<T>` «притворяется» типизированным DataType<object> и кастует
   `(T)` на каждом Compare/Write/Read.
2. **Итераторы.** `UncommittedIterator`, `CommittedIterator`, `RepeatableIterator`,
   `TMIterator` работают с `Cursor<object, VersionedValue<object>>` и `object key`.
3. **R-tree.** `MVRTreeMap<V> : MVMap<object,V>` с `ObjectMapDataType<Spatial>`;
   его внутренности — `Page<object,V>` и касты `(Spatial)`.
4. **Undo/rollback.** `Record<object,object>` хранит `object`-ключ; rollback и
   `Record.Type` достают карту по id и оперируют стёртыми типами. В Java это
   работает из-за erasure; в C# `MVMap<long, …>` нельзя привести к
   `MVMap<object, …>`, а `VersionedValue<SearchRow>` — к `VersionedValue<object>`.
5. **Кэш карт.** `MVStore.maps` — `ConcurrentDictionary<int,IAnyMap>`;
   `GetMap<K,V>` кастует к `MVMap<K,V>`, поэтому карта должна открываться
   ровно с теми K/V, с которыми её потом достают.

## Выбранная форма

Значения остаются `VersionedValue<object>` (это уже так; V стирается в `TxDecisionMaker`),
типизируется **ключ**. Для карт, которые нельзя типизировать (R-tree, system maps),
остаётся object-keyed реализация.

* `IObjectMapAccess` (сделано, stage 1) — негенерный доступ к versioned-карте:
  `GetObject`/`OperateObject`/`WriteKey`/`ReadKey`/`WriteValue`/`ReadValue`/
  `GetKeyMemory`/`GetValueMemory`. Реализуется `MVMap<K,V>`; ключ кастуется к K
  (для `K=long` — unbox без бокса).
* `ITransactionMapData<K>` (stage 2) — то, чем `TransactionMap<K,V>` пользуется
  вместо прямого `MVMap`: `Get/Get(root,key)/Put/Operate/Append/Remove`,
  `Cursor(root,from,to,reverse)`, `GetKeyType`/`CompareKeys`, `Inner` (`IAnyMap`).
  Две реализации:
  - `TypedMapData<K>` над `MVMap<K, VersionedValue<object>>` (primary, secondary);
  - `ObjectMapData<K>` над `MVMap<object, VersionedValue<object>>` (spatial,
    system maps; бокс только там, где K — не long).
* `Snapshot` хранит корень как `IRootView`/`object`; `ITransactionMap` не
  выставляет типизированный root, а получает `SetStatementSnapshot(long[] committing)`.
* `TxMapBuilder<K>`/`TMVMap<K>` — generic builder поверх существующего
  `VersionedValueType<object, Database>`; ключ пишется настоящим `DataType<K>`.
  `ObjectMapDataType` для ключа больше не нужен (для значения остаётся).
* Rollback/undo идут через `IObjectMapAccess`, поэтому типизированная карта им
  подходит.

## Этапы

1. **Stage 1 (сделано).** `IObjectMapAccess`; `MVMap<K,V>` реализует его;
   `RollbackDecisionMaker`, `RollbackListener`, `Record.Type`, commit-путь
   `TransactionStore` переведены на интерфейс. Поведение и формат не меняются.
2. **Stage 2 (сделано).** `TxMapBuilder<K>`/`TMVMap<K>`,
   `TransactionStore.OpenVersionedMap<K>`; регистр разворачивает
   `ObjectMapDataType` обратно в реальный тип и кастует его к K. Object-перегрузки
   сохранены; typed-путь пока никем не вызывается — это Stage 3.
3. **Stage 3.** `ITransactionMapData<K>` и `TransactionMap` на нём; итераторы
   получают типизированный cursor/root. Secondary index — `MVMap<SearchRow,…>`.
4. **Stage 4.** R-tree: либо `MVRTreeMap<V> : MVMap<Spatial,V>` (механическая
   замена `Page<object,V>`/кастов), либо spatial остаётся на `ObjectMapData`.
5. **Stage 5.** Формат/совместимость: reopen файла, созданного до/после;
   `Record.Type`/`GetDataTypeRegistrationKey` не должны изменить байты и имена типов.

## Гейты

- `H2Sharp.MvStore.Tests` (152), `H2Sharp.Data.Tests` (719), native
  transaction/LOB-наборы, `TransactionMapAllocationTests`.
- Allocation-гейт: `GetImmediate`/`Put` для id 1_000_000 — 24 B → 0.
- File reopen: файл, записанный stage-2 кодом, читается stage-1/эталонным
  форматом и наоборот.
- Никаких `MVMap<object, VersionedValue<object>>` в primary/secondary после
  stage 3; `ObjectMapDataType` остаётся только для spatial/system.

## Риск

Средний-высокий: транзакции, откат, secondary/spatial индексы, персистентность.
Каждый этап — отдельный коммит с прогоном полного MVStore/ADO/transaction набора.

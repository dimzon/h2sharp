# План спринта: честные заглушки вместо тихих ответов

База: HEAD `31ffa95` + незакоммиченные `CurrentGeneralValueSpecification.cs`
(P-001, утверждён), `AGENTS.md` (универсальное правило), черновик
`docs/APPROVED_DEVIATIONS.md`.

## Фаза 0. Документы (готово, проверить)

- [x] AGENTS.md: раздел «Универсальное правило» + ссылка на таблицу.
- [x] docs/APPROVED_DEVIATIONS.md: разделы A (D-001…D-007) и B (P-002…P-008).
- [x] Проверить файл чтением целиком после всех правок.

## Фаза 1. Код — дословные копии и утверждённые решения

- [x] 1.1. `Database.GetDatabasePath()`: `null` для mem, TODO для persistent
  (недостижимо: open файлов бросает TODO). `SysInfoFunction.DATABASE_PATH`
  делегирует ему (убрать hardcoded null).
- [x] 1.2. `TRANSACTION_ID`: `session.Transaction` + `HasChanges()` → seq,
  иначе NULL (всё есть в порту).
- [x] 1.3. `MEMORY_FREE/USED` → `TODOException` (платформа не воспроизводит
  семантику кучи JVM); 2 теста — в `BlockedByPort` с пометкой.
- [x] 1.4. `CANCEL_SESSION` по найденной сессии → вызывать `s.Cancel()`
  (там уже честный TODO); `ABORT` — `Close()` + TRUE.
- [x] 1.5. Дефолты из эталона: `lockMode = DefaultLockMode` (3, а не 0),
  `lockTimeout` сессии из `database.GetLockTimeout()` (2000, а не 0).
- [x] 1.6. NextIs-костыли → обратно на починенный `ReadIfWords`
  (`ReadIsJsonPredicate`, `ReadIgnoreNulls`).
- [x] 1.7. `TableInfoFunction.IsEverything`: DETERMINISTIC → false (пропущено).
- [x] 1.8. `ValueVarchar.Get(s)` → с `session.CastDataProvider` там, где есть
  сессия (мои новые места: DBObject SQL, SysInfo строки, Current specs).
- [x] 1.9. `ROWNUM`: поля `currentSelect/currentPrepared` в парсере с
  save/restore + fallback (вместо syntax error без контекста).
- [x] 1.10. `CURRENT_ROLE='PUBLIC'` const — уже в дереве, только проверка.

## Фаза 2. Проверка

- [x] 2.1. Сборка (`TreatWarningsAsErrors` — чисто).
- [x] 2.2. Точечные тесты: SysInfo ×10, DBObject ×10, ROWNUM, TruncateValue,
  DECODE/CASEWHEN/NVL2/CONVERT, SET-термин, GREATEST/LEAST, CURRENT_*.
- [x] 2.3. Полные наборы: H2Sharp.Tests (1259), MvStore (86), Data (467),
  Specs green-filter — сверить счётчики с базой 641/302, новых падений: 0.
- [x] 2.4. `git diff` построчно против эталона; сверить `APPROVED_DEVIATIONS.md`.
- [ ] 2.5. Коммит + пуш (только по команде).

## Открыто (в работу НЕ брать без визы)

- DbSettings-таблица: реализовывать или фиксировать отсутствие.
- `ESTIMATED_ENVELOPE`/пропущенные kinds: NULL + пометка «дописать при
  появлении фичи» — подтвердить формулировку.
- Файл таблицы: оставить `docs/APPROVED_DEVIATIONS.md`?

## Фаза 3. Файловая БД (в работе)

Кода нет в `Database`/`Engine` до этой фазы: `Engine.openSession` бросал TODO на
файловом URL, конструктор `Database` — тоже.

- [x] 3.1. Тесты совместимости написаны ПЕРВЫМИ (`FileDatabaseOracleTests`):
  порт→Java, Java→порт, переоткрытие, `DATABASE_PATH`.
- [x] 3.2. `Engine.openSession`: ветка `IFEXISTS`/`FORBID_CREATION`, `.mv.db`,
  старый `.data.db` → FILE_VERSION_ERROR, read-only при не-writable файле;
  `ConnectionInfo` получил `RemoveProperty(bool)` и `SetProperty`.
- [x] 3.3. `Store` портирован (ctor, accessors, `convertMVStoreException`,
  close/flush); `Database.GetDatabasePath()` через `FileUtils.ToRealPath`,
  `GetPageSize()` из `PAGE_SIZE`; `RemoveSession`/`Close` по `closeDelay`.
- [x] 3.4. Совместимость формата: `MetaType` пишет/читает **Java-имена** типов
  (`org.h2.*`), `RowDataType` стал `StatefulDataType` с `Factory`,
  `VersionedValueType.Factory`, `LobStorageMap.BlobReference/BlobMeta.Type`,
  реестр типов хранит `object` и регистрирует тип значения (reference).
  Итог: Java открывает файл порта, порт открывает файл Java.
- [ ] 3.5. Словарь схемы: таблица `SYS` (`createSysTableData`), `addMeta`,
  `updateMeta`, `removeMeta`, `executeMeta`, восстановление `objectIds`.
  Сейчас `FileSurvivesCloseAndReopen` красный: «Table T not found» — данные
  в мапах есть, определения таблиц не сохранены. Тесты помечены
  `BlockedByPort` до этого шага.
- [ ] 3.6. DDL-хуки: `AddSchemaObject`/`AddDatabaseObject`/`RemoveSchemaObject`
  пишут/удаляют meta-строку (сейчас стоит комментарий «deferred»).
- [ ] 3.7. Lock-файл (`FileLock`, `DATABASE_ALREADY_OPEN_1`). Отложено:
  внутрипроцессная блокировка `SingleFileStore.OpenFiles` уже есть, файл
  `.lock.db` и межпроцессная — следующий шаг.
- [ ] 3.8. `DB_CLOSE_ON_EXIT` (AppDomain.ProcessExit) — не сделано.
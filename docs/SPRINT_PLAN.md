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
- [x] 3.5. Словарь схемы: таблица `SYS` (`createSysTableData`), `addMeta`,
  `updateMeta`, `removeMeta`, `executeMeta`, восстановление `objectIds`.
  Плюс разбор строк эталона: `CREATE CACHED/MEMORY TABLE`, `CREATE PRIMARY KEY`
  (quirks mode, как при старте), `INDEX <schema>.<name>` в ADD CONSTRAINT.
  Все файловые тесты сняты с `BlockedByPort` и зелёные.
- [x] 3.6. DDL-хуки: `AddSchemaObject`/`AddDatabaseObject`/`RemoveSchemaObject`
  пишут/удаляют meta-строку; `GetObjectId` в DDL — один раз на команду, как в
  эталоне; `Prepared.IsCacheable()` снова `false` по умолчанию.
- [x] 3.7a. FS-лок (дефолт): `FileChannel.TryLock/ReleaseLock` через
  `FileStream.Lock` на всём файле данных, в `SingleFileStore` и легаси
  `FileStore`; ошибка маппится в `DATABASE_ALREADY_OPEN_1` (90020), совместимо
  с эталонным jar. Тесты: `TestFileSystemLock` (чужой range lock) и
  `FileLockedByThePortIsRefusedByTheReference` (jar получает 90020, пока порт
  держит файл).
- [x] 3.7b. Значения URL, которые порт не умеет, больше не игнорируются:
  `FILE_LOCK=FILE|SOCKET` → `TODOException`, неизвестный метод → 90060,
  `AUTO_SERVER=TRUE` → 50100 (`AUTO_SERVER_PORT` без него — как в эталоне,
  просто настройка). `FILE_LOCK=NO` принимается: проверено jar — MVStore и при
  `NO` держит OS-лок на файле данных (90020), т.е. ведёт себя как FS.
- [ ] 3.7c. Отдельный `.lock.db` (`FileLock`, методы `FILE`/`SOCKET`, heartbeat)
  — отложено.
- [ ] 3.8. `DB_CLOSE_ON_EXIT` (AppDomain.ProcessExit) — не сделано.

## Фаза 4. Builtin- и aggregate-функции

Отдельный план и очередность: [SPRINT_BUILTIN_FUNCTIONS.md](SPRINT_BUILTIN_FUNCTIONS.md).
Batch A (A0, A1, A3, A4) завершён: 696/947 conformance, исходные регрессионные
тесты зелёные. R1–R3 и локальная J применены после отдельного утверждения.
После A4 выполнение остановлено; отчёт и контроль baseline:
[REGRESSION_BATCH_A_INVESTIGATION.md](REGRESSION_BATCH_A_INVESTIGATION.md).

B2 XML выполнен: 702/947 conformance, исходные XML-тесты 6/6, новые XML SQL
37/37; утверждённые решения и XML-diff — в
[XML_BCL_SQL_IMPLEMENTATION.md](XML_BCL_SQL_IMPLEMENTATION.md).
B5 REGEXP выполнен: 705/947 conformance; исходные regex-тесты 3/3, новые
41/41. Pattern и replacement передаются BCL как есть, без трансляторов;
отличия задокументированы в [REGEXP_BCL_IMPLEMENTATION.md](REGEXP_BCL_IMPLEMENTATION.md).
Весь B6 HASH/ORA_HASH отложен по указанию пользователя. Следующий текущий
conformance-пункт — B7 ENCRYPT/DECRYPT, затем B8 форматирование даты/времени.

Отдельно спланирован и отложен [SPRINT_DOTNET_EXTENSIONS.md](SPRINT_DOTNET_EXTENSIONS.md):
DI, .NET UDF/aliases, triggers, trace и необязательные hash-провайдеры.
ORA_HASH будет обычной UDF из этого спринта. Сейчас приоритет — остальные
conformance-задачи, без удаления отложенных тестов.

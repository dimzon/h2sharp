/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Перечень утверждённых отклонений от эталона (H2 2.5.x).
 *
 * Раздел A — утверждённые отклонения. Каждая запись ссылается на основание:
 * принятое проектное решение, замер против эталонного jar или прямое указание
 * пользователя. Код с пометкой из этого раздела — не дефект.
 *
 * Раздел B — кандидаты. Это НЕ утверждения: код с такой пометкой — дефект,
 * а не решение, и ждёт визы пользователя.
 *
 * Универсальное правило (AGENTS.md): не знаешь как — копируй с эталона 1:1.
 * Платформа не позволяет (dotnet vs Java) — кидай TODOException с именем
 * недостающего, а не отвечай заглушкой.
 */

# Перечень утверждённых отклонений от эталона (H2 2.5.x)

## A. Утверждены

### D-001. Синтаксис регулярных выражений — .NET

Для `REGEXP`, `~`, `~*`, `!~`, `!~*` и regex-функций используется
`System.Text.RegularExpressions`, без переноса движка `java.util.regex` и без
перевода Java-шаблонов. Основание: принятое проектное решение (AGENTS.md).

### D-002. Без коллатора

`SET COLLATION <имя>` отказывает (`NotSupportedException`). Замеры против
эталона в `MIGRATION_STATUS.md`: веса .NET не воспроизводят CLDR root.
Пересмотр: ORDER BY с локалью, персистентная зависимость, чтение данных Java-H2.

### D-003. Только встроенный режим, без сети

Движок выполняется в процессе приложения; TCP/remote вне скоупа.
Файловое хранение **в скоупе** и реализовано: открытие/закрытие `.mv.db`, формат
эталона в обе стороны, словарь схемы (`SYS`). Не портированы только lock-файл
(`DATABASE_ALREADY_OPEN_1`) и `DB_CLOSE_ON_EXIT`.

### D-004. Ролей/пользователей нет

Имитируются дефолты эталона, проверенные на пустой in-memory БД (H2 2.5.259):
`CURRENT_USER`/`SESSION_USER`/`SYSTEM_USER` — имя соединения (по умолчанию ""),
`CURRENT_ROLE` — константа `'PUBLIC'` (`PublicRoleName`), `checkAdmin` — no-op.
`SET PASSWORD` / `SET SALT` — отказ `TODOException` (нужны AlterUser и
пользователи). Пересмотр — только вместе с пользователями.

### D-005. Работы с CSV нет

`CSVREAD`/`CSVWRITE`/`FILE_READ`/`FILE_WRITE` — вызов бросает `TODOException`
с именем недостающего (CSV-писатель/читатель, LOB-хранилище). Пересмотр —
только вместе с реализацией.

### D-006. Фасад Dialect и расширение видимости ValueLob

У `Data/Dialect.cs` нет аналога в `org.h2`; `ValueLob.LobOctetLength`,
`ValueLob.LobCharLength` и `FormatLobDataComment` расширены с protected до
internal. Задокументировано в шапке файла и в MIGRATION_STATUS.md.

### D-007. `CURRENT_ROLE` — константа `'PUBLIC'`

Замер T14 на пустой БД; пересмотреть при появлении ролей.

### D-008. `CANCEL_SESSION` отказывает, а не отвечает TRUE

Эталон отменяет выполняемую команду сессии. Порт выполняет команды
синхронно и текущей команды не отслеживает, поэтому `CANCEL_SESSION` для
найденной сессии вызывает `SessionLocal.Cancel()`, который отказывает
`TODOException`. Утверждено пользователем (P-002 = вариант «вызывать Cancel»).
Пересмотр — при появлении отмены команд.

### D-009. `DATABASE_PATH` делегирует базе

`SysInfoFunction` спрашивает `Database.GetDatabasePath()`, как эталон.
Для in-memory ответ `null`; для persistent — `FileUtils.ToRealPath`, как в
эталоне (проверено `DatabasePathMatchesTheReference`).

### D-010. `MEMORY_FREE` / `MEMORY_USED` не отвечают

Эталон читает `Runtime.freeMemory()`/`totalMemory()`. Платформа не
предоставляет семантику кучи JVM (`GC.GetGCMemoryInfo` отвечает на другой
вопрос), поэтому ветви бросают `TODOException`; тесты — в `BlockedByPort`.
Утверждено пользователем (P-003).

### D-011. Словарь схемы: таблица как отдельный вход, пропуск USER-строк

В эталоне таблица — `DbObject`, поэтому у `addMeta`/`populateRowFromDBObject`
один вход. В порте `Tables.Table` не `DbObject`, поэтому добавлены
`Database.AddMeta(SessionLocal, Table)` и `MetaRecord.PopulateRowFromTable` с той
же строкой (ID/HEAD/TYPE/SQL, тип `TABLE_OR_VIEW`). Строки `USER`
(в файле эталона всегда есть встроенный пользователь) парсер порта не читает:
пользователей нет (D-004). Эталон при старте без listener тоже не прерывает
открытие на ошибке строки, а пишет её в trace; trace в порте нет, поэтому
пропуск не оставляет сообщения. `LockMeta`/`UnlockMeta` — no-op: менеджер
блокировок не портирован. Утверждено пользователем.

## B. Кандидаты (НЕ утверждено — код с такой пометкой остаётся дефектом)

### P-007. `ROWNUM` в DML — текущей команды нет

Поля `currentSelect`/`currentPrepared` заведены, и `ROWNUM` читает
`aggregateSelect ?? currentSelect ?? currentPrepared`, как
`getCurrentPreparedOrSelect`. Остаток: parse-only слой DML (`INSERT ... VALUES`,
`UPDATE ... SET`) строит запись команды без `Prepared`, поэтому `ROWNUM`
там бросает `TODOException` с именем недостающего. В эталоне это работает.
Дописать, когда parse-only слой будет создавать команду до чтения выражений.

### P-008. `DbSettings` (таблица h2.*-свойств) не портирована

`INFORMATION_SCHEMA.SETTINGS` показывает только именованные настройки.
Открыто: реализовывать таблицу или фиксировать отсутствие.

## C. Дословные копии, вопросов не требуют

* `ESTIMATED_ENVELOPE` и пропущенные kinds (`DOMAIN`, `CONSTANT`, `ROUTINE`,
  `SYNONYM`, `TRIGGER`, `ROLE`, `USER`) отвечают `NULL` — ровно то, что
  эталон отвечает при пустом поиске. Дописать ветви, когда появятся сами
  объекты (spatial-индексы, домены и т.д.); пометка стоит в коде.
* `GetDiskSpaceUsed`: `Index`/`Table` → 0, `MVTable` — сумма/primary,
  `MVIndex` — корень мапы (как в эталоне).
* `readIfWords` — пик со сдвигом, кавычки не совпадают (как `testToken`).
* `SupportsPrecision` для `TRUNCATE_VALUE` — `{NUMERIC, DECFLOAT, ARRAY}`.

# Спринт: builtin- и aggregate-функции (SQL conformance)

План утверждён. **Batch A (A0, A1, A3, A4), B2 XML и B5 REGEXP выполнены**.
Текущий conformance — **760/952**. DDL/grammar: sequence options 15/15 и
CREATE/ALTER/DROP DOMAIN перенесены; Domain-набор 26/26, новый полный
lifecycle доменного столбца 5/5; ALTER COLUMN 19/19 и JVM lifecycle/error/metadata
сверка 11/11. Смена типа копирует таблицу, индексы и ограничения по эталону,
включая файловый block-merge и переоткрытие. Весь B6 HASH/ORA_HASH отложен; B7
ENCRYPT/DECRYPT и HASH вынесены в опциональный `H2Sharp.Crypto` (D-022).
После A4 остановка для обсуждения соблюдена; XML выполнен по отдельным
утверждённым решениям D-012—D-018 и конкретному XML-diff тестов.

## 0. Правила

- Источник истины — эталон H2 2.5.259 (`reference/h2` + `h2-2.5.259-SNAPSHOT.jar`).
  Каждое поведение подтверждается запуском jar, а не памятью.
- Порт 1:1 из `org.h2`. Платформенные замены — только через
  `docs/APPROVED_DEVIATIONS.md` с визой пользователя.
- Тест, расходящийся с jar, — дефект теста. Правится только с выводом jar
  (hex/значения) в обосновании.
- Непортированное — `TODOException` с именем недостающего, но **в той точке, где
  эталон ветвится**, а не через свой список имён.
- **JSON — только на существующей инфраструктуре порта.** `System.Text.Json`
  (`Utf8JsonWriter`, `JsonDocument`/`JsonElement`) — принятая основа; нельзя её
  убирать, обходить или заменять. Нельзя писать свой JSON-парсер/писатель/
  эскейпер и нельзя вводить третий путь кодирования. Существующие файлы
  (`JsonStjTarget`, `H2JavaScriptEncoder`, `JsonConstructorUtils`, `ValueJson`,
  `JsonByteArrayTarget`) не переписываются. `JsonByteArrayTarget` остаётся как
  есть: он намеренно не построен на `JsonStjTarget` из-за канонических байтов и
  непарных суррогатов (см. `MIGRATION_STATUS.md`). Любое изменение
  JSON-инфраструктуры — только если jar-проба докажет расхождение, и только с
  отдельным подтверждением.
- `BlockedByPort` (12 тестов) не входит в этот спринт: коллатор ×2,
  `SET PASSWORD`/`SET SALT`, CSV/FILE ×3, `MEMORY_FREE`/`MEMORY_USED`,
  `DB_OBJECT_SQL`/`DATA_TYPE_SQL`, `ESTIMATED_ENVELOPE`.

## 1. Точка отсчёта

Коммит `8e78f98`: полный набор 947 тестов — **673 прошло, 274 упало**
(12 из них `BlockedByPort`); зелёный прогон `Category!=BlockedByPort` —
673/935, 262 падения. Документ-снимок: `docs/sql-specs.md`.

## 2. Инвентаризация 37 падений

| Группа | Тестов | Эталон (jar) | Тест совпадает |
|---|---:|---|---|
| GCD/LCM + GCD_AGG/LCM_AGG | 6 | `6`, `12`, `6`, `12`, `36`, `6/12`, `36` | да |
| JSON_OBJECT/JSON_ARRAY | 9 | `{"id":1}`, `{}`, `{"a":1}`, `[1,2]`, `[]`, `[1]`, `[{"a":1}]` | да |
| ENVELOPE | 2 | `TRUE`, bbox `POLYGON ((1 2, 1 4, 3 4, 3 2, 1 2))` | да |
| XML | 6 | ` a="1"`, `<a/>\n`, `<a>x</a>\n`, `<!-- x -->\n`, `<![CDATA[x]]>`, `<?xml version="1.0"?>\n`, `x&#xa;y` | **5 из 6 — нет** |
| REGEXP_* | 3 | `HeLLo`, `X`, `TRUE`, `ll`, `l` | да |
| HASH/ORA_HASH | 2 | 32 байта; `5` | да |
| ENCRYPT/DECRYPT | 2 | encrypt ≠ plain; decrypt = 16 байт (`Hello` + 11 NUL) | Decrypt — **нет** |
| FORMATDATETIME/PARSEDATETIME/TO_CHAR | 5 | `2024-05-06`, `2024` | да |
| FILE_READ/FILE_WRITE | 2 | — | `BlockedByPort` (D-005) |

### 2.1 Тесты, чьи ожидания расходятся с jar

Проверено `RAWTOHEX(STRINGTOUTF8(...))` на jar 2.5.259:

- `XMLATTR('a','1')`: тест `a="1"`, эталон ` a="1"` (ведущий пробел).
- `XMLNODE('a')`: тест `<a/>`, эталон `<a/>\n`.
- `XMLNODE('a', NULL, 'x')`: тест `<a>x</a>`, эталон `<a>x</a>\n`.
- `XMLCOMMENT('x')`: тест `<!--x-->`, эталон `<!-- x -->\n`.
- `XMLSTARTDOC()`: тест `<?xml version="1.0" encoding="UTF-8"?>`, эталон
  `<?xml version="1.0"?>\n` (hex `3c3f786d6c2076657273696f6e3d22312e30223f3e0a`).
- `XMLTEXT('x' || CHAR(10) || 'y', TRUE)`: тест `x&#10;y`, эталон `x&#xa;y`.
- `DECRYPT`: тест `"Hello"`, эталон — 16 байт (`Hello` + 11 NUL-байтов
  паддинга).

`XMLCDATA` совпадает с тестом.

## 3. Дефект структуры, который устраняется первым

Порт держит ручной список `Parser.IsAggregateCall` (`Parser.cs:3112`) вместо
эталонного реестра `Aggregate.getAggregateType` (`Aggregate.java:184`) и
проверяет агрегаты до builtin-функций. Эталон (`Parser.java:3753`): реестр →
`readAggregate` → builtins → window → compat → **90022 FUNCTION_NOT_FOUND**.

- Все 58 агрегатных имён эталона реализованы; ошибки «неподдерживаемый
  агрегат» в эталоне нет.
- Неизвестное имя функции/агрегата — 90022 (`FOO(1)`, `FOO_AGG(1)`), проверено
  jar.
- Агрегат не на месте — 90054 `INVALID_USE_OF_AGGREGATE_FUNCTION`
  (`SELECT 1 WHERE SUM(1) > 0`), проверено jar.

Исправление: убрать `IsAggregateCall`, спрашивать реестр, портировать
`readAggregate` по веткам, а нереализованные типы отказывать `TODOException` из
`Aggregate.Optimize`/`CreateAggregateData` (сейчас там есть `GetInternalError` —
заменить).

## 4. Batch A — выполняется первым (0, 1, 3, 4)

### A0. Структура парсера

Файлы: `src/H2Sharp/Command/Parser.cs`,
`src/H2Sharp/Expressions/Aggregate/Aggregate.cs`.

- Удалить `IsAggregateCall` и раннюю ветку в `ReadQueryOr`.
- В `ReadFunction` после `(` — `Aggregate.GetAggregateType(upperName)`; найден →
  `ReadQueryAggregate`, иначе builtin/window/compat, иначе 90022.
- `ReadQueryAggregate`: сигнатура `(select, type, aggregateName)`; ветки по
  эталону: `COUNT(*)`, двухаргументные регрессии, `HISTOGRAM`,
  `LISTAGG`/`GROUP_CONCAT`/`STRING_AGG`, `ARRAY_AGG`,
  `RANK`/`DENSE_RANK`/`PERCENT_RANK`/`CUME_DIST` (пустые скобки → window),
  `PERCENTILE_*`, `MODE`, `JSON_*AGG`, `default` (DISTINCT + один аргумент),
  `FILTER`/`OVER`.
- Непортированные типы — `TODOException` вместо internal error.
- Приёмка: полный conformance не теряет ни одного зелёного (673); window
  `RANK() OVER` зелёные; `FOO(1)` → 90022; `GCD_AGG` доходит до data factory.

### A1. GCD/LCM (6 тестов)

Файлы: `src/H2Sharp/Expressions/Functions/GCDFunction.cs` (черновик уже есть),
`src/H2Sharp/Expressions/Aggregate/AggregateDataGCD.cs`, `Aggregate.cs`,
`Parser.cs`.

- `GCDFunction` — дословный порт `GCDFunction.java`; в парсер —
  `readGCDFunction` (2+ аргумента, как эталон), не общий `readParameters`.
- `AggregateDataGCD` — порт; `GCD_AGG`/`LCM_AGG` в
  `Optimize`/`CreateAggregateData`.
- Приёмка: 6 тестов; значения уже сверены jar.

### A3. JSON (9 тестов)

Файлы (только добавление недостающего из эталона):

- `src/H2Sharp/Expressions/ExpressionWithFlags.cs` — порт интерфейса;
- `src/H2Sharp/Expressions/Format.cs` — порт `Format.java` (96 строк), через
  существующие `ValueJson.FromJson`/`ConvertToJson`;
- `src/H2Sharp/Expressions/Functions/JsonConstructorFunction.cs` — порт
  класса-выражения (155 строк) поверх существующих
  `JsonConstructorUtils`/`ValueJson`/`JsonByteArrayTarget`;
- `Parser.cs` — ветки `JSON_OBJECT`, `JSON_ARRAY` и постфикс `FORMAT JSON`
  строго по `Parser.java`.

Явно не трогаем: `Util/Json/JsonStjTarget.cs`,
`H2JavaScriptEncoder.cs`, `JsonByteArrayTarget.cs`, `JsonBytesSource`,
`JsonStringTarget/Source`, `JsonValueTarget`, `JsonValue`,
`H2ValueMapper`/`Dialect`.

Приёмка: 9 целевых тестов; `JsonValueOracleTests` и `JsonReadingOracleTests`
остаются зелёными; в диффе нет новых JSON-энкодеров/парсеров.

### A4. ENVELOPE (2 теста)

Файлы: `src/H2Sharp/Expressions/Aggregate/AggregateDataEnvelope.cs`,
`Aggregate.cs`.

- Порт `AggregateDataEnvelope.java`; `type = TypeGeometry`.
- Quick-path через spatial index не делаем: spatial-индексов в порте нет,
  эталон в этом случае отвечает `false`.
- Приёмка: 2 теста.

### Ожидание после Batch A

690/947 (257 падений; 245 без `BlockedByPort`).

### СТОП после A4

После A4 **останавливаемся** и обсуждаем дальнейшие действия. Batch B не
начинается без отдельного решения по вопросам из §7.

## 5. Batch B — после обсуждения (2, 5, 6, 7, 8)

- **B2. XML — выполнено (6 тестов):** XMLFunction/SQL-reader перенесены,
  BCL-поведение утверждено D-012—D-018. Пять проверок изменены только после
  отдельной визы точного diff. Итоги: XML 6/6, новые SQL/XML 37/37;
  [XML_BCL_SQL_IMPLEMENTATION.md](XML_BCL_SQL_IMPLEMENTATION.md).
- **B5. Regexp (3 теста):** порт `RegexpFunction.java` (251 строка) на
  `System.Text.RegularExpressions` (D-001). Добавить
  `Mode.RegexpReplaceBackslashReferences` (REGULAR=false; MariaDB/MySQL/Oracle/
  PostgreSQL=true). Oracle-кейсы: flags `i/c/n/m/g`, position/occurrence/group,
  замена `$1`/`\1`, регион.
  **Pattern передаётся System.Text.RegularExpressions как есть**: никакого
  переписывания групп, якорей, escape или иных попыток воспроизвести Java.
  Расхождения документируются. Replacement тоже .NET как есть, утверждено D-019.
  Выполнено: исходные 3/3, новые 41/41; результаты и отличия — в
  [REGEXP_BCL_IMPLEMENTATION.md](REGEXP_BCL_IMPLEMENTATION.md).
- **B6. HASH/ORA_HASH (2 теста):** порт `HashFunction.java` (177 строк).
  SHA-224 нет в BCL; SHA3 — решить: BCL (с проверкой совпадения) или порт
  `org.h2.security.SHA3`. ORA_HASH — SHA-1 + старшие 8 байт big-endian.
  Последнее указание пользователя: **все хэши, включая базовый HASH,
  откладываются** в [SPRINT_DOTNET_EXTENSIONS.md](SPRINT_DOTNET_EXTENSIONS.md).
  Сейчас B6 не выполняется; существующие HASH/ORA_HASH тесты сохраняются.
- **B7. ENCRYPT/DECRYPT (2 теста):** переносятся не в ядро, а в опциональный
  пакет `H2Sharp.Crypto` на общем .NET UDF API (D-022). Сейчас отложено.
- **B8. Дата/время (5 тестов):** `FORMATDATETIME`/`PARSEDATETIME` (307 строк) и
  `TO_CHAR` (1057 строк) — нужен транслятор Java-шаблонов (отклонение) либо
  `BlockedByPort` отдельным этапом.

Ожидание после B2–B7: 703/947; с датой — 708/947.

## 6. Проверка

- На каждый этап: jar-проба (команда и вывод — в отчёте), целевые тесты,
  полный conformance; счёт не должен падать.
- `dotnet test tests/H2SharpSql.Specs/H2SharpSql.Specs.csproj` — полный и
  `--filter "Category!=BlockedByPort"`.
- Документы: этот файл, `docs/sql-specs.md` (счётчики), `MIGRATION_STATUS.md`,
  отклонения — только с визой.

## 7. Вопросы на будущее (Batch B)

- **Q1 (XML/Decrypt):** разрешается править 5 XML-ожиданий и `Decrypt` по jar?
  (hex: `XMLSTARTDOC` = `3c3f786d6c2076657273696f6e3d22312e30223f3e0a`;
  decrypt = 16 байт).
  XML-часть закрыта отдельным утверждением конкретного diff; Decrypt открыт.
- **Q2 (Regexp) — закрыт:** pattern и replacement передаются .NET Regex как есть,
  без перевода replacement по режимам (D-001/D-019). Иные mode-правила SQL
  (Oracle argument count, PostgreSQL g/default-first) берутся из H2 2.5.
- **Q3 (HASH):** BCL-хеши (с проверкой совпадения) или полный порт H2 `SHA3`?
  Закрыто: .NET HashAlgorithm и словарь фабрик; дополнительные primitives идут
  через optional пакеты отложенного extension-спринта, без собственной реализации.
- **Q4 (CRYPT):** порт H2 security AES (рекомендуется) или .NET AES (не будет
  совместим с Java)?
- **Q5 (дата/время):** порт с транслятором сейчас или `BlockedByPort` отдельным
  этапом?
- **Q6:** `SET PASSWORD`/коллатор/CSV/FILE/`MEMORY_*`/`ESTIMATED_ENVELOPE`
  остаются `BlockedByPort` — подтверждается?
- **Q7:** 90054 (агрегат не на месте) и 90022-поведение вне select — чинить в
  этом спринте или отдельно?

## 8. Состояние рабочего дерева на момент утверждения плана

Незакоммичены черновики `GCDFunction.cs`, `AggregateDataGCD.cs`, правки
`Aggregate.cs` и `Parser.cs` (GCD/LCM из ручного списка). В A0/A1 они
перерабатываются: классы сохраняются, парсерная часть заменяется реестром.

## 9. Итог Batch A

- [x] A0: реестр агрегатов вместо IsAggregateCall, эталонные ветки чтения,
  window-делегирование и именованные TODO в неперенесённых реализациях.
- [x] A1: GCD/LCM и GCD_AGG/LCM_AGG; null, типы, NUMERIC, группы и окна
  сверены с jar. Value.getBigInteger перенесён как getBigDecimal().toBigInteger().
- [x] A3: JSON_OBJECT/JSON_ARRAY/FORMAT JSON; существующая JSON-инфраструктура
  и System.Text.Json сохранены. Для Dapper утверждена локальная фикстура с
  отдельным AssemblyLoadContext на тест, без изменения default mapper.
- [x] A4: ENVELOPE, пустые группы, FILTER и окна сверены с jar.
- [x] Три временные TODO-проверки R1–R3 изменены только после утверждения
  точного diff; SQL, данные, имена и Fact сохранены.
- [x] Добавлены 24 JSON-контрактные проверки: string, byte[], object,
  GetValue и Dapper; проверены native byte[] и изоляция default mapper.

Полный conformance: **696/947**, 251 падение (12 BlockedByPort).
Без BlockedByPort: **696/935**, 239 падений. Прирост +23 вместо ожидаемых +17:
ещё шесть проверок закрылись эталонным разбором в A0 (MODE ×3,
LISTAGG ON OVERFLOW ×2, JSON_ARRAYAGG ORDER BY).

Исходные 1268 регрессионных тестов прошли; отдельно 5 новых oracle-тестов и
24 JSON-контрактных проверки. Data 467/467, MvStore 87/87.
Сравнение с замороженным baseline по именам: новых регрессий,
исчезнувших и пропущенных тестов — 0.

Подробности и согласованные изменения:
[REGRESSION_BATCH_A_INVESTIGATION.md](REGRESSION_BATCH_A_INVESTIGATION.md).

**СТОП после A4 соблюдён.** B2 XML затем отдельно согласован и выполнен.
B5 выполнен: pattern и replacement без преобразований утверждены.
Hash, crypt и форматирование дат остаются для последующих пунктов.

## 10. Отложенный extension-спринт

DI, .NET UDF, SQL aliases, triggers, trace/diagnostics, optional NuGet hashes и
ORA_HASH объединены в [SPRINT_DOTNET_EXTENSIONS.md](SPRINT_DOTNET_EXTENSIONS.md).
Вся криптография вынесена в опциональный `H2Sharp.Crypto` (D-022).
По указанию пользователя сначала продолжаем оставшийся conformance, затем
возвращаемся к реализации этого спринта.

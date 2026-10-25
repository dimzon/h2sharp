# Разбор Batch A по протоколу защиты регрессионных тестов

Статус: исследование завершено. После отдельного утверждения применены R1–R3
и локальная J. Итоговый контроль исходных наборов — в §6. Baseline сохранён.

Протокол: [REGRESSION_TEST_PROTOCOL.md](REGRESSION_TEST_PROTOCOL.md).

## 1. Зафиксированная исходная точка

- Порт: `8e78f98f759e6aa58dfa0ceb1756c18a56e087a0`.
- Эталон: `27c51709fef687bc064f23c8dd495b566c69bb93`.
- Jar: `reference/h2/h2/target/h2-2.5.259-SNAPSHOT.jar`.
- SHA-256 jar:
  `fd56d4326bae1f2c718892cdbca494c36dcc1c7b75616b702959f7e62533dbcf`.
- Контрольные суммы 730 исходных файлов тестов и фикстур сохранены.
  Все они совпадают с исходным коммитом; после исследования изменённых — 0.
- Текущие производственные исходники также зафиксированы перед исследованием.
  Изменённых в ходе исследования — 0.

Baseline повторно запущен из отдельного worktree. Использован тот же jar.
Сохранены полные TRX и сравнение по именам тестов.

Каталог артефактов:

```text
C:\Users\User\AppData\Local\Temp\opencode\h2-regression-8e78f98-20261025
```

В нём находятся `manifest.json`, `commands.md`,
`baseline-core.trx`, `current-core.trx`, `baseline-specs.trx`,
`current-specs.trx`, `comparison-core.json`, `comparison-specs.json`,
`probe-baseline.txt`, `probe-current.txt`, `probe-jar.txt`,
`probe-dapper-configuration.txt`, исходники probe-программ и предлагаемые diff.

## 2. Итоги на неизменённых тестах

Это результаты этапа исследования до применения утверждённых R1–R3/J.

| Набор | Baseline | Текущее состояние |
|---|---:|---:|
| Исходные H2Sharp.Tests, `Category!=BlockedByPort` | 1268 passed, 0 failed | 1265 passed, 3 failed |
| Новые oracle-тесты Batch A, отдельно | — | 5 passed |
| SQL conformance, полный набор | 673 passed, 274 failed | 687 passed, 260 failed |

В исходном регрессионном наборе ровно 3 перехода `Passed → Failed`.
В conformance переходов `Passed → Failed` нет, переходов `Failed → Passed` — 14.
Пропавших и пропущенных тестов нет.

Все девять JSON-conformance тестов остаются `Failed → Failed`, но причина
изменилась с `TODOException` при разборе SQL на ошибку Dapper при преобразовании
возвращённого JSON из `byte[]` в `string`. Они не засчитываются как исправленные.

## 3. Три регрессионных падения

Probe для порта вызывает те же неизменённые private setup-помощники, что и
исходные регрессионные тесты. Jar создаёт ту же таблицу:

```sql
CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR);
INSERT INTO T VALUES (3, 'c'), (1, 'b'), (2, 'b'), (4, NULL);
```

Подготовка проверяется отдельно от выполнения: в порте
`SessionLocal.PrepareCommand`, в jar — `SessionLocal.prepareCommand`.
Диагностическое выполнение — `CommandContainer.Query` / `executeQuery`.

### R1. AggregateOutsideSelectWaitsForReadAggregate

Файл: `tests/H2Sharp.Tests/BuiltinFunctionCallTests.cs`.

Точная исходная проверка: `PrepareCommand` должен бросить `TODOException` с
текстом `aggregate calls outside the select list`.

```sql
SELECT ID FROM T WHERE COUNT(*) > 1
```

| Стадия | Baseline | Текущий порт | Jar |
|---|---|---|---|
| Подготовка | TODOException | Успех | Успех |
| Выполнение | Не достигается | 90054 | 90054 |

Jar: `Invalid use of aggregate function "COUNT(*)"`.

Успех подготовки не означает, что недопустимый агрегат начал выполняться:
при выполнении отказ сохраняется с кодом эталона. Исходный тест фиксирует
отказ на стадии подготовки; это конфликт его контракта с эталонным поведением,
который агент самостоятельно менять не вправе.

Предложение R1: сохранить имя теста, SQL и данные; заменить временную проверку
TODO на две проверки: успешная подготовка и обязательный отказ при выполнении
с кодом 90054. Обе стадии сверять с jar.

### R2. GcdAggWaitsForAggregates

Файл: `tests/H2Sharp.Tests/SelectTextTests.cs`.

Точная исходная проверка: `PrepareCommand` должен бросить `TODOException`,
содержащий `GCD_AGG`.

```sql
SELECT GCD_AGG(NAME) FROM T
```

| Стадия | Baseline | Текущий порт | Jar |
|---|---|---|---|
| Подготовка | TODOException | 90008 | 90008 |

`NAME` имеет тип VARCHAR. Эталонный `GCDFunction.checkType` отвергает его как
неподходящий аргумент: `Invalid value "NAME" for parameter "GCD_AGG argument"`.
Текущий порт также отвергает этот запрос; тип исключения изменился с временной
заглушки на ошибку аргумента.

Предложение R2: сохранить имя теста, SQL, данные и стадию проверки; требовать
`H2SharpException` с кодом 90008 и именем `GCD_AGG argument`, сверенным с jar.
Не подменять `NAME` на числовой столбец.

### R3. ListaggOverflowWaitsForTruncate

Файл: `tests/H2Sharp.Tests/SelectTextTests.cs`.

Точная исходная проверка: `PrepareCommand` должен бросить `TODOException`,
содержащий `ON OVERFLOW`.

```sql
SELECT LISTAGG(NAME ON OVERFLOW ERROR) FROM T
```

| Стадия | Baseline | Текущий порт | Jar |
|---|---|---|---|
| Подготовка | TODOException | Успех | Успех |
| Выполнение | Не достигается | Одна строка `b,b,c` | Одна строка `b,b,c` |

`ON OVERFLOW ERROR` — допустимая конструкция. На исходных данных переполнения
нет. Имя теста упоминает truncate, но его SQL содержит ERROR; этот конкретный
сценарий не проверяет фактическое усечение или превышение предела длины.

Предложение R3: сохранить имя, SQL и данные; проверять успешную подготовку,
ровно одну строку с полным значением `b,b,c` и совпадение с jar. Фактическое
переполнение должно проверяться отдельным новым сценарием, а не подменой SQL
этого теста.

## 4. Девять JSON-conformance падений

Файл: `tests/H2SharpSql.Specs/Functions/JsonFunctionTests.cs`.
Во всех тестах точная операция — `connection.QuerySingle<string>(sql)`
из Dapper 2.1.66.

| Тест | Точный SQL | Текст, возвращаемый jar и типизированным getter порта |
|---|---|---|
| JsonObject_WithKeyValue | `SELECT JSON_OBJECT(KEY 'id' VALUE 1)` | `{"id":1}` |
| JsonObject_WithColonForm | `SELECT JSON_OBJECT('id': 1)` | `{"id":1}` |
| JsonObject_AbsentOnNull_DropsTheKey | `SELECT JSON_OBJECT(KEY 'a' VALUE NULL ABSENT ON NULL)` | `{}` |
| JsonObject_WithUniqueKeys | `SELECT JSON_OBJECT(KEY 'a' VALUE 1 WITH UNIQUE KEYS)` | `{"a":1}` |
| JsonObject_WithoutArguments_IsEmpty | `SELECT JSON_OBJECT()` | `{}` |
| JsonArray_WithExpressions | `SELECT JSON_ARRAY(1, 2)` | `[1,2]` |
| JsonArray_WithQuery | `SELECT JSON_ARRAY((SELECT JSON_OBJECT(KEY 'a' VALUE 1) FROM SYSTEM_RANGE(1, 1)) FORMAT JSON)` | `[{"a":1}]` |
| JsonArray_AbsentOnNull_DropsTheElement | `SELECT JSON_ARRAY(1, NULL ABSENT ON NULL)` | `[1]` |
| JsonArray_WithoutArguments_IsEmpty | `SELECT JSON_ARRAY()` | `[]` |

Для каждого SQL проверены все следующие операции, а не только успешное чтение:

| Операция | Текущий порт | Jar |
|---|---|---|
| Метаданные | JSON, System.Byte[] | JSON, [B |
| Нетипизированный getter | GetValue → byte[] | getObject → byte[] |
| Явный текстовый getter | GetString → ожидаемый текст | getString → ожидаемый текст |
| Типизированный getter | GetFieldValue<string> → ожидаемый текст | getObject(..., String.class) → ожидаемый текст |
| Прямое преобразование исходного объекта в строку | InvalidCastException | ClassCastException |

Байты и текст каждого JSON-значения совпадают. Dapper в исходной конфигурации
не обращается к типизированному текстовому getter для этих вызовов, а пытается
преобразовать нетипизированный `byte[]` в `string`. Это приводит к
`DataException`, внутри которой `InvalidCastException`.

У Dapper нет JVM-аналога: его ошибка не объявляется результатом jar.
Jar подтверждает SQL и контракты JDBC-getter; точно такой же вызов Dapper,
как в исходных тестах, отдельно воспроизведён на порте и падает во всех девяти
случаях. Это незакрытый интеграционный контракт, а не основание самовольно
переписать тесты.

### Диагностически проверенное предложение J

Глобальный вариант ниже был предложением на этапе исследования. Пользователь
потребовал изоляцию в рамках теста; глобальный вариант не применялся.
Реализованный локальный вариант описан в §6.

Во временном отдельном процессе проверена явная настройка:

```csharp
Dapper.SqlMapper.AddTypeMap(typeof(string), DbType.String, useGetFieldValue: true);
```

С ней все девять тех же `QuerySingle<string>` возвращают исходные ожидаемые
значения. Нетипизированный результат провайдера остаётся `byte[]`.

Предлагаемый diff J меняет только
`tests/H2SharpSql.Specs/Infrastructure/TestDatabase.cs`: добавляет эту настройку
в точку подключения. Тела девяти тестов, их SQL, тип `string` и ожидаемые значения
сохраняются. JSON-инфраструктура и System.Text.Json не меняются.

Это изменение защищённой общей фикстуры, требующее отдельного утверждения.
Настройка Dapper действует на string-маппинг в процессе, поэтому после её
утверждения потребуется полный повторный прогон и сравнение всех тестов по
именам. Диагностический успех не засчитан в исходный conformance.

## 5. Конкретные diff для рассмотрения

Оба файла находятся в каталоге артефактов из §1 и не применены к репозиторию.

| Предложение | Файл | SHA-256 |
|---|---|---|
| R1–R3, три регрессионных теста и новый общий oracle-помощник; имена тестов и атрибуты Fact сохранены | proposed-regression-tests-v2.patch | `a1a8abd7fa1b6c22105c189deb7e96c5df8b5f1ce6298be9cc327635bfa5a957` |
| J, явная интеграция типизированного чтения Dapper в общей фикстуре | proposed-json-dapper-fixture.patch | `3f5b90842c60f2bf19c87bac29284be72493d3accf8e2704a27f263461e71a07` |

Предложения можно утвердить независимо. До утверждения исходные тесты и
baseline сохраняются. После утверждения применяются только согласованные diff,
выполняются полные проверки, а изменения тестов отделяются от реализации при
коммите по команде пользователя.

В R1–R3 сохранён `[Fact]`, а не заменён на допускающий пропуск
`[JavaOracleFact]`: отсутствие jar приводит к ошибке проверки, а не к скрытому
исключению прежнего регрессионного теста из прогона. Первая версия предложения
с таким изменением атрибутов сохранена только как артефакт; для согласования
предлагается v2 из таблицы.

## 6. Итог после утверждения R1–R3 и локальной J

Пользователь утвердил R1–R3, затем локальную J с явным ограничением:
настройка Dapper действует только в рамках конкретного теста. Дополнительно
согласованы девять замен строки открытия JSON-фикстуры и Compile/Link
нового помощника в проекте conformance.

### Изоляция

`tests/H2Sharp.Tests/DapperJsonFixture.cs` загружает отдельную копию настоящего
Dapper в collectible `AssemblyLoadContext` на каждый экземпляр фикстуры.
Типизированные string/byte[]-маппинги задаются в этой копии; default Dapper не
меняется даже временно. При закрытии фикстуры закрывается её соединение и
выгружается контекст.

Девять JSON-тестов используют локальную фикстуру. Их имена, SQL,
`QuerySingle<string>` и ожидаемые значения сохранены.
Общий `TestDatabase.Open()` не изменён.

### Контрактные проверки

Добавлены 24 проверки в `DapperJsonContractTests.cs`: девять исходных JSON-SQL,
Unicode, JSON null, SQL NULL, изоляция двух одновременно живых фикстур.
Для каждого JSON-значения проверяются:

| Операция | Контракт |
|---|---|
| GetFieldType | typeof(byte[]) |
| GetFieldValue<string> | Точный JSON-текст |
| GetFieldValue<byte[]> | Точные UTF-8 байты |
| GetFieldValue<object> | Фактически byte[], точные UTF-8 байты |
| GetValue | Фактически byte[], точные UTF-8 байты |
| Локальный Dapper QuerySingle<string> | Точный JSON-текст |
| Локальный Dapper QuerySingle<byte[]> | Точные UTF-8 байты |
| Локальный Dapper ExecuteScalar<object> | Фактически byte[], точные UTF-8 байты |

Getter-операции сверены с jar, включая типизированные String/byte[] и
нетипизированный JDBC getObject. Также проверено, что изменение полученных
массивов не меняет сохранённое JSON-значение.

SQL NULL остаётся `DBNull` при GetValue/GetFieldValue<object>, а Dapper
преобразует его в CLR null при скалярном чтении. JSON null остаётся байтами
`null`, а не DBNull.

Общий Dapper сохраняет исходное поведение: байтовое и объектное скалярное
чтение JSON возвращает byte[], а обычный QuerySingle<string> без явной
типизированной настройки отказывает. Это проверяется во время жизни локальных
фикстур и после их закрытия; default-таблица типов Dapper остаётся той же.
Существующая интеграция System.Text.Json и производственный ADO.NET mapping
не изменялись.

### Контроль замороженного baseline

Проверены 730 исходных файлов: изменены только четыре явно согласованных
файла — два файла с R1–R3, JSON-тесты (локальное подключение фикстуры) и
csproj (Compile/Link). Изменения точно совпадают с утверждёнными правками.
Неутверждённых изменений тестов/фикстур — 0.

Артефакты `approved-test-changes.json`, `approved-scoped-j.patch`,
`final-*.trx`, `comparison-final-*.json` добавлены в каталог из §1;
исходный manifest и baseline-TRX не заменены.
SHA-256 локальной J:
`0867b1f7d317c143f01f59b571e54943161a0d708d5c83b985af87a5f898ac8c`.

| Набор | Baseline | После утверждённых изменений |
|---|---:|---:|
| Исходные H2Sharp.Tests | 1268 passed | 1268 passed |
| Новые oracle-тесты Batch A | — | 5 passed |
| Новые JSON/Dapper-контрактные проверки | — | 24 passed |
| H2Sharp.Data.Tests | 467 passed | 467 passed |
| H2Sharp.MvStore.Tests | 87 passed | 87 passed |
| Полный conformance | 673 passed, 274 failed | 696 passed, 251 failed |
| Conformance без BlockedByPort | 673 passed, 262 failed | 696 passed, 239 failed |

Сравнение по именам: `Passed → Failed` — 0, исчезнувших/пропущенных тестов — 0.
В conformance `Failed → Passed` — 23. Полный H2Sharp.Tests — 1297/1297 с
учётом 29 новых проверок. Оставшиеся conformance-падения не скрыты фильтрами
или новыми трейтами. После A4 выполнение остановлено для обсуждения Batch B.

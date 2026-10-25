# B5: SQL REGEXP на BCL без преобразования pattern/replacement

Перенесены `RegexpFunction` и три ветви `Parser.readBuiltinFunctionIf` из
H2 2.5.259-SNAPSHOT. Pattern передаётся `new Regex(regexp, flags)` как получен
из SQL; replacement — в `Regex.Replace`/`Match.Result` как получен. Группы,
якоря, escapes и replacement не переписываются. Решения: D-001 и D-019.

Структура класса, порядок вычисления аргументов и NULL, arity, возвращаемые
BOOLEAN/VARCHAR и constant folding соответствуют той же версии Java.
`MakeRegexpFlags` возвращает RegexOptions вместо Java int-флагов Pattern:
`i`/`c` применяются по порядку, `n` — Singleline, `m` — Multiline. `g` принимается
только в PostgreSQL REGEXP_REPLACE. Сопоставление CultureInvariant, без inline
вставок в pattern. Uppercase-константы эталона сохранены, чтобы не столкнуться
с PascalCase имён regexpReplace/regexpSubstr.

## Проверки и результаты

- Исходные три regex-conformance теста — **3/3**, без изменения файла,
  SQL, данных, getter или ожидаемых значений. Все шесть исходных SQL сверены jar.
- Новые regex-тесты — **41/41**, включая BCL-специфичные balancing groups,
  class subtraction, числовую named group, replacement и локализацию слеша.
- Общая часть проверена построчно в Regular, Oracle, PostgreSQL, MySQL и MariaDB.
- Полный core-прогон: **1374 passed / 3 failed / 0 skipped**, total 1377
  (40 новых regex-проверок). После него добавлена одна локализующая проверка;
  весь regex-класс из 41 проверки прогнан отдельно и прошёл.
- Полный conformance: **705 passed / 242 failed / 0 skipped**, total 947.
- Сравнение с XML-этапом по именам: все прежние 1337 core-исходов сохранены;
  ровно три regex-теста conformance изменили Failed→Passed, регрессий,
  пропавших и пропущенных проверок — **0**.
- 273 защищённых ненативных файла: только ранее утверждённый XML-diff,
  новых изменений старых тестов/фикстур — **0**.

Три старых core-падения `PostfixCastOracleTests` и 242 прежних conformance
падения остаются в результатах. Новый зелёный regex-набор их не компенсирует.

## Локализация обратного слеша

Один input `ab` и pattern `(a)` сначала проверены с обычным replacement `Z`,
затем с одним слешем и цифрой `1`. SQL ` '\1' ` и `E'\\1'` декодируются в
одинаковые два символа и на JVM, и в порте.

| Replacement | BCL во всех режимах | JVM Regular | JVM Oracle/PostgreSQL |
|---|---|---|---|
| `Z` | `Zb` | `Zb` | `Zb` |
| `\1` (обычная/E-строка) | `\1b` | `1b` | `ab` |

Это не ошибка декодирования SQL-литерала: это явно принятые различные
replacement-диалекты D-019. В .NET `\1` — буквальный текст, `$1` — группа.

Первый новый тестовый запуск останавливался на неподдержанном URL `;MODE=`.
Для проверок используется принимаемый обеими базами `SET MODE`, без изменения
SQL проверяемых функций. При этом найден дефект SessionLocal.GetMode:
он читал фиксированный cast-provider. Метод перенесён из Java дословно как
`GetDatabase().GetMode()`, поэтому теперь видит реально установленный режим.
Полный core-прогон подтвердил отсутствие потерь старых исходов после исправления.

## Явные отличия BCL/JVM

| Операция | JVM H2 2.5 | .NET-порт |
|---|---|---|
| PostgreSQL replace `(a)` replacement `$1` в `ab` | `$1b` | `ab` |
| Oracle replace `a` в `ababa`, position=3, occurrence=0 | `XbXbX` | `abXbX` |
| Oracle replace `^a` в `ababa`, position=3, occurrence=1 | `abXba` | `ababa` |
| SUBSTR `x`, pattern `(a)?x`, group=1 (не участвовала) | 50000 | SQL NULL |

Все четыре пары проверены на jar и порте с явными ожиданиями. Java
Matcher.replaceAll сбрасывает region, BCL Replace использует startAt; Java
region имеет собственную границу для якоря, BCL ищет по исходной строке.
Шаблон ради воспроизведения этих различий не изменяется. Группа без участия
остаётся NULL вместо внутренней ошибки JVM.

Ошибочные .NET-шаблоны дают SQL 22025, неизвестные SQL-флаги — 90008,
ошибочное число аргументов — 7001. Java-only `\Q...\E` не переводится и
отвергается BCL. У Oracle 3..6 аргументов replacement, у остальных режимов
3..4; PostgreSQL без `g` заменяет первое совпадение, с `g` — все.

## Доказательства

TRX и сравнение по именам хранятся в:

```text
C:\Users\User\AppData\Local\Temp\opencode\h2-xml-bcl-prototype-20261025\xml-test-proposal
```

Файлы: `regex-original.trx`, `regex-complete.trx`, `regex-localized.trx`,
`regex-core-full.trx`, `regex-specs-full.trx`, `regex-regression-comparison.json`.
Сравнение: `python tools/XmlTestProposal/report_results.py regex`.
Защита источников: `python tools/XmlTestProposal/scoped_baseline.py verify-approved-xml`.

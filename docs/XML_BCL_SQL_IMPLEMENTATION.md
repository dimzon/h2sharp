# SQL XML: перенос H2 2.5 и утверждённый BCL-контракт

Реализованы `XMLATTR`, `XMLNODE`, `XMLCOMMENT`, `XMLCDATA`, `XMLSTARTDOC`,
`XMLTEXT`. Эталон — `reference/h2`, H2 2.5.259-SNAPSHOT; jar SHA-256:
`fd56d4326bae1f2c718892cdbca494c36dcc1c7b75616b702959f7e62533dbcf`.

## Структура переноса

- `src/H2Sharp/Expressions/Functions/XMLFunction.cs`: поля, конструктор,
  ветви вычисления, порядок NULL-проверок, число аргументов, тип VARCHAR,
  constant folding и `GetName` перенесены из той же версии Java.
- `Parser.readBuiltinFunctionIf` использует эталонные шесть ветвей и
  `readParameters`. Его generic-интерфейс уже есть в порте; select остаётся
  явным параметром существующего C# expression reader.
- XML-конструирование — System.Xml по отдельно утверждённым D-012—D-018.
  Адаптеры BCL — локальные функции, не новые члены интерфейса H2.
- Константы оставлены в эталонном uppercase: PascalCase `XMLNODE` столкнулся бы
  с эталонным методом `xmlNode`. Имена прочих членов следуют обычному PascalCase.
- BCL-ошибки XML явным образом отображаются в SQL 90008 (`InvalidValue2`).
  Это платформенная адаптация; Java StringUtils не валидирует такие входы.
  Ошибки числа аргументов остаются эталонными 7001; ошибки преобразования Value
  не перехватываются внутри XML-адаптера.
- StringUtils.Xml* сохраняет существующее эталонное поведение: его отдельные
  JVM-oracle тесты не заменяются новым SQL-контрактом.

## Отдельно утверждённый diff исходных XML-тестов

Пользователь явно выбрал **«Утверждаю XML-diff»** после показа пяти замен и
полного помощника. Применены только:

- `tests/H2SharpSql.Specs/Functions/XmlFunctionTests.cs`: пять проверок в
  четырёх методах; все восемь SQL, `QuerySingle<string>`, имена шести тестов,
  `[Fact]`, входные данные и `TestDatabase.Open()` сохранены;
- новый `tests/H2SharpSql.Specs/Infrastructure/XmlFunctionAssert.cs`.

Помощник проверяет имена, namespace, данные, число и типы узлов. Для
`escapeNewline=TRUE` проверяется не только декодированный LF, но и отсутствие
буквальных CR/LF в строке. Проверки XMLCOMMENT, обычной CDATA и обычного XMLTEXT
сохранены без замены.

Утверждённый patch SHA-256:
`a1728ce32cd4942c5ab17f83c61e592f35a86538d436f8773f6cd8d164cf3957`.
Его artifact и checksum-ожидания неизменяемы; исходный baseline не переписан.
Перед утверждением пять новых проверок прошли на точных SQL jar и BCL;
18 отрицательных примеров обнаруживали потерю значения, namespace, узла,
пробелов, экранированного LF и декларации.

## Результаты

| Набор | Результат |
|---|---|
| Исходные XML-conformance тесты до переноса | 0 passed / 6 failed / 0 skipped: XMLFunction TODO |
| После переноса, до утверждённого diff | 2 passed / 4 failed: старые форматные/декларационные ожидания |
| Исходные XML-тесты после утверждённого diff | **6/6 passed**, SQL и имена сохранены |
| Новые SQL/ADO.NET/Dapper/oracle проверки | **37/37 passed** |
| Движок против утверждённого BCL-макета | **131/131**, точные строки/NULL/коды ошибок |
| Полный H2Sharp.Tests | 1334 passed / 3 failed / 0 skipped, total 1337 |
| Сравнение с прежними 1297 core-проверками | все 1297 сохранили Passed; регрессий/пропавших — **0** |
| Полный SQL-conformance | **702 passed / 245 failed / 0 skipped**, total 947 |
| Сравнение SQL-conformance с 696/947 | ровно шесть XML-переходов Failed→Passed; регрессий/пропавших — **0** |
| Checksum существующих ненативных тестов/фикстур | 273 файла; только точная разрешённая XML-правка, неожиданностей — **0** |

Три незакрытых core-падения — ранее добавленный WIP `PostfixCastOracleTests`
(коммит `1eea6e4`), не изменение XML:

- `ExistingConversionGapsAlsoAffectPostfixCasts`;
- `PostgreSqlDisallowedTypesAreNotPortedYet`;
- `SchemaObjectLookupIsNotCaseSensitiveYet`.

Сами проверки сохранены, не исключались фильтром из полного прогона и не
компенсировались новыми зелёными тестами. В conformance остаются 245 прежних
падений; по именам их исходы не ухудшились.

## Воспроизведение и доказательства

Все новые TRX и JSON-отчёты:

```text
C:\Users\User\AppData\Local\Temp\opencode\h2-xml-bcl-prototype-20261025\xml-test-proposal
```

Основные файлы: `xml-baseline.trx`, `xml-current-original.trx`,
`xml-approved-original.trx`, `xml-core-regression.trx`, `xml-specs-full.trx`,
`xml-engine-vs-prototype.json`, `xml-regression-comparison.json`,
`approved-xml-test-changes.json`, `non-native-test-checksums.json`,
`proposed-xml-tests.patch`, `xml-proposal-evidence.json`, `xml-exact-input.tsv`.

Команды:

```powershell
dotnet build src/H2Sharp/H2Sharp.csproj --no-restore
dotnet test tests/H2Sharp.Tests/H2Sharp.Tests.csproj --no-restore
dotnet test tests/H2SharpSql.Specs/H2SharpSql.Specs.csproj --no-restore
python tools/XmlTestProposal/scoped_baseline.py verify-approved-xml
python tools/XmlTestProposal/report_results.py
```

Сборка .NET 8 и .NET 10 — без ошибок и предупреждений. Изменения не коммитились:
для этого требуется отдельная команда; утверждённые тестовые изменения при
коммите будут отделены от реализации по регрессионному протоколу.

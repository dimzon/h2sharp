# MIGRATION STATUS — рабочая памятка

Памятка для себя. Не документация продукта; читать при возобновлении работы.

## Правило работы

1. **Сначала эталон, потом код.** Если поведение можно проверить — проверяй на JVM
   (`tests/H2Sharp.Tests/Oracle/JavaOracle.cs`). Не рассуждать «по спецификации».
2. **Транслитерация для буквы, оракл для смысла.** Структуру и имена — 1:1 из Java.
   Числа, форматы, кодовые точки — сверять с эталоном.
3. **Таблицы генерировать, не переписывать.** `tools/*.py` + сверка поимённо.
4. Порт считается готовым, когда есть тест, который бы упал при расхождении.

## Где что

| Что | Путь |
|---|---|
| Контракт порта | `docs/PORTING.md` |
| Фазы и статус по классам | `docs/ROADMAP.md` |
| Установленные факты о поведении | `docs/notes/*.md` |
| Пайплайн переноса таблиц | `tools/port_errorcode.py`, `tools/port_dbexception_kind.py` |
| Эталонный H2 | `reference/h2` (git-ignored, shallow clone) |
| H2 jar для оракула | `reference/h2/h2/target/h2-*.jar` |

## Состояние сборки

* .NET 8/9/10 SDK, SDK закреплён в `global.json` (10.0.401).
* JDK 25 + Maven 3.9.16 доступны → оракул работает.
* Сборка: `dotnet build H2Sharp.sln -c Release` — 0 warnings, 0 errors.
* Тесты: **233 passed, 0 FAILED** (из них 24 группы — дифференциальные против JVM,
  13 — проверки аллокаций).

## Известные расхождения (осознанные, не чинить)

Единственное известное расхождение поведения — `startsWithIgnoringCase`:

| Что | Java (CLDR) | .NET (BCL) |
|---|---|---|
| `Ａ`/`ａ` (полноширинные) | **разные** на PRIMARY | `IgnoreCase` сворачивает → равны |
| `а`/`А` (кириллица), en-US/root | **разные** на PRIMARY | сворачивает → равны |
| `а`/`А`, ru-RU | равные (tailoring) | равные — совпадает |
| `a`/`A`, `e`/`E` | равные | равные |
| `café`/`cafe` | равные | равные с `IgnoreNonSpace` |

Вывод: PRIMARY в ICU — это **не** «игнорировать регистр». В CLDR root регистровые
пары кириллицы и полноширинных форм имеют *разные* первичные веса, а тайлоринг
локали меняет ответ (`ru-RU` отличается от `en-US`/root). Проверено напрямую:
`Collator.getInstance()` даёт `RuleBasedCollator`; ASCII `a`/`A` → 0, а
`а`/`А` → 1 (root, en-US, tr-TR) и 0 (ru-RU).

Поэтому порт следует `CultureInfo.CurrentCulture` (а не инвариантной культуре) с
`IgnoreCase | IgnoreNonSpace`, а расхождение зафиксировано явно в
`StartsWithIgnoringCase_KnownCollatorDivergences`, а не спрятано. Дифференциальный
тест гоняет **обе локали** (`-Duser.language=ru/en` + pinned `CurrentCulture`).

Остальное — 0 расхождений на 233 тестах.
## Производительность и GC pressure

`tests/H2Sharp.Tests/StringUtilsAllocationTests.cs` — 13 тестов на
`GC.GetAllocatedBytesForCurrentThread`. Границы диапазоном, а не равенством: цель —
«ровно эта аллокация и больше ничего», без привязки к заголовку объекта.

Найдено и исправлено (замеры до → после, байт на вызов):

| Метод | Было | Стало | Что сделано |
|---|---|---|---|
| `Hex.BytesToHex(byte[])` | 32816 | 16408 | `string.Create` напрямую в результат; был `StringBuilder` + его `char[]` |
| `Hex.AppendBytesHex` | посимвольно | пачками по 64 | stackalloc-буфер + один `Append(span)` |
| `ToUpperEnglish`/`ToLowerEnglish` | аллокация всегда | 0 | если отображение регистра — тождество, возвращается исходный инстанс (иначе `.ToUpperInvariant()` всегда новый string) |
| `JavaDecode` | 17656 | 16428 | (а) ранний выход, если нет `\`; (б) ёмкость = длина − число escape'ов |
| `JavaEncode` | 25920 | ~9400 | подсчёт точной длины одним проходом + батчинг ASCII-прогонов |
| `Pad` | 2096 | 1048 | заполнение точного буфера вместо `AppendCodePoint` в цикле |
| `ArrayCombine` | 4464 | 1536 | точный буфер; `string.Create` для длинных списков |
| `ArraySplit` | ~700 | 472 | подсчёт разделителей → прямой `string[]`, без `List` + `ToArray` |
| `UrlEncode` | ~500 | 320 | подсчёт точной длины, без эвристики ёмкости |

Общие принципы, которые дальше надо соблюдать:
* Не увеличивать ёмкость StringBuilder «на глаз»: escape'ы и non-ASCII её ломают,
  и лишняя ёмкость платится дважды (чанк + итоговый string).
* Возвращать исходный инстанс, если результат равен входу (строки иммутабельны).
* Считать проходом, а не `List<T>` + `ToArray()`.

Найденный при этом баг (закреплён `ArraySplit_CountsOnlyUnescapedSeparators`):
предподсчёт разделителей обязан зеркалить разбор, включая порядок проверок и
пропуск экранированного символа — иначе массив длиннее нужного.
## Установленные факты (не выводить заново!)

Оформлены в `docs/ROADMAP.md` («Verified behaviour») и `docs/notes/`.

1. UUID: H2 пишет канонический текстовый порядок байт. `Guid.ToByteArray()` нельзя.
2. `ErrorCode`: `NULL_NOT_ALLOWED` = 23502 (не 90022), `DUPLICATE_KEY_1` = 23505.
3. `appendHex(cp, 2)` → **4** hex-цифры (`\0009` для таба).
4. `sortTopN` → n **наименьших** в `[from, to)`; остальное недетерминировано (pivot случайный).
5. `quote` экранирует только `Character.getType` ∈ {0} ∪ [12..18], кроме пробела.
   Эмодзи литерален; PUA → `\e000`; `\+xxx` только для кодовых точек > BMP.
6. `MessageFormat`: одиночный апостроф исчезает (`a'b` → `ab`). Внутренняя незакрытая
   `{` → пустой вывод; внешняя незакрытая `{` → ошибка. Экрана `{{` нет.
7. Каталог сообщений: перевод **приклеивается перед** английским текстом.
8. `Integer.decode`: `0x`/`#` — hex, ведущий `0` — octal, пробелы запрещены.
9. **Java использует ПОЛНОЕ отображение регистра, .NET — ПРОСТОЕ.** `ß`.ToUpper → `SS`,
   `İ`.ToLower → `i` + U+0307, `ı`.ToUpper → `I`. Таблица расхождений генерируется
   в `src/H2Sharp/Util/CaseFolding.cs` (`tools/DumpCaseTable.java` +
   `tools/gen_case_folding.py`), 390 верхних и 288 нижних записей.
10. `URLEncoder` пишет hex **заглавными** и заменяет непарный суррогат на `?`,
    а не на U+FFFD.
11. В `Dictionary`/`collection initializer` нельзя ставить `;` — только запятые
    (иначе CS1003).

## Принятые отклонения от 1:1

| Java | Решение | Почему |
|---|---|---|
| `DbException` (обёртка над `SQLException`) | Тип-обёртка убран | JDBC не умеет менять SQL после создания; у `H2SharpException` свойство изменяемое |
| 10 подклассов `JdbcSQL*Exception` | `SqlExceptionKind` | В .NET нет такой иерархии, и она не помогала бы потребителям |
| `AtomicReferenceArray` в кэшах | обычный массив | .NET не даёт передать элемент массива по `ref`; для кэша безвредно |
| `ByteArrayOutputStream` | `MemoryStream` | — |
| `java.nio.ByteBuffer` | `Span<byte>` + свой курсор | Фаза 2 |
| `synchronized` / `ReentrantLock` | `lock` / `SemaphoreSlim` | — |
| `Integer.decode` в свойствах | свой `TryParseInt` | Нет в .NET |
| `Java system properties` (`-Dh2.x`) | **переменные окружения** `h2.x` | Нет аналога в .NET; записано в PORTING.md |

## Следующие шаги (по порядку)

1. **`Command/ParserUtil.cs`** (694 строки констант токенов) + **`Command/Token.cs`**
   (чистые константы). Генерировать по образцу `tools/port_errorcode.py`.
2. **`Util/IOUtils.cs`** — зависит от `mvstore/DataUtils.convertToIOException`,
   поэтому это уже фаза 2.
3. **Фаза 2 — MVStore.** Гейт: файл, записанный H2Sharp, открывается эталонным H2
   и наоборот. LZF портировать дословно.

Фаза 1 (Util/Engine/Api/Message/StringUtils) закрыта: 233 теста, 0 расхождений.

## Подводные камни, на которые уже наступали

* Оракул не должен обрезать вывод: `OutputLines` больше **не** делает `Trim()` —
  иначе теряются конечные пробелы в сообщениях. Обе стороны префиксуют результат
  знаком `=`, иначе пустой результат исчезает как пустая строка.
* Строки в оракул передаются кодами UTF-16 в hex (`ToCodeUnitsHex`). UTF-8 и
  UTF-16-байты ломают одиночные суррогаты (превращаются в U+FFFD).
* null нельзя передать в оракул — используется маркер `NullMarker`
  (`"\u0000<NULL>"`), на стороне Java `nullable(raw)`.
* Локаль влияет на каталог сообщений: JVM берёт `Locale.getDefault()`, .NET —
  `CurrentUICulture`. В тестах JVM запускается с `-Duser.language=en`.
* `ErrorMessages.BuildCatalogue(language)` сделан отдельно от статического поля,
  чтобы тесты не зависели от культуры процесса.
* В числовых параметрах оракула (`nums`) индексы надо передавать явно: Java не имеет
  `String.slice`, а коллекции-инициализаторы требуют запятых.
* `Enum`-подобные константы H2 через `to_pascal` дают `IoException1`, а не
  `IOException1`: `IO` → `Io`.
* **Не редактируй тестовые файлы регулярками.** Несколько раз ломалось
  частичное применение замен. Правь целиком через write/edit и проверяй сборкой.
* В Java `StringBuilder.append(s, start, end)` — это **(start, end)**, а в .NET
  `Append(string, startIndex, count)` — это **(start, count)**. Портировать надо
  `end - start`. Ошибка выглядит как `ArgumentOutOfRangeException` в самом неожиданном
  месте; на ней поймались `ReplaceAll`, `AddAsterisk`, `Indent`, `TrimSubString`.
* `decodeUnicodeStringSQL` в Java читает следующий code point **не двигая** `i`, и
  только ветка «удвоенного escape» двигает. digits парсятся от `i`, поэтому
  `s.charAt(i)` проверяется на `+`. Сдвинул индекс «для аккуратности» — получил 6 из 15
  расхождений.
* Восьмеричный escape (`\101`) парсится как **base 8**, не как десятичный:
  `\101` → `'A'`. `int.TryParse` без radix даёт `'e'` (101).
* `Collator` в JDK 25 — это `RuleBasedCollator` (CLDR), а не старый `RuleBasedCollator`
  из ICU4J; локаль по умолчанию берётся из категории FORMAT. На этой машине `ru_RU` —
  и это меняет ответы `startsWithIgnoringCase`.
* `output.WriteLine` в xUnit виден только при `--logger "console;verbosity=detailed"`.

## Что НЕ портируется

`fulltext`, `security`/encryption, `server` (TCP + веб-консоль), `tools`, `jmx`,
`jdbc`, `jdbcx`, `org.h2.bnf` (мёртвый код). Причины — в PORTING.md.
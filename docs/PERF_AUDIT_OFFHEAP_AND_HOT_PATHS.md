# Аудит производительности, раунд 2: off-heap, hot paths, SIMD, Frozen

Дата: 2026-11-05. Метод: четыре параллельных аудита по подсистемам (storage/MVStore,
парсер, исполнение запросов, значения/утилиты) плюс построчная перепроверка каждой
цитируемой строки по текущему коду этой сессии. Изменений в код не внесено — это
перечень находок и порядок их развёртывания.

Область: `src/**` (1206 файлов), target'ы `net8.0;net10.0`. Эталон: `reference/h2`
(H2 v2.5.x).

Преемственность: [PERF_AUDIT_ALLOCATIONS.md](PERF_AUDIT_ALLOCATIONS.md) от 2026-11-04.
Ниже не повторяется его §1–5 в закрытой части; статус его пунктов — в §0. Off-heap-направление
в том аудите отсутствовало: `NativeMemory`/`CoTaskMemAlloc`/`Marshal.AllocHGlobal` во всём
`src` — ноль вхождений (проверено grep).

Все номера строк в документе проверены по коду на момент составления.

---

## 0. Статус предыдущего аудита

Закрыто коммитами (не предлагать повторно):

| Пункт docs | Что сделано | Где видно сейчас |
|---|---|---|
| 1.1 closure в `TransactionMap` | `UseSnapshot(Func…)` убран, CAS-цикл отдаёт кортеж | `MvStore/Tx/TransactionMap.cs:484` (`ReadSnapshotRoot()`) |
| 1.3 боксинг энумератора | `Row.HasSameValues` без бокса | коммит `cbcbee1` |
| 2.1 substring перед парсом | `BigDecimal.Parse(TrimForParse(span), …)` | `Value/ValueStringBase.cs:184` |
| 3.1 таблица Unicode на символ | `SearchValues<char> AsciiIdParts` | `Command/Tokenizer.cs:51-52` |
| 3.5 casing на каждый символ | сворачивание ASCII одним битом | коммит `025e90b` |
| 2.7 LOB-блоки в SCRIPT | арендованные буферы + span-котировка | коммит `00f9ae1` |
| 4 «связанный дефект» | `ClrAggregateRegistration` строится при регистрации | `Expressions/Aggregate/AggregateRegistry.cs:83-87` |
| 1.6 (частично) | токен — один struct вместо класса на лексему | коммит `a08c674`, см. §2.10 ниже |

Открыто из прежнего аудита (объём не пересматривался): **2.3** (`Array.Copy` — 69 мест),
**2.4** (`Stream.ReadByte`/`WriteByte` — 10 мест), **2.6** (`Data.CopyString`,
`Store/Data.cs:235`), **3.2** (`StringUtils.IsNumber`, `Util/StringUtils.cs:2362`),
**3.3** (`CompareLike.ContainsIgnoreCase`), **4** (заморозка реестров, см. §4), **5** (`ThrowIfNull`
уже не в счёт: проверено, часть мест уже переведена).

---

## 1. Off-Heap: тяжёлое managed-удержание

### 1.1. WriteBuffer и образ чанка — единственный реальный LOH движка

**Компонент:** `MvStore/WriteBuffer.cs:56` (`new byte[initialSize]`, initialSize = 1 МБ),
`Grow()` `WriteBuffer.cs:357` (новый LOH-массив + `BlockCopy`); образ чанка —
`volatile byte[] Chunk.Buffer` от 1 МБ. Собственный пул: `MvStore/FileStore.cs:368`
(`Get/ReleaseWriteBuffer`, переиспользование до 4 МБ).

**Проблема и влияние:** массивы >85 КБ попадают в LOH; обнуление 1 МБ на каждую аллокацию
(порт writes инициализированным нулём), LOH-дефрагментация на autosave-цикле. Пул снимает
churn, но сам держит LOH-память и **не учитывается в `GetMemory()`-бухгалтерии MVStore**,
то есть лимит кэша недооценивает реальное удержание.

**Решение (.NET 8/10):** шов уже существует — `MvStore/RentedBuffer.cs` (readonly struct,
наружу отдаёт `Memory<byte>`/`Span<byte>`, `Dispose` возвращает аренду). Hot-пути через него
идут уже сейчас: `FileStore.ReadPage` (`FileStore.cs:1864-1911`), `Chunk.ReadBufferForPage`
(`Chunk.cs:617-670`), `DataUtils.ReadFully/WriteFully`. Позиционный ввод-вывод —
`RandomAccess.Read(stream.SafeFileHandle, span, offset)` (`Store/FileSystem/DiskFilePath.cs:468-473`),
он принимает native-`Span<byte>` без переделки вызывающих.

```csharp
// было
public static RentedBuffer Rent(int length) {
    byte[] array = ArrayPool<byte>.Shared.Rent(length);
    return new(new Memory<byte>(array, 0, length), array);
}

// стало — контракт тот же, аллокатор выбирает политика
internal sealed unsafe class NativeBuffer : MemoryManager<byte> {
    private readonly void* ptr;
    private readonly int length;
    private NativeBuffer(int length) { this.length = length; ptr = NativeMemory.Alloc((nuint)length); }
    public override Memory<byte> Memory => CreateMemory(length);
    public override Span<byte> GetSpan() => new(ptr, length);
    public override MemoryHandle Pin(int elementIndex = 0) => new((byte*)ptr + elementIndex);
    public override void Unpin() { }
    protected override void Dispose(bool disposing) { if (ptr is not null) NativeMemory.Free(ptr); }
}
// Rent: ArrayPool для length < 85 КБ (бакет пула не рождает LOH-трафик),
// NativeBuffer — для >= порога; байты аренды прибавляются к учётной памяти кэша.
```

Виза пользователя обязательна: меняется модель учёта памяти (см. §5 и правило AGENTS.md
«предложить .NET-идиому и спросить»), запись в `docs/APPROVED_DEVIATIONS.md`.

### 1.2. LOB — проектировать native-арену до проводки блочного хранилища

**Компонент:** `Values/Lobs/LobData.cs:68-98` (`LobDataInMemory` держит один `byte[] small`),
`Values/ValueLob.cs:156-164` (`GetBytes()` возвращает `[.. GetSmall()]`, то есть полную копию),
`Values/ValueLob.cs:168-169` (`GetBytesNoCopy()`). Блочного хранилища в порту ещё нет:
`MvStore/Db/LobStorageMap.cs` и `MvStore/StreamStore.cs` не подключены (`new StreamStore(` —
ноль ссылок), запись db-LOB даёт `TODOException` (`MvStore/Db/ValueDataType.cs:603`, `:627`).
План — [LOB_STORE_PLAN.md](LOB_STORE_PLAN.md).

**Проблема и влияние:** по плану блоки LOB — 256 КБ, то есть гарантированный LOH, удерживаемый
пока живо значение. Это будет самое тяжёлое удержание после страниц. Мигрировать с `byte[][]`
на native после подключения дороже, чем начать сразу.

**Решение:** блоки LOB — естественный кандидат в unmanaged arena: размер известен заранее,
жизненный цикл = жизнь значения, содержимое не интерпретируется внутри GC-графа. Арена —
`NativeBuffer` из §1.1 плюс счётчик удержанных байтов, участвующий в лимите памяти.

### 1.3. Что выносить нельзя (и почему это не дефект)

**Кэш страниц.** `MvStore/Cache/CacheLongKeyLIRS.cs` хранит не `byte[]`, а граф объектов
`Page`; ключ `long` живёт в поле `Entry.key`,Dictionary нет, боксинга нет (проверено).
Буфер чтения страницы ≤ `Engine/Constants.cs:209` `DefaultPageSize = 16 * 1024` — в LOH не
попадает и уже арендуется через `RentedBuffer`. Перенос payload'а страниц в unmanaged =
переопределение модели памяти MVStore и `Page.GetMemory()` → отступление от эталона.

**Строки результата.** `Result/LocalResult.cs:70` `private List<Value?[]> rows` и `Value` как
reference type — контракт `org.h2` (см. docs §6 прежнего аудита). Единственный эталонный путь
разгрузки — spill во временное хранилище, он реализован (`LocalResult.cs:604-614`,
`MVTempResult`).

---

## 2. Горячие пути: аллокации

### 2.1. `H2DataReader.GetBytes` — полная копия всего LOB на каждое оконное чтение. Критический

**Компонент:** `Data/H2DataReader.cs:436-443` (`cell.GetBytes()` → `CopySlice`), источник копии —
`Values/ValueLob.cs:156-164`.

**Проблема и влияние:** канонический ADO.NET-цикл `while ((read = GetBytes(...)) > 0)` с буфером
100 КБ на BLOB в 10 МБ даёт 100 полных материализаций по 10 МБ ≈ 1 ГБ LOH-мусора на одно поле.
Метод `GetBytesNoCopy()` уже есть (`ValueLob.cs:168`) и уже применяется в
`MvStore/Db/ValueDataType.cs:410`, `:426`, `:434`.

**Решение:** no-copy + `Span`.

```csharp
// было
&& cell.GetBytes() is byte[] bytes)
    return CopySlice(bytes, fieldOffset, buffer, bufferOffset, length);

// стало
&& cell.GetBytesNoCopy() is byte[] bytes)
    return CopySlice(bytes.AsSpan(), fieldOffset, buffer, bufferOffset, length);
```

Правка безопасна ровно постольку, поскольку `CopySlice` не изменяет источник — проверить до
внесения. То же касается `Data/H2DataReader.cs:391` (ветка UUID, копия на 16 байт).

### 2.2. `ValueBlob.CharLength` и `ValueClob.GetString` — полный декод ради длины. Высокий

**Компонент:** `Values/ValueBlob.cs:172` (`CharLength()` → `IOUtils.StreamEncoding.GetString(...).Length`,
строка около :181), `Values/ValueBlob.cs:81` + `:88` (тот же декод в `GetString()`),
`Values/ValueClob.cs:79` (декод на каждый `GetString`) и `Values/ValueClob.cs:162`
(два декода на сравнение).

**Проблема и влияние:** строка материализуется целиком, чтобы узнать число символов; >85 КБ
уходит в LOH. Горячо через ADO.NET (`GetChars`, `GetString`, сравнения, `CHAR_LENGTH`).

**Решение (.NET 8/10):** подсчёт по байтам без декода + кэш расшифрованного текста.

```csharp
// было
p = IOUtils.StreamEncoding.GetString(inMemory.GetSmall()).Length;

// стало — 0 аллокаций, тот же ответ, включая суррогатные пары
int units = Utf8.CodeUnitCount(span); // (b & 0xC0) != 0x80  плюс  (b & 0xF8) == 0xF0
LobCharLength = p = units;
```

> **Ловушка, обязательная к запоминанию.** Наивный lead-byte-count даёт число **кодовых точек**,
> а `string.Length` и Java `String.length()` считают **единицы UTF-16**. Расхождение на
> astral-символах: 2 против 1. Правильная форма — `nonContinuation + highSurrogates`, и тест
> с U+10000 обязан входить в гейт.

Для `ValueClob` — кэш `string? text` в `LobDataInMemory` (строка неизменяема, наблюдаемый ответ
не меняется), сравнение в `ValueClob.cs:162` переводится на один декод на значение.

### 2.3. Боксинг счётчиков дублей во внешнем результате. Высокий (порт хуже эталона)

**Компонент:** `MvStore/Db/MVSortedTempResult.cs:46` (`MVMap<object, object> map`), строки
`:242`, `:249`, `:252` (`map.PutIfAbsent(key, 1L)`, `map.Put(key, (long)old + 1L)`).

**Проблема и влияние:** в Java те же строки дают `Long.valueOf`, который интернирует −128..127 —
ноль аллокаций. В .NET бокс каждый раз: ~24 Б на строку при `DISTINCT`/`ORDER BY` с spill
(частота — на каждую строку, попавшую во внешнее хранилище, `LocalResult.cs:590-608`).
Паритета нет, это реальное расхождение с эталоном.

**Решение:** зеркало JVM `LongCache`.

```csharp
private static readonly object[] SmallCounts = BuildSmallCounts(); // 0..127
private static object Box(long n) => (ulong)n <= 127 ? SmallCounts[n] : n;

if (map.PutIfAbsent(key, Box(1)) is null) { rowCount++; }
...
map.Put(key, Box((long)old + 1L));
```

Наблюдаемость сохраняется: десериализация читает значение через `LongDataType`.

### 2.4. Компаратор, собираемый на каждую строку. Высокий (оконные функции, перцентили)

**Компонент:** `Expressions/Analysis/WindowFrame.cs:200` и `:237` —
`rows.BinarySearch(row, Comparer<Value?[]>.Create(sort!.Compare))`;
`Result/SortOrder.cs:434` `GetRowValueComparator()` каждый вызов возвращает новое замыкание
(проверено: телеметрия lambda создаёт делегат + closure на вызов).

**Проблема и влияние:** до двух объектов на строку-partition для
`SUM … RANGE BETWEEN n PRECEDING`; вызывается внутри бинарного поиска, то есть и в цикле
по строкам. Эталон (`WindowFrame.java:676`, `:756`) передаёт `sortOrder` напрямую — ноль
аллокаций. В порту уже есть канонический образец кэширования компаратора:
`Result/SortOrder.cs:93` (`private Comparison<Value?[]>? comparison`).

**Решение:**

```csharp
// на SortOrder
private Comparer<Value?[]>? rowComparer;
internal Comparer<Value?[]> RowComparer => rowComparer ??= Comparer<Value?[]>.Create(Compare);

// WindowFrame.cs:200, :237
index = rows.BinarySearch(row, sort!.RowComparer);

private Comparison<Value>? rowValueComparator;
public Comparison<Value> GetRowValueComparator() => rowValueComparator ??= CompareRowValues;
```

### 2.5. Visitor на каждую несовпавшую строку outer join. Средний

**Компонент:** `Tables/TableFilter.cs:133` — `nestedJoin.Visit(new NullRowVisitor())` внутри
`SetNullRow()`. Класс `TableFilter.cs:140` полей не имеет (проверено).

**Проблема и влияние:** аллокация на каждую строку LEFT JOIN без пары; `SetNullRow` вызывается
рекурсивно по цепочке join'ов.

**Решение:** `private static readonly NullRowVisitor NullRowVisitorInstance = new();`
Java здесь аллоцирует анонимный класс — .NET может быть лучше эталона без изменения поведения.

### 2.6. Заголовок и подвал чанка: `StringBuilder → ToString → GetBytes`, четыре раза на сохранение. Средний

**Компонент:** `MvStore/Chunk.cs:451-478` — `Encoding.Latin1.GetBytes(buff.ToString())`;
`MvStore/Chunk.cs:485-501` — `GetBytes` **дважды** (`:491` ради Fletcher, `:500` для результата).
Вызовы: `Chunk.cs:296/298` `EstimateHeaderSize()` (откуда `FileStore.cs:1285`) и `Chunk.cs:322`
`WriteChunkHeader`, плюс `FileStore.cs:1373` (`Put(c.GetFooterBytes())`).

**Проблема и влияние:** на каждый чанк при сохранении текст собирается и сериализуется
повторно: ~4 StringBuilder + 4 строки + 4 byte[]; при autosave-цикле с тысячами чанков и в
очереди записи это заметный gen0-шум, а `EstimateHeaderSize` вычисляет длину, которую затем
считают заново.

**Решение:** латин-1 байты в `Span<byte>` известной длины, Fletcher по тому же буферу,
оценка длины — арифметикой по фиксированным полям. Побайтовый вывод не меняется.

```csharp
Span<byte> footer = stackalloc byte[FooterLength];
int w = TryFormatFooter(footer, checksum: 0);
int fletcher = DataUtils.GetFletcher32(footer[..w]);
_ = TryFormatFooter(footer, fletcher);
buff.Put(footer);
```

### 2.7. Парсер: аллокации на каждое имя. Средний (раз на prepare, но на каждое слово)

**Компонент и место:**
* `Command/Parser.cs:3070` — `switch (name.ToUpperInvariant())` в `ReadTypedLiteral`, вызывается
  на каждый идентификатор-терм (`Parser.cs:6328`); строка + culture-fold, в ~99 % случаев
  терм не типизированный литерал. То же `Parser.cs:3017` (`ReadDataChangeDeltaTable`, холодно).
* `Command/Parser.DataTypes.cs:113` — `string original = originalCase.ToUpperInvariant();`
  на каждое упоминание типа и каждый `CAST`; дальше по ней два ординальных поиска
  (`DisallowedTypes.Contains` :247, `DataType.GetTypeByName` :252).
* `Command/Parser.cs:5358` — `private bool ReadIfWords(params string[] words)`, 36 точек вызова
  (`NOT NULL`, `PRIMARY KEY`, `ON UPDATE` на каждую колонку `CREATE TABLE`
  `Parser.Columns.cs:694`, `:726`, `:553`): для reference-type `params` компилятор каждый раз
  материализует `new string[N]`.

**Решение:** сравнение без копии (pattern уже живёт в парсере — `Parser.cs:1101-1104` сравнивает
`OrdinalIgnoreCase`) и `ReadOnlySpan<string>` + статические массивы.

```csharp
// было
switch (name.ToUpperInvariant()) { case "DATE": ... }
private bool ReadIfWords(params string[] words)

// стало
if (name.Equals("DATE", StringComparison.OrdinalIgnoreCase)) ...
private bool ReadIfWords(ReadOnlySpan<string> words)
private static readonly string[] NotNullWords = ["NOT", "NULL"];
```

> `params ReadOnlySpan<string>` с литералами не поможет: `RuntimeHelpers.CreateSpan` для
> reference types тоже аллоцирует.

### 2.8. `Token`: комментарий обещает overlay, кода нет. Средний

**Компонент:** `Command/Token.Body.cs:51-76` — поля `kind, start, type, text, quoted, unicode,
index, number, bytes, value` идут последовательно (~56 Б); `[StructLayout]` во всём
`src/H2Sharp/Command/` отсутствует (проверено grep), хотя шапка файла (:13-15) утверждает
«the payload fields overlay each other». Плюс `List<Token>` без ёмкости
(`Command/Tokenizer.cs:125`) — рост удвоением на 40-80 лексем.

**Решение:** явный `LayoutKind.Explicit` с union'ом взаимоисключающих payload'ов
(`number | index | bytes | value | text`) → ~32 Б на токен; мутации write-back не трогать.
Это доделка уже визируемого отклонения 1.6, а не новое отклонение. Ёмкость списка —
эвристикой (`sql.Length / 8`), с обоснованием в коде.

### 2.9. `new Regex` на каждую проверенную строку. Высокий по CPU — требует визы

**Компонент:** `Expressions/Functions/RegexpFunction.cs:48` (`REGEXP_LIKE` в WHERE — на каждую
строку), `:93` (`REGEXP_REPLACE`), `:123` (`REGEXP_SUBSTR`).

**Честная рамка:** эталон тоже компилирует шаблон на вызов (`RegexpFunction.java:71`, `:146`,
`:183`; сессионный кэш `getRegExp` подключён только к операторам `REGEXP`/`~`). То есть это
**паритет, а не расхождение** — улучшение поверх эталона.

**Решение:** пропустить три места через уже легализованный в порту кэш —
`Expressions/Conditions/CompareLike.cs:65-66` (`SmallLRUCache<(bool IgnoreCase, string Pattern), Regex>`,
`GetCompiledRegex` :547). Шаблон SQL передаётся как есть (D-001/D-019 не нарушаются),
поведение на невалидном шаблоне сохраняется. Запись в APPROVED_DEVIATIONS.

### 2.10. Эксклюзивный `lock` на каждый hit кэша страниц. Отклонено ранее

**Компонент:** `MvStore/Cache/CacheLongKeyLIRS.cs:199` (`Get(long key)`) →
`Segment.Get(Entry?)` `:624-643` — всё чтение значения и `Access(e)` под `lock (Sync)`; в
эталоне `get()` шёл под readLock.

**Почему не предлагается:** прежний аудит (§6: «`CacheLongKeyLIRS.cs:626` и контракт в шапке
файла — эталонная модель памяти; замена лога меняет семантику видимости») отклонил это место,
и возражение остаётся в силе: обход стека LIRS мутирует порядок, поэтому «быстрый путь без
блокировки» меняет момент, когда запись считается употреблённой. Фиксировать как известный
contension, не как дефект аллокаций.

---

## 3. SIMD и `SearchValues<T>`

### 3.1. Fletcher-32 — скалярный цикл, **уже закрыто**. Отмечено в коде.

**Компонент:** `MvStore/DataUtils.cs:1145-1172`: `int x = (bytes[i++] << 8) | bytes[i++]`.
Воспроизведение: `GetFletcher32Scalar` — скалярный, векторная версия была попробована и
отклонена: веса до 360*32bit превышают возможности векторных путей без гарантии побайтовой
идентичности для поддерживаемых целей. Комментарий :1147-1152 подтверждает, что скалярный
путь оставлен как контракт. Проверено: `src/H2Sharp/MvStore/DataUtils.cs:1145-1175` — нет
`Vector128`/`Vector256`.

**Статус:** находка закрыта коммитом или предыдущей правкой перед аудитом. В порядке развёртывания
§7 этот пункт удалить.

### 3.2. `SearchValues<char>` — пять мест с ручным посимвольным циклом

| Место | Сейчас | Решение |
|---|---|---|
| `Command/Tokenizer.cs:2167` `SkipSimpleComment` | `for (char c; … c != '\n' && c != '\r'; i++)` | `sql.AsSpan(i, end + 1 - i).IndexOfAny('\r', '\n')` |
| `Command/Tokenizer.cs:2133` `SkipBracketedComment` | пара `*`/`/` посимвольно | `IndexOfAny('*', '/')` + локальная проверка следующего |
| `Command/Tokenizer.cs:1635` `SkipWhitespace` + ветка `default:` главного цикла (`:633`) | `JavaCharacter.IsWhitespace` на символ | ASCII-sweep `IndexOfAnyExcept(SearchValues.Create(" \t\n\v\f\r"))`, на остановке — эталонная проверка |
| `Command/Tokenizer.cs:1682` `Read0xBinaryString` | hex-скан посимвольно | `SearchValues.Create("0123456789abcdefABCDEF")` |
| `Util/StringUtils.cs:564` `QuoteStringSql(StringBuilder, ReadOnlySpan<char>)` | «plain»-проход по каждому литералу (`getSQL`, SCRIPT, trace) | `IndexOfAnyExcept` печатного ASCII + `IndexOfAny('\'', '\\')`, чанковые `Append`; plain-случай — `string.Create` |

Вызовы коммент-скоппинга подтверждены: `Tokenizer.cs:251`, `:278`, `:284`, `:308`, `:1662`,
`:1668` — объём реальный на SCRIPT и на SQL с комментариями.

Плюс открытые пункты прежнего аудита: **3.2** `StringUtils.IsNumber` (`Util/StringUtils.cs:2362`)
и **3.3** `CompareLike.ContainsIgnoreCase`.

> Напоминание из docs §0.1, чтобы не гадать: `SearchValues<string>` не существует ни в .NET 8,
> ни в .NET 10 (проверено компиляцией). Наборы строк — только Frozen, векторизуется только
> поиск по `char`/`byte`.

---

## 4. `FrozenDictionary` / `FrozenSet`: остаток

Уже заморожено и работает (не трогать): `Util/ParserUtil.cs:495` (keywords),
`Value/DataType.cs:186` (types by name), `Expressions/Functions/FunctionRegistry.cs:42`
(OrdinalIgnoreCase), `BuiltinFunctions` (FrozenSet), `Engine/Mode.cs:378`, часть `Engine/Database.cs`.

Открыто:

| Место | Сейчас | Частота чтения | Эффект |
|---|---|---|---|
| `Expressions/Aggregate/AggregateRegistry.cs:78` | `Dictionary` (OrdinalIgnoreCase) | каждое имя агрегата в `ReadFunction` (`Command/Parser.cs:6489-6491`) | Средний |
| `MvStore/Type/MetaType.cs:57` `ClrTypes` + `:36` | `Dictionary<string, Type>` / `<Type, string>` | сериализация JAVA_OBJECT | Средний |
| `Values/CaseFolding.cs:40`, `:439` | `Dictionary<int, string>` | каждый символ с override | Средний (трогать только Unicode-путь) |
| `Engine/Database.cs:120` `schemas` | `Dictionary<string, Schema>` под локом БД | каждый разбор имени схемы | Средний: вторая frozen-копия, пересборка на DDL; модель блокировок не трогать |
| `MvStore/Type/ObjectDataType.cs:104` | `Dictionary<Type, int>` | сравнение массивов | Низкий/Средний |

Форма перехода — `ToFrozenDictionary(comparer)` с сохранением компарера (образец:
`Engine/Mode.cs:378-409`). Не замораживать: пользовательские реестры ADR-001 (`RegisterUdf`,
агрегаты сессии), `Store/FileSystem/MemoryFilePath.cs:29`, `MvStore/SingleFileStore.cs:27`.

---

## 5. Проверено и отброшено (чтобы не предлагать повторно)

* **`ValueStringBase.GetHashCode` не кэширует Java-хэш** — `Value/ValueStringBase.cs:70-96`.
  Это осознанное, измеренное и задокументированное в коде решение: кэш значений хэширует только
  что переданное значение, у которого хэша ещё нет, поэтому поле было бы нулём на каждом вызове
  (+4 Б на значение выигрыша ради). Остаточный вопрос только CPU-характера: `ValueCollectionBase`
  пересчитывает хэш элементов строкового ключа; до измерения — не находка.
* **`SmallLRUCache` «узел на каждую вставку»** — `Util/SmallLRUCache.cs:28`, `:111-129`:
  уже `Dictionary<TKey, LinkedListNode<KeyValuePair<…>>>` + `CollectionsMarshal.GetValueRefOrAddDefault`,
  единственный lookup, узел при перезаписе снимается из порядка. Узел списка — паритет с
  эталонным `LinkedHashMap`, не дефект.
* **Lock-модель `CacheLongKeyLIRS`** — см. §2.10, отклонено прежним аудитом по семантике видимости.
* **`ObjectDataType` боксинг скаляров** — контракт `DataType<Object>` эталона, менять нельзя.
* **`StreamStore`, `LobStorageMap`, `Data.Create`, `ByteArrayDataType`** — в порт не подключены
  (ноль ссылок); их аллокации — будущая работа по LOB_STORE_PLAN, а не текущий дефект. См. §1.2.
* **`Value` как reference type, `new Value?[columnCount]` на строку результата**
  (`Command/Query/Select.cs:1143`, `:1359`, `:1599`) — 1:1 с эталонным `queryFlat`.
* **LINQ в построчных циклах** — греп по `Indexes/`, `Result/`, `Tables/`, `Expressions/`:
  единственное `using System.Linq` — `AggregateDataCollecting.GetArray` (раз на группу). Нарушений нет.

---

## 6. Вне производительства, но найденное попутно

Боевой путь парсера токенизирует через `Tokenizer.TokenizeForTest`
(`Command/Tokenizer.cs:105-112`) с зашитыми `identifiersToUpper: true,
identifiersToLower: false, nonKeywords: null`, вызовы: `Command/Parser.cs:558`, `:594`, `:675`,
`:705`, `:724`, `:1840`, `:8044`, `:8397`. Эталонный `Parser` строит `Tokenizer` из режима БД
(`reference/h2/.../command/Tokenizer.java:155`, поля `identifiersToUpper`/`nonKeywords`).
Риск расхождения с H2 2.5: режимы с LOWER, keyword-as-name. Конструктор с эталонной сигнатурой
в порту есть (`Tokenizer.cs:86-96`) — правка состоит в том, чтобы боевой путь стал его вызывать.
Пункт §2.7 и §3.2 накладывать после, иначе оптимизация закрепится на неверном входном режиме.

---

## 7. Порядок развёртывания и способ проверки

1. Очередь по цене/риск: **2.1 → 2.2 → 2.3 → 2.4 → 2.5 → 3.1 → 2.6 → 2.7 → 2.8 → 3.2 → 4**.
2. Отдельно, с визы пользователя: **2.9** (regex-кэш поверх эталона), **1.1** и **1.2**
   (off-heap: меняется модель учёта памяти → запись в
   [APPROVED_DEVIATIONS.md](APPROVED_DEVIATIONS.md) и в `MIGRATION_STATUS.md`).
3. Каждый фикс — гейт аллокаций в стиле `tests/H2Sharp.Tests/*AllocationTests.cs`
   (`GC.AllocatedBytesForCurrentThread` как граница, а не как равенство) и `JavaOracleFact`-тест
   на совпадение поведения тем же коммитом ([REGRESSION_TEST_PROTOCOL.md](REGRESSION_TEST_PROTOCOL.md) §8),
   со снятием `BlockedByPort` с открывшихся тестов.
4. Обязательные отдельные проверки: §2.2 — astral-символы (U+10000) на длине и сравнении;
   §3.1 — границы окна редукции и нечётный хвост; §2.3 — DISTINCT с spill; §2.6 — побайтовое
   совпадение заголовка/подвала чанка с эталонным файлом; §2.8 — оракул на список токенов.
5. Мерить аллокационные гейты в Debug; профайлер — по правилам AGENTS.md.
6. Бенчмарк-харнеса в решении нет (`BenchmarkDotNet` упоминается только в
   [NATIVE_TESTS_NOT_PORTED.md](NATIVE_TESTS_NOT_PORTED.md)); без него оценки «Высокий/Средний»
   — вывод из структуры кода, а не измерение. Предлагаемый минимум: `tests/H2Sharp.Benchmarks`
   с четырьмя сценариями — tokenize 5k запросов, SELECT 1M VARCHAR-строк, GET BYTES оконно по
   BLOB 10 МБ, `SUM … RANGE PRECEDING` на 100k строк.

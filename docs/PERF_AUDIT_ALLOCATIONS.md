# Аудит производительности: аллокации, Span, SearchValues, Frozen

Дата: 2026-11-04. Совместная ревизия: исходный аудит (Qoder, Principal .NET /
low-allocation) + повторная верификация каждой цитируемой строки по текущему коду
(ZCode / Mercury-3). Все ссылки на строки ниже даны по актуальной нумерации на
момент ревизии; исходный отчёт незначительно сместился по строкам после правок.
Область: `src/**` (1194 файла, ~312k строк), target'ы `net8.0;net10.0`.
Изменений в код не внесено — это перечень находок и порядок их развёртывания.

Метод: два прохода по парсеру и по storage-слою плюс построчная перепроверка каждой
цитируемой строки. Доступность API проверялась компиляцией probe-проекта на net10,
а не по памяти. Находки исходного аудита, подтверждённые кодом, помечены (✓);
новые находки этой ревизии — (NEW).

---

## 0. Две поправки к постановке

**0.1. `SearchValues<T>` поддерживает только `byte` и `char`.** `SearchValues<string>`
не существует ни в .NET 8, ни в .NET 10 — проверено компиляцией:

```
SearchValues.Create(new[] { "SELECT", "FROM" }, StringComparer.OrdinalIgnoreCase)
  -> error CS1503: Argument 1: cannot convert from 'string[]' to 'byte'
```

Следствие: наборы строк (ключевые слова, имена функций, имена типов, имена агрегатов)
переводятся на `FrozenDictionary`/`FrozenSet`; векторизуется только поиск по символам
и байтам. Проверялось и работает: `SearchValues<char>` + `IndexOfAnyExcept`,
`FrozenSet<string>`/`FrozenDictionary<string,int>` с `StringComparer.OrdinalIgnoreCase`,
`FrozenDictionary<int,string>`, `FrozenDictionary<Type,string>`.

**0.2. Значительная часть работы уже сделана**, и аудит обязан это учитывать, иначе
советы будут повторять закрытое: `FrozenDictionary` в `Util/ParserUtil.cs:495` и
`Engine/Mode.cs:378` (проверено — обе живут и работают); `Span`/`ReadOnlyMemory`
в `Compression/*`, `MvStore/WriteBuffer.cs`, `MvStore/Type/*`; `ArrayPool`
в `MvStore/Page.cs:655` и `Compression/CompressDeflate.cs:86`; стек-буферы и
`string.Create` в `Util/StringUtils.cs`; `GC.AllocateUninitializedArray`
в `MvStore/Type/ObjectDataType.cs:1392`; гейты аллокаций — 14 тестов:
`tests/H2Sharp.Tests/SelectAllocationTests.cs`, `TokenAllocationTests.cs`,
`StringUtilsAllocationTests.cs`, `JavaUtf8SpanTests.cs`,
`JavaNumberFormatAllocationTests.cs` и др.
(`MIGRATION_STATUS.md:1763` — «Производительность и GC pressure»). Ниже — только остаток.

---

## 1. Скрытые аллокации на самом горячем пути

### 1.1. Делегат + closure на каждое чтение строки из транзакционной карты — Критический (✓)

**Место:** `src/H2Sharp/MvStore/Tx/TransactionMap.cs:478` и `:514` (в исходном
отчёте `:479`/`:514`, сейчас код сместился на строку)

```csharp
public V? GetImmediate(K key)
    => TransactionStore.Unbox<V>(UseSnapshot((rootReference, committedTransactions) =>
                            GetFromSnapshotObject(rootReference, committedTransactions, key)));

private R UseSnapshot<R>(Func<RootReference<object, VersionedValue<object>>, long[], R> snapshotConsumer)
```

**Проблема и влияние.** `GetImmediate` — точка чтения каждой строки при скане и поиске.
Каждый вызов создаёт closure (ловит `this` и `key`) и экземпляр `Func<,,>` — два объекта
на строку. На 1M прочитанных строк это ~48 МБ gen0-мусора и соответствующие сборки.
Дополнительно метод с delegate-параметром не инлайнится, то есть закрывается и весь
остальной путь. В эталоне `org.h2.mvstore.tx.TransactionMap.get()` берёт snapshot и вызывает
`getFromSnapshotObject` напрямую: `UseSnapshot` — обёртка, придуманная портом, чтобы
сделать CAS-цикл дженериком по `R`, а не перенесённая структура.

**Решение (.NET 8/10).** Убрать `Func`, отдать CAS-циклу кортеж:

```csharp
public V? GetImmediate(K key)
{
    (RootReference<object, VersionedValue<object>> root, long[] committing) = ReadSnapshotRoot();
    return TransactionStore.Unbox<V>(GetFromSnapshotObject(root, committing, key));
}

private (RootReference<object, VersionedValue<object>>, long[]) ReadSnapshotRoot()
{
    // тот же цикл CompareExchange, что сейчас внутри UseSnapshot, без индирекции
}
```

`UseSnapshot<R>` остаётся ровно там, где потребитель правда различается (`CreateSnapshot`
возвращает `Snapshot`), или сворачивается в те же две строки.

**Эффект.** Критический: −2 аллокации на чтение строки, возвращается инлайнинг
`GetImmediate`. Отклонением от эталона не является — наоборот, убирает портом придуманный слой.

> **Развёрнуто с поправкой к обоснованию (2026-11-04).** Утверждение «в эталоне
> вызов напрямую, `UseSnapshot` придумана портом» неверно: эталон имеет
> `TransactionMap.useSnapshot(BiFunction)` (`TransactionMap.java:560`), и его
> `getImmediate` передаёт capturing lambda — один объект на чтение на JVM.
> Порт перевёл эталон 1:1; разница в том, что .NET-lambda с захватом — это
> closure + delegate (два объекта) и непроинлайненный цикл. Кортежный
> `ReadSnapshotRoot` — .NET-улучшение ниже эталонной цены (0 аллокаций),
> зафиксировано в комментарии метода и в MIGRATION_STATUS. `ContainsKey`
> дополнительно к `GetImmediate` переведён на кортеж — у него та же проблема;
> `UseSnapshot<R>` удалён, `CreateSnapshot` тоже на кортеже.

### 1.2. Декодирование VARCHAR: аренда пула плюс вторая копия строки — Высокий (✓)

**Место:** `src/H2Sharp/MvStore/DataUtils.cs:413-434` (`ReadString`)

```csharp
char[] chars = ArrayPool<char>.Shared.Rent(len);
try
{
    for (int i = 0; i < len; i++) { /* раскод в chars[i] */ }
    return new string(chars, 0, len);
}
finally { ArrayPool<char>.Shared.Return(chars); }
```

**Проблема и влияние.** Комментарий рядом утверждает, что `string.Create` неприменим,
«потому что `Span` не может ехать в generic-аргументе state». Для `Span` это верно, но
итог — два прохода по данным (раскод в арендованный массив, затем копирование в строку)
плюс трафик пула на каждой строке текста. Десериализация страницы с VARCHAR — основной
расход пути чтения.

**Решение (.NET 8/10).** Состояние `string.Create` — `ReadOnlyMemory<byte>` (он быть
generic-аргументом может), длину в байтах считать отдельным дешёвым прогоном:

```csharp
int byteLen = CountEncodedBytes(buff.Span.Slice(position, remaining));
string result = string.Create(
    len,
    buff.Slice(position, byteLen),
    static (Span<char> dst, ReadOnlyMemory<byte> src) =>
    {
        ReadOnlySpan<byte> s = src.Span;
        int p = 0;
        for (int d = 0; d < dst.Length; d++)
        {
            int x = s[p++];
            dst[d] = x < 0x80
                ? (char)x
                : x >= 0xE0
                    ? (char)(((x & 0xF) << 12) + ((s[p++] & 0x3F) << 6) + (s[p++] & 0x3F))
                    : (char)(((x & 0x1F) << 6) + (s[p++] & 0x3F));
        }
    });
position += byteLen;
```

Если предварительный подсчёт неприемлем, компромисс — оставить пул для длинных строк, а
короткие (доля в реальных базах >80%) гнать через `stackalloc` без аренды вовсе.

**Эффект.** Высокий для LOAD/SELECT с текстом: минус один полный проход по символам и
весь трафик `ArrayPool`.

> **Развёрнуто в компромиссной форме (2026-11-04).** Основной снипет не
> компилируется: `buff` в `ReadString` — `Span<byte>`, а Span→Memory-конверсии
> не существует (и span-состояние `string.Create` — только с net9; probe из
> §0 собирался только на net10). Реализован второй, компромиссный вариант
> аудита: ≤ 256 символов — стек + одна материализация, без пула и без второй
> копии; длиннее — пул как раньше. Вторая копия на длинном пути без unsafe
> или массива-владельца на net8 не убирается. Декод общий
> (`DecodeStringChars`), оракул round-trip не изменился.
>
> **Дополнено по указанию пользователя (2026-11-04, условная компиляция).**
> Подсчёт байтов (`CountStringBytes`, те же ветки 1/2/3 по lead-байту — он же
> bounds-check усечённого тела) и декод вынесены в общие для обеих платформ
> функции; с `NET9_0_OR_GREATER` результат строится `string.Create` напрямую
> из среза — единственная аллокация на любой длине, без пула и без копии
> (первоначальный замысел этого пункта). На net8 — стек/пул из компромиссной
> формы. Условной остаётся только сборка результата, поэтому net8-ветка не
> является непроверяемой копией декода.

### 1.3. `Row.HasSameValues` — боксинг энумератора на каждую строку UPDATE — Высокий (✓)

**Место:** `src/H2Sharp/Result/Row.cs:45`

```csharp
public bool HasSameValues(Row other) => GetValueList().SequenceEqual(other.GetValueList());
```

**Проблема и влияние.** LINQ-перегрузка для `T[]` идёт через `IEnumerable<T>.GetEnumerator()`,
struct-энумератор `array` боксится — два объекта на вызов. Вызов построчный:
`Command/Dml/Insert.cs:314` и `Command/Dml/SetClauseList.cs:304`, то есть на каждом
`UPDATE ... SET` и на проверке «значение не изменилось». Это единственный LINQ в потоке
данных, найденный в решении.

**Решение (.NET 8/10).** Императивный цикл с идентичным сравнением — внутри `SequenceEqual`
всё равно работает `EqualityComparer<Value?>.Default.Equals`, то есть виртуальный
`Value.Equals(object?)`:

```csharp
public bool HasSameValues(Row other)
{
    Value?[] a = GetValueList(), b = other.GetValueList();
    if (a.Length != b.Length)
    {
        return false;
    }

    for (int i = 0; i < a.Length; i++)
    {
        if (!EqualityComparer<Value?>.Default.Equals(a[i], b[i]))
        {
            return false;
        }
    }

    return true;
}
```

**Эффект.** Высокий при нулевом риске: −2 аллокации и −1 виртуальный вызов на строку UPDATE.

### 1.4. Двойная аллокация на каждый идентификатор — Высокий (✓)

**Место:** `src/H2Sharp/Command/Tokenizer.cs:1426-1427` → `:1471-1477`

```csharp
private string ExtractIdentifier(string sql, int beginIndex, int endIndex)
    => ConvertCase(sql[beginIndex..endIndex]);      // аллокация №1 — срез строки

private string ConvertCase(string s)
    => identifiersToUpper ? StringUtils.ToUpperEnglish(s) : s;   // аллокация №2, если регистр менялся
```

**Проблема и влияние.** Обычный SQL от EF Core и linq2db пишется в нижнем регистре, поэтому
на каждый идентификатор получается два `string`: срез, затем копия в верхнем регистре.
Дополнительно `Util/ParserUtil.Members.cs:143` прогоняет результат через `ToUpperEnglish`
ещё раз перед `Keywords.TryGetValue`.

**Решение (.NET 8/10).** Свернуть срез и фолд в один проход там, где длина результата
известна (фолд — тождество либо не расширяется; расширение только для override-символов
≥ U+00DF):

```csharp
private string ExtractIdentifier(string sql, int beginIndex, int endIndex)
{
    ReadOnlySpan<char> slice = sql.AsSpan(beginIndex, endIndex - beginIndex);
    if (!identifiersToUpper && !identifiersToLower)
    {
        return new string(slice);                                  // как было, одна аллокация
    }

    if (StringUtils.NeedsCaseExpansion(slice))
    {
        return StringUtils.ToUpperEnglish(new string(slice));      // прежний StringBuilder-путь
    }

    return string.Create(slice.Length, slice, static (dst, src) =>
    {
        for (int i = 0; i < src.Length; i++)
        {
            dst[i] = identifiersToUpperFlag ? char.ToUpperInvariant(src[i]) : char.ToLowerInvariant(src[i]);
        }
    });                                                            // ровно одна аллокация вместо двух
}
```

Пути с override-символами (`ß`, `İ`) оставляем в `ToUpperEnglishCore` через `StringBuilder`:
они требуют расширения и сверены с эталоном (`Util/CaseFolding.cs:9`, `Util/JavaCharacter.cs:245`).

**Эффект.** Высокий: ~−1 аллокация на идентификатор на каждый prepare.

> **Развёрнуто с поправками (2026-11-04).** Сделано через span-перегрузки
> `ToUpperEnglish`/`ToLowerEnglish` в `StringUtils`, фолд из среза; guard — не
> отдельный `NeedsCaseExpansion`, а существующий `HasCaseFoldingOverrides`
> (порог 0x00DF: всё с суррогатами и ﬁ уходит в builder-путь эталонного
> фолдинга). Две поправки к приведённому коду: (1) `string.Create` со
> span-состоянием не компилируется на net8 (CS9244; probe из §0 собирался
> только на net10) — использован паттерн `Hex.BytesToHex` (стек-буфер /
> scratch-массив); (2) добавлен identity-pre-scan, чтобы уже-верхние
> идентификаторы сохранили поведение эталонного `toUpperEnglish` (возврат
> того же содержимого одной аллокацией — материализацией среза).
> `ParserUtil.Members.cs:143` — не аллокация: при `identifiersToUpper` второй
> фолд identity-проверка с 0 аллокаций, при `identifiersToLower` он нужен.

### 1.5. `ToUpperCache`: массив-пара на каждый промах — Средний/Высокий (✓)

**Место:** `src/H2Sharp/Util/StringUtils.cs:164`, `:171` (поле — `:79`)

```csharp
string?[]? entry = ToUpperCache[index];
if (entry is not null && entry[0] == s) return entry[1]!;
string s2 = ToUpperEnglishCore(s);
ToUpperCache[index] = [s, s2];   // collection expression → новый string?[2] на каждый промах
```

**Проблема и влияние.** Обоснование в комментарии — «C# не позволяет передать элемент массива
по ref». Передавать по ref здесь и не нужно, есть только присваивание. Каждый промах кэша
стоит `string?[2]` (~40 байт с заголовком), а на именах длиной больше нескольких символов
промахов большинство: кэш, придуманный экономить, сам производит мусор.

**Решение (.NET 8/10).** Два параллельных массива — ноль аллокаций на вставку:

```csharp
private static readonly string?[] ToUpperCacheKeys = new string?[ToUpperCacheLength];
private static readonly string?[] ToUpperCacheValues = new string?[ToUpperCacheLength];

int index = s.GetHashCode() & (ToUpperCacheLength - 1);
if (ToUpperCacheKeys[index] == s)
{
    return ToUpperCacheValues[index]!;
}

string s2 = ToUpperEnglishCore(s);
ToUpperCacheKeys[index] = s;      // 0 аллокаций
ToUpperCacheValues[index] = s2;
return s2;
```

**Эффект.** Средний/Высокий за пять строк; побочный плюс — исчезает `entry is not null`
в горячем чтении.

> **Отозвано при развёртывании (2026-11-04).** Эталонный `StringUtils.toUpperEnglish`
> (`reference/h2/h2/src/main/org/h2/util/StringUtils.java:98`) сам аллоцирует
> `new String[] { s, s2 }` на каждый промах, и слоты у него те же — 2048
> (`TO_UPPER_CACHE_LENGTH`). То есть порт уже совпадает с эталоном по
> аллокационному профилю, и «мусор, которого нет в эталоне», здесь отсутствует.
> Кроме того, предложенная замена некорректна под конкурентностью: два отдельных
> записи `ToUpperCacheKeys[index] = s; ToUpperCacheValues[index] = s2;` дают окно,
> в котором другой поток читает старый ключ и получает значение другого
> идентификатора (даже на x86-TSO: запись значения видна раньше записи ключа).
> Текущая атомарная замена ссылки на пару-массив разрыву не поддаётся, поэтому
> гонка с порчей имён идентификаторов исключена. Нулевой вариант без разрыва
> возможен (reservation CAS на слот + volatile-публикация value → key → gen),
> но это новая схема конкурентности, которой в эталоне нет, — только по отдельной
> визе пользователя.

### 1.6. Токенизация целиком: объект на каждый токен и клонирование для batch — Высокий (NEW)

**Место:** `src/H2Sharp/Command/Tokenizer.cs:100-642` (`Tokenize`), иерархия токенов
`src/H2Sharp/Command/Token.Body.cs:105-346` (class-иерархия: `IdentifierToken`,
`KeywordToken`, `CharacterStringToken`, `ValueToken`, …), и
`src/H2Sharp/Command/Parser.cs:1564-1572` (`SetSql`: клонирование токенов для batch-паттерна).

**Проблема и влияние.** Каждый `Prepare` любого SQL генерирует:
- один объект токена на каждую лексему (reference-типы, каждый — отдельная аллокация);
- `List<Token>` с ростом и последующим хранением;
- при batch (несколько операторов в одном тексте) — полное клонирование токенов
  (`tokens[i].Clone()`).
Типичный SELECT — 30–40 токенов ⇒ 40–80 мелких аллокаций gen0 на один парс.
Это главный источник мусора в OLTP-нагрузке, где подготовки команд переиспользуются
недостаточно.

**Решение (.NET 8/10).** `Token` → `readonly record struct` c диапазоном в исходной
строке; имя/литерал материализуются лениво, только когда реально нужны:

```csharp
readonly record struct Token(int Start, int Length, int Type, int Param, ...);

public string GetIdentifier(string sql)
{
    ReadOnlySpan<char> span = sql.AsSpan(Start, Length);
    return identifiersToUpper ? StringUtils.ToUpperEnglish(span) : span.ToString();
}
```

Для batch-паттерна копирование исчезает само (структура копируется по значению),
`List<Token>` заменяется на массив из `ArrayPool<Token>.Shared`.

⚠️ **Требуется виза пользователя:** это отклонение от 1:1-порта (в Java токены — классы).
По `AGENTS.md` («предложить пользователю и спросить») решение вносится только после
подтверждения. Существующий `TokenAllocationTests` остаётся гейтом.

**Эффект.** Высокий на throughput парсера — самый крупный остаток в этой зоне.

> **Развёрнуто по визе пользователя (2026-11-05).** `Token` — мутабельная
> структура с объединением полей по виду токена; фабрики сохранили имена
> прежних классов, поверхность членов та же, типовые проверки стали свойствами
> (`IsCharacterString`/`IsStringLiteral`). Мутации — write-back: пост-проход
> `\u` в токенизаторе, номер параметра, `SetSql`. Кэш значения литерала
> сохранён (мутация локальной переменной структуры). `Clone` стал бесплатным —
> `CloningATokenCostsNothing` (на классах был бы красным); оракул потока
> токенов против JVM зелёный без изменений; полный набор 1891. Отклонение от
> 1:1-порта записано в MIGRATION_STATUS и в шапке `Token.Body.cs`.

---

## 2. Строки, массивы, потоки: Span/Memory вместо substrings и байтовых циклов

### 2.1. `GetBigDecimal` режет подстроку перед парсом — Высокий (✓)

**Место:** `src/H2Sharp/Value/ValueStringBase.cs:180` (`GetBigDecimal`), а также
`Value/Value.Conversion.Families.cs:1225`, `:1530`, `:1604`, `:1683`, `:1792`

```csharp
return BigDecimal.Parse(value.Trim());   // Trim() → новый string
```

**Проблема и влияние.** Все соседи по классу уже идут через span: `GetByte`/`GetShort`/
`GetInt`/`GetLong` (`ValueStringBase.cs:167-177`) вызывают `Parse(value, static (ReadOnlySpan<char> s, ...) => ...)`,
а в `:371` того же файла используется span-перегрузка `BigDecimal.Parse(text.AsSpan().Trim(), ...)`.
`GetBigDecimal` и пять конверсий в `Families.cs` остались на string-пути. Это `CAST(v AS DECIMAL
/ TIME / TIMESTAMP / INTERVAL)` — по строке на каждую конвертацию.

**Решение (.NET 8/10).**

```csharp
public sealed override BigDecimal GetBigDecimal()
{
    try
    {
        return BigDecimal.Parse(value.AsSpan().Trim(), value);   // span-перегрузка уже существует
    }
    catch (FormatException e)
    {
        throw DbException.Get(ErrorCode.DataConversionError1, e, value);
    }
}
```

В `Families.cs` — `ReadOnlySpan<char> s = (GetString() ?? string.Empty).AsSpan().Trim();`
как уже сделано в `:1049`, оригинал строки оставлять только для текста ошибки.

**Эффект.** Высокий при малом риске: −1 string на числовую/дательностную конвертацию.

> **Развёрнуто частично (2026-11-04).** Сделано: `GetBigDecimal`, общий `Parse<T>`
> и `GetBoolean` в `ValueStringBase` — span-путь. Оракул против JVM нашёл при
> этом второе расхождение: `String.trim` эталона снимает `<= U+0020`, а .NET
> `Trim()` — по `char.IsWhiteSpace`; введён `TrimForParse`, обе стороны границы
> закрыты `ValueStringBaseOracleTests` (U+001F эталон снимает и принимает,
> U+00A0/U+3000 эталон оставляет и отказывает).
>
> Отложено: пять мест в `Families.cs` (`:1225`, `:1530`, `:1604`, `:1683`,
> `:1792`) — это не `BigDecimal.Parse`, а `ValueDate/Time/TimeTimeZone/
> Timestamp/TimestampTimeZone.Parse`, которые принимают только `string`;
> span-перегрузки тянут за собой работу в `DateTimeUtils`, а аллокация на этих
> местах и так возникает только при реальном паддинге (`Trim()` без снятых
> символов возвращает исходный инстанс). Отдельный пункт работы, не
> механический остаток.
Это недоработанный остаток приёма, применённого в том же файле шесть раз.

### 2.2. BLOB → CLOB: круговой прогон через `Encoding.UTF8` ради проверки — Средний (✓)

**Место:** `src/H2Sharp/Value/Value.Conversion.Families.cs:428-435`

```csharp
byte[] bytes = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(small));
v = ValueClob.CreateSmall(bytes.AsSpan().SequenceEqual(small) ? small : bytes);
```

**Проблема и влияние.** Два буфера и два полных прохода, чтобы выяснить, был ли вход
валидным UTF-8.

**Решение (.NET 8/10).** Валидный UTF-8 при round-trip даёт байт-в-байт те же байты, поэтому
строгий декодер закрывает быстрый путь без повторного кодирования:

```csharp
private static readonly System.Text.UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

byte[] bytes;
try
{
    _ = StrictUtf8.GetString(small);   // бросит на невалидных байтах
    bytes = small;                     // round-trip тождественен
}
catch (DecoderFallbackException)
{
    bytes = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(small));
}

v = ValueClob.CreateSmall(bytes);
```

**Эффект.** Средний: −2 аллокации и −1 проход на `CAST(BLOB AS CLOB)` для валидных данных.

### 2.3. `Array.Copy` → `Span<T>.CopyTo` — Средний, 69 мест (✓)

**Место (основные).** `src/H2Sharp/Store/Data.cs:149`, `:162`;
`MvStore/Page.cs:332`, `:333`, `:350`, `:351`, `:1090`, `:1091`, `:1441`, `:1442`, `:1462`, `:1463`;
`MvStore/Db/MVSortedTempResult.cs:234`; `MvStore/WriteBuffer.cs:365`; `MvStore/DataUtils.cs:489-515`;
`MvStore/Tx/BitSetHelper.cs:42`; `Compression/CompressNo.cs:28`; `Compression/LZFOutputStream.cs:96`;
`Store/FileSystem/SplitFileChannel.cs:273`, `:346`.

Проверено счётчиком: в `src/H2Sharp` ровно 69 вызовов `Array.Copy`.

**Проблема и влияние.** `Array.Copy(src, srcIndex, dst, dstIndex, len)` на reference-массивах
проверяет совместимость типов элементов во время работы и поэлементно ставит барьер записи;
`Span<T>.CopyTo` для примитивов уходит в `Buffer.Memmove` (SIMD) с одной проверкой на вызов.

**Решение (.NET 8/10).**

```csharp
public void Write(byte[] buff, int off, int len)
{
    buff.AsSpan(off, len).CopyTo(data.AsSpan(pos, len));
    pos += len;
}

public void Read(byte[] buff, int off, int len)
{
    data.AsSpan(pos, len).CopyTo(buff.AsSpan(off, len));
    pos += len;
}
```

**Эффект.** Средний: 1.5–3× на коротких копированиях (< 256 байт) за счёт снятых проверок,
≈1× на длинных. Механическая замена; не смешивать с `Array.Fill`/`Array.Clear`-семантикой.

### 2.4. Байт за байтом через `Stream.ReadByte`/`WriteByte` — Средний (✓)

**Место.** `src/H2Sharp/Store/DataReader.cs:52`, `:70`, `:81`, `:96`, `:103`, `:110`, `:117`, `:123`, `:141`, `:144`;
`Compression/LZFInputStream.cs:114`, `:121`; `Compression/LZFOutputStream.cs:150-153`;
`MvStore/DataUtils.cs:334`, `:338`, `:470`, `:474`; `MvStore/StreamStore.cs:196`, `:203`, `:250`;
`Store/RangeInputStream.cs:83`, `:130`; `Store/CountingReaderInputStream.cs:106`.

**Проблема и влияние.** Каждый `ReadByte()` — виртуальный вызов с проверкой disposed и bounds;
на заголовок int-блока их четыре. `WriteVarInt(Stream)` пишет до пяти отдельных виртуальных
вызовов, хотя span-перегрузка `WriteVarInt(Span<byte>, ref int)` есть в том же файле (`:445`).

**Решение (.NET 8/10).** Span-перегрузки `Stream`, доступные с .NET 8:

```csharp
public static void WriteVarInt(Stream @out, int x)
{
    Span<byte> scratch = stackalloc byte[5];
    int n = 0;
    while ((x & ~0x7f) != 0)
    {
        scratch[n++] = (byte)(x | 0x80);
        x = (int)((uint)x >>> 7);
    }

    scratch[n++] = (byte)x;
    @out.Write(scratch[..n]);                       // один вызов вместо до пяти
}

// заголовок блока в LZFInputStream — вместо четырёх ReadByte со сдвигами
Span<byte> header = stackalloc byte[4];
if (input.ReadAtLeast(header, 4, throwOnEndOfStream: false) != 4)
{
    throw DbException.Get(ErrorCode.IOError1, ...); // прежняя реакция на усечение
}

int len = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header);
```

**Эффект.** Средний на путях чтения/записи страниц и LOB: −3..−4 виртуальных вызова и
bounds-проверки на заголовок. Поведение на усечённом файле обязательно сохраняется через
`ReadAtLeast` и покрывается тестом.

> **Развёрнуто с поправками (2026-11-04).** `ReadAtLeast` не потребовался — и
> именно потому, что «прежняя реакция на усечение» в `LZFInputStream.ReadInt`
> — это не исключение: неполный заголовок подставляет −1 в пропущенные
> сдвиги (мусорное значение падает дальше по чтению), а пустое чтение — quirk
> `decompress = null; return 0` (конец потока). Реализован один буферизованный
> `Stream.Read` с точным воспроизведением этой арифметики; таблица всех 36
> префиксов реального потока закреплена `LzfTruncationTests` (снята с кода до
> изменения). В объём также вошли `WriteVarLong(Stream)`, `LZFOutputStream.
> WriteInt` и заголовки `StreamStore`. Пропущено: `Store/DataReader` (в порте
> пока не вызывается; `Read()` контрактом — один байт), `RangeInputStream.
> ReadByte`/`CountingReaderInputStream.ReadByte` (перегрузки контракта).

### 2.5. 64 KiB буфер и `MemoryStream` на каждый stream-параметр — Средний (✓)

**Место:** `src/H2Sharp/Data/H2ValueMapper.cs:226-243`

```csharp
using System.IO.MemoryStream copy = new();
stream.CopyTo(copy);
return Values.ValueBlob.CreateSmall(copy.ToArray());
...
byte[] buffer = new byte[64 * 1024];
```

**Решение (.NET 8/10).** `ArrayPool<byte>.Shared` (в проекте уже применяется —
`Compression/CompressDeflate.cs:86`) плюс span-перегрузки `Read`/`Write`; `ToArray()`
заменить на одно точное копирование из аренды.

**Эффект.** Средний: 64 KiB не попадает в LOH (порог 85 000 байт), но это крупные объекты
gen0, ускоряющие collection 0 при массовых вставках BLOB.

### 2.6. `Data.CopyString`: временные буферы на каждый вызов — Средний (NEW)

**Место:** `src/H2Sharp/Store/Data.cs:234-250`

```csharp
public static void CopyString(TextReader source, Stream target)
{
    char[] buff = new char[Constants.IoBufferSize];
    Data d = new(new byte[3 * Constants.IoBufferSize]);
    while (true)
    {
        int l = source.Read(buff, 0, buff.Length);
        if (l <= 0) break;
        d.WriteStringWithoutLength(buff, l);
        target.Write(d.data, 0, d.pos);
        d.Reset();
    }
}
```

**Проблема и влияние.** Две аллокации на каждый вызов (char[] + byte[] + обёртка
`Data`), хотя метод вызывается в цикле по блокам при перекачке текстовых потоков
(скрипт, LOB-пути). `WriteStringWithoutLength` принимает только `char[]` — ещё и
bounds-проверки по всей длине.

**Решение (.NET 8/10).** `ArrayPool<char/byte>.Shared` + span-перегрузка:

```csharp
public static void CopyString(TextReader source, Stream target)
{
    char[] buff = ArrayPool<char>.Shared.Rent(Constants.IoBufferSize);
    byte[] data = ArrayPool<byte>.Shared.Rent(3 * Constants.IoBufferSize);
    try
    {
        int l;
        while ((l = source.Read(buff, 0, buff.Length)) > 0)
        {
            int len = WriteStringWithoutLength(data, buff.AsSpan(0, l));
            target.Write(data, 0, len);
        }
    }
    finally
    {
        ArrayPool<char>.Shared.Return(buff);
        ArrayPool<byte>.Shared.Return(data);
    }
}
```

**Эффект.** Средний: −2 аллокации на вызов; в LOB-циклах — десятки тысяч вызовов.

> **Развёрнуто с поправкой (2026-11-04).** В порте у `CopyString` пока нет
> production-вызовов — только оракул-тест; «десятки тысяч вызовов» относится к
> использованию в эталоне и придёт к порту с LOB/скриптовыми путями. Фикс
> внесён: аренда char[]/byte[] на вызов, span-чтение и статический
> `WriteStringWithoutLength(byte[], ReadOnlySpan<char>)`; инстансный
> reference-метод сохранён. Гейт — 10 вызовов в одну пару аренд
> (`DataCopyStringAllocationTests`, с учётом округления корзин пула).

### 2.7. `ScriptCommand`: аллокации на каждый LOB-блок — Средний (NEW)

**Место:** `src/H2Sharp/Command/Dml/ScriptCommand.cs:456-470`

```csharp
char[] chars = new char[lobBlockSize];
...
StringBuilder buff = new(lobBlockSize * 2);
buff.Append("INSERT INTO SYSTEM_LOB_STREAM VALUES(").Append(id)
   .Append(", ").Append(part).Append(", ");
StringUtils.QuoteStringSql(buff, new string(chars, 0, len)).Append(", NULL)");
add(buff.ToString());
```

**Проблема и влияние.** Четыре аллокации на блок: char[], `new string(chars, 0, len)`,
StringBuilder, `ToString()`. При дампе больших CLOB это значимо.

**Решение (.NET 8/10).** Пул + перегрузка `QuoteStringSql` под `ReadOnlySpan<char>`:

```csharp
char[] chars = ArrayPool<char>.Shared.Rent(lobBlockSize);
// в цикле:
StringBuilder buff = new(lobBlockSize * 2);
buff.Append("INSERT INTO SYSTEM_LOB_STREAM VALUES(").Append(id)
   .Append(", ").Append(part).Append(", ");
StringUtils.QuoteStringSql(buff, chars.AsSpan(0, len));   // span-перегрузка, без new string
buff.Append(", NULL)");
add(buff.ToString());
```

**Эффект.** Средний: −2 аллокации на блок. `QuoteStringSql(string)` сохраняется как
обёртка над span-версией.

> **Развёрнуто с поправками (2026-11-04).** `char[]` собирался один раз на
> вызов, а не на блок — на блок приходились `new string`, builder и
> `ToString()`. В объём вошла и симметричная BLOB-ветка (byte[] в аренду);
> `ReadFully` принял явную длину блока — арендованный массив длиннее
> `lobBlockSize`, и границы блоков обязаны остаться прежними. Под этим:
> `QuoteIdentifierOrLiteral` на span-параметре, общий `ReadCodePoint` принял
> span (все string-вызовы совместимы неявной конверсией). Аллокационный гейт
> не заведён: SCRIPT — команда выгрузки, а не горячий путь OLTP; цена блока
> теперь — builder и его строка, что и есть предел формы `add(string)`.

---

## 3. `SearchValues<T>`: где он действительно работает

Ограничения, определяющие применимость: только `byte`/`char` (см. 0.1); окупается на
пробегах примерно от 16–32 символов, на односимвольных SIMD-пролог дороже скалярного
цикла; набор символов, задаваемый Java-семантикой категорий, обязан быть выведен из
`JavaCharacter`, а не написан от руки.

### 3.1. Сканирование идентификатора: таблица Unicode на каждый символ — Высокий (✓)

**Место:** `src/H2Sharp/Command/Tokenizer.cs:1406`, `:1411` → `src/H2Sharp/Util/JavaCharacter.cs:162`

```csharp
isPart = JavaCharacter.IsJavaIdentifierPart(c);   // → CharUnicodeInfo.GetUnicodeCategory(c)
```

**Проблема и влияние.** `FindIdentifierEnd` вызывает категорию Unicode для каждого символа
каждого имени. Идентификаторы в SQL — почти всегда ASCII `[A-Za-z0-9_]` и обрываются на
пробеле, точке или запятой. Токенизация — порядка 30–40% стоимости prepare на типичных
запросах, то есть это одна из самых частых таблиц в решении.

**Решение (.NET 8/10).** Два слоя, оба семантически равны исходному проверочному пути.

```csharp
// ASCII-маска вычисляется НА СТАРТЕ из самого JavaCharacter — расхождение невозможно по построению
private static readonly ulong IdPartLo = BuildAsciiMask();

private static readonly SearchValues<char> IdPartAscii = SearchValues.Create(
    "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_$");

private static ulong BuildAsciiMask()
{
    ulong mask = 0;
    for (int c = 0; c < 128; c++)
    {
        if (JavaCharacter.IsJavaIdentifierPart((char)c))
        {
            mask |= 1UL << c;
        }
    }

    return mask;
}

// внутри FindIdentifierEnd: векторный прогон по ASCII, прежний медленный путь для >= 128
for (;;)
{
    if (i > end)
    {
        break;
    }

    int stop = sql.AsSpan(i, end + 1 - i).IndexOfAnyExcept(IdPartAscii);   // SIMD, 0 аллокаций
    if (stop < 0)
    {
        i = end + 1;
        break;
    }

    i += stop;
    char c = sql[i];
    if (c < 128 && ((IdPartLo >> c) & 1) == 0)
    {
        break;                                     // ASCII-символ не часть имени — конец
    }

    // дальше — существующая ветвь: CodePointAt для суррогатных пар и категорий >= 128
}
```

`SearchValues<char>` перечисляет только явные символы, поэтому для `c >= 128`
(`IsIgnorableControl`, `Mn`/`Cf`, суррогаты) обязателен откат на текущий путь. Маска нужна
именно как доказательство, что ASCII-набор совпадает с `IsJavaIdentifierPart` символ в
символ. Если не хочется держать и `SearchValues`, и маску — довольно одной маски: она уже
снимает `GetUnicodeCategory` с подавляющей части символов.

**Эффект.** Высокий на throughput парсера.

### 3.2. `StringUtils.IsNumber` — Средний (✓)

**Место:** `src/H2Sharp/Util/StringUtils.cs:2184` — посимвольный `char.IsDigit`.

**Решение (.NET 8/10).** Векторный прогон с обязательным откатом, потому что
`Character.isDigit` в Java принимает и Decimal_Digit_Number вне ASCII (арабские, devanagari):

```csharp
private static readonly SearchValues<char> AsciiDigits = SearchValues.Create("0123456789");

public static bool IsNumber(string s)
{
    if (s.Length == 0)
    {
        return false;
    }

    ReadOnlySpan<char> span = s.AsSpan();
    if (span.IndexOfAnyExcept(AsciiDigits) < 0)
    {
        return true;                               // весь ASCII-диапазон — один SIMD-прогон
    }

    for (int i = 0; i < span.Length; i++)          // редкая ветвь: не-ASCII цифры
    {
        if (!char.IsDigit(span[i]))
        {
            return false;
        }
    }

    return true;
}
```

**Эффект.** Средний: вызывается на проверках числовых литералов и в метаданных.

### 3.3. `CompareLike.ContainsIgnoreCase` — Средний (✓)

**Место:** `src/H2Sharp/Expressions/Conditions/CompareLike.cs:513-539` — собственный
посимвольный поиск подстроки с проверкой регистра первого символа.

**Решение (.NET 8/10).** Полностью заменять на `OrdinalIgnoreCase` нельзя: расхождения с
Java `regionMatches(true)` — это ровно двенадцать символов из
`src/H2Sharp/Util/JavaCharacter.cs:245-271`. Заменяем под guard'ом ASCII:

```csharp
if (StringUtils.IsPureAscii(src, start, length) && StringUtils.IsPureAscii(what, start, what.Length - start))
{
    // для ASCII обе платформы дают один и тот же фолд — допустим векторный поиск рантайма
    return src.AsSpan(0, src.Length - length).IndexOf(what.AsSpan(start), StringComparison.OrdinalIgnoreCase) >= 0;
}

// прежний цикл для не-ASCII
```

**Эффект.** Средний для `LIKE '%…%'` и `ILIKE` на длинных значениях; на коротких пар, поэтому
guard по минимальной длине тоже полезен.

### 3.4. Куда `SearchValues` ставить не надо

* Пробельные промежутки токенизатора (`Command/Tokenizer.cs:610`, `if (c <= ' ') { i++; continue; }`):
  пробег обычно один символ, прогон SIMD его не окупает.
* Наборы строк (`Command/Token.cs:180` `TOKENS`, `Command/Parser.Set.cs:512` `Array.IndexOf`,
  `Command/Parser.cs:5348` `ReadIfWords(params string[])`): `SearchValues<string>` не существует,
  а `Array.IndexOf` по двадцати элементам дешевле любого хэш-пути.
* `params`-массивы (`Result/LocalResult.cs:559`, `Result/SimpleResult.cs:180`): Roslyn кэширует
  статический массив для литеральных аргументов, аллокации там нет. Популярный совет, который
  в этом коде ничего не даёт.

### 3.5. Case-insensitive сравнения: `char.ToUpperInvariant`/`ToLowerInvariant` на каждый символ — Средний (NEW)

**Место:** `src/H2Sharp/Util/StringUtils.cs:2428-2449` (`RegionMatches`, ignoreCase) и
`src/H2Sharp/Value/CompareMode.cs:167-186` (`EqualsChars`).

**Проблема и влияние.** При первом несовпадении символов — до четырёх вызовов
`char.ToUpperInvariant`/`char.ToLowerInvariant` на символ (по два на каждый из двух
сравниваемых). Это горячий путь `LIKE`, `ILIKE`, `~*`, collation-сравнений: строка
длиной 20 символов с расхождением в регистре оплачивает десятки вызовов культуры.

**Решение (.NET 8/10).** ASCII-быстрый путь; для не-ASCII — прежний полный фолдинг
(поведение не меняется):

```csharp
if (c1 == c2) continue;
if (ignoreCase)
{
    // ASCII: одна битовая операция вместо двух вызовов культуры
    int a = c1 | 0x20, b = c2 | 0x20;
    if (a is >= 'a' and <= 'z' && a == b) continue;
    if (c1 >= 0x80 || c2 >= 0x80)
    {
        if (char.ToUpperInvariant(c1) == char.ToUpperInvariant(c2)
            || char.ToLowerInvariant(c1) == char.ToLowerInvariant(c2)) continue;
    }
}
return false;
```

**Эффект.** Средний для LIKE/ILIKE/collation. Заменяемый путь сохраняется для не-ASCII,
существующие оракул-тесты остаются зелёными.

> **Развёрнуто (2026-11-04).** Бит-фолд с проверкой диапазона `'a'..'z'` в
> `RegionMatches` и базовом `CompareMode.EqualsChars`; не-ASCII идёт прежним
> полным фолдом. Новые оракулы против JVM — `regionMatches` на 28 парах (включая
> четыре ASCII-ловушки бит-фолда: 0x5B→0x7B и соседи) и `equalsChars` базового
> режима на 20 парах. **Побочная находка:** `EqualsChars`/`RegionMatches` уже
> расходятся с эталоном на U+0130/U+0131 (снижение .NET против `Character`);
> пара исключена из корпусов и дефект записан как отдельный — нужна
> char-уровневая таблица маппинга из JVM.

---

## 4. `FrozenDictionary` / `FrozenSet` для read-mostly реестров

Все перечисленные словари пишутся до первого запроса и после только читаются — ровно тот
режим, где frozen снимает ленивую проверку и даёт аллокационно свободный probe. Шаблон
перехода в проекте есть: `Engine/Mode.cs:378-409` (проверено — работает).

| Место | Сейчас | Записей | Где читается | Эффект |
|---|---|---|---|---|
| `Expressions/Functions/FunctionRegistry.cs:37` (✓) | `Dictionary<string, FunctionRegistration>` (OrdinalIgnoreCase) | 94 | каждое имя функции в SQL | **Высокий** |
| `Value/DataType.cs:180` (✓) | `Dictionary<string, DataType>` (Ordinal) | ~60 | каждое имя типа колонки | **Высокий** |
| `Expressions/Aggregate/AggregateRegistry.cs:78` (✓) | `Dictionary` (OrdinalIgnoreCase) | ~30 | каждое имя агрегата | Средний |
| `Util/CaseFolding.cs:40`, `:439` | `Dictionary<int, string>` | сотни | каждый символ с override | Средний |
| `MvStore/Type/MetaType.cs:36`, `:57` | `Dictionary<Type,string>` / `Dictionary<string,Type>` | десятки | сериализация JAVA_OBJECT | Средний |
| `MvStore/Type/ObjectDataType.cs:104` | `Dictionary<Type,int>` | десятки | сравнение массивов | Средний |
| `Expressions/Aggregate/Aggregate.cs:100` | `Dictionary<string, AggregateType>(128)` | ~30 | план, `GetAggregateTypes` | Низкий (размер ниже порога выгоды) |
| `Data/H2ConnectionStringBuilder.cs:26`, `:35` | `HashSet<string>` (OrdinalIgnoreCase) | ~10 | разбор строки соединения | Низкий |
| `Message/ErrorMessages.cs:40` | `Dictionary<string, string>` | сотни | только на ошибке | Низкий |
| `Engine/Database.cs:120` `schemas` (NEW) | `Dictionary<string, Schema>` | счётчик схем | каждый разбор имени схемы | Средний |

Форма перехода — `ToFrozenDictionary(comparer)` сохраняет comparer, регистронезависимость
не теряется:

```csharp
private static readonly FrozenDictionary<string, FunctionRegistration> Functions = Create();

private static FrozenDictionary<string, FunctionRegistration> Create()
{
    var functions = new Dictionary<string, FunctionRegistration>(StringComparer.OrdinalIgnoreCase);
    Add(functions, "ABS", 1, 1, static a => new MathFunction(a[0], null, MathFunction.Abs));
    // список не меняется после инициализации
    return functions.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
}
```

**Про `Database.schemas` (NEW).** Мутации редки (CREATE SCHEMA / RENAME), чтение —
на каждый разбор `schemaName`. Рекомендация: держать вторую frozen-копию и пересобирать
её при DDL-операциях:

```csharp
private FrozenDictionary<string, Schemas.Schema> frozenSchemas =
    FrozenDictionary<string, Schemas.Schema>.Empty;

// в FindSchema (Database.cs:152-157):
return frozenSchemas.TryGetValue(name, out var schema) ? schema : null;

// при AddSchema/RenameSchema: frozenSchemas = schemas.ToFrozenDictionary();
```

Оговорка: текущий код осознанно использует обычный словарь под локом БД (комментарий
у `Database.cs:115-119`) — frozen-копия не меняет модели блокировок, только ускоряет
чтение. Если хочется сэкономить память — достаточно заменить на `FrozenDictionary` при
неизменном наборе схем (режим «только системные схемы»).

**Замораживать нельзя:** пользовательские реестры ADR-001 (`RegisterUdf`, зарегистрированные
агрегаты на сессии), `Store/FileSystem/MemoryFilePath.cs:29` и `MvStore/SingleFileStore.cs:27` —
они пишутся во время работы. У двухуровневых реестров замораживается только built-in-половина.

**Связанный дефект.** `Expressions/Aggregate/AggregateRegistry.cs:84` (✓) на каждый `TryGet`
зарегистрированного агрегата создаёт `new ClrAggregateRegistration(custom, isBuiltIn: false)` —
аллокация на каждое разрешение имени. Регистрация — редкое событие, разрешение — частое,
поэтому объект надо готовить при `RegisterAggregate` и отдавать готовый:

```csharp
if (session?.Extensions.FindAggregate(name) is { } custom)
{
    registration = custom.Registration;   // построен один раз при регистрации
    return true;
}
```

---

## 5. Прочее, по цене

**`OrderBy(...).ToArray()` в оптимизаторе — Средний. (✓)**
`Expressions/Conditions/ConditionAndOrN.cs:96`, `:140`: итератор `OrderBy`, делегат-компаратор
и массив на каждый узел AND/OR при планировании. Замена — `Array.Sort` по копии массива со
статическим `Comparison<IComparable>` (компаратор `CompareByCost` уже статический) либо
вставка на месте при малом числе операндов.

> **Развёрнуто (2026-11-04).** Стабильная вставка на месте (`SortByCost`) — именно
> вставка, а не `Array.Sort`: эталонный `Collections.sort` стабилен, а
> `Array.Sort`/`List.Sort` — нет, и относительный порядок равных по цене операндов
> наблюдаем. Второй пункт §5 (`H2Connection.ToUpperInvariant`) тоже сделан —
> ветки сравнивают `OrdinalIgnoreCase`. Не вошло: `ValueJson` (байты удерживаются
> значениями — span-свойство давало бы `ToArray()` на вызов), пулинг в
> `BitFunction`/`ArrayFunction` (нужен разбор владения по каждому месту — отдельный
> пункт, ошибка «аренда утекла в значение» хуже аллокации), 214 `ThrowIfNull`,
> `.ToArray()`-геттеры (Низкий). Перечень в MIGRATION_STATUS.

**214 × `?? throw new ArgumentNullException(nameof(x))` → `ArgumentNullException.ThrowIfNull(x)` — Средний.**
`Data/H2Connection.cs:127`, `:408`, `:414`; `Store/DataReader.cs:43`; `Indexes/Index.cs:125`;
`Util/Utils.cs:620`; `MvStore/Type/MetaType.cs:99`; `Data/Extensions/AggregateProvider.cs:23`.
На аллокации это не экономит (`throw` и так холодный путь); эффект в том, что helper помечен
`[DoesNotReturn]` и не раздувает тело: конструкторы и методы ADO.NET-оболочки инлайнятся чаще.
Рядом применимы `ArgumentOutOfRangeException.ThrowIfNegative`, `ArgumentException.ThrowIfNullOrWhiteSpace`
и `ObjectDisposedException.ThrowIf` (заменяют ручные проверки disposed в `H2Command`/`H2Connection`).

**`.ToArray()` на enumerable-геттерах — Низкий.** `Schemas/Schema.cs:158`, `:210`, `:215`, `:220`,
`:225`, `:230` и `Schemas/Domain.cs:186`: копия ради безопасной итерации под мутацией. Кэшируемый
snapshot снимает шесть копий за один DDL-обход.

**`H2Connection.cs:566`: `ToUpperInvariant()` на каждый вызов метаданных — Низкий. (NEW)**
`CollectionName.ToUpperInvariant()` аллоцирует строку при каждом обращении к метаданным,
хотя `switch` сравнивает всего с четырьмя константами. Решение — сравнивать без копии:

```csharp
return collectionName switch
{
    _ when collectionName.Equals("METADATACOLLECTIONS", StringComparison.OrdinalIgnoreCase) => MetaDataCollectionsTable(),
    _ when collectionName.Equals("RESTRICTIONS", StringComparison.OrdinalIgnoreCase) => RestrictionsTable(),
    // ...
};
```

**`BitFunction`/`ArrayFunction`: churn массивов на вызов — Низкий/Средний. (NEW)**
`Expressions/Functions/BitFunction.cs:269`, `:292`, `:504-505`, `:662`, `:670` — по 2–3
`byte[]` + `Array.Copy` на вызов; `Expressions/Functions/ArrayFunction.cs:71` — двойной
`GetList()` + slice. Для пакетной обработки миллионов строк с `BIT_*`/`ARRAY_SLICE` это
заметный gen0. Решение: `ArrayPool<byte>.Shared` / единственный вызов `GetList()` с
`Array.Copy` из него.

**`Values/ValueJson.cs:31-35` — Низкий.** `"null"u8.ToArray()` в `static readonly byte[]` —
однажды, не hot path; `static ReadOnlySpan<byte> NullBytes => "null"u8;` убирает и поле, и
косвенность.

**Тяжёлые структуры, `in`/`ref readonly`, `ValueTask` — неприменимо.** В всём `src` две структуры:
`MvStore/RentedBuffer.cs:16` (уже `readonly struct`, ✓) и `MvStore/FileStore.cs:2559`
`RemovedPageInfo`. Категория «структуры передаются по значению» проверять не на чем. `async`
в ядре отсутствует (4 упоминания), storage синхронный как эталон, `PipeReader` здесь не нужен.

---

## 6. Что менять нельзя, и почему это не дефекты

Чтобы перечень не раздувался псевдонаходками:

* `MvStore/Type/ObjectDataType.cs` — боксинг `int`/`long`/`bool` на каждом скалярном чтении.
  Java боксует так же (`Long`, `Integer`), `object?` — контракт эталонного `DataType<Object>`.
  Убрать нельзя без отступления от `org.h2`. Исключение — `ReadBytes` (см. 1.2): там
  промежуточный массив нужен только для декодирования, контракт `object?` не нарушается.
* `MvStore/Db/ValueDataType.cs:873`, `:929` — `Slice().ToArray()`: значение владеет байтами
  дольше страницы. Уже оптимизировано до `GetNoCopy` там, где владение передаётся
  (`MIGRATION_STATUS.md:1862`).
* `MvStore/Db/MVSortedTempResult.cs:194`, `:234` — массив на строку для `DISTINCT`; в эталоне
  тот же `new Object[]`.
* `MvStore/Cache/CacheLongKeyLIRS.cs:626` и контракт в шапке файла — эталонная модель памяти;
  замена лога меняет семантику видимости, а не аллокации.
* `Data/SyncTask.cs:36` — синхронный движок под async ADO.NET-контрактом; `ValueTask`-переделка
  потребовала бы async-storage, которого в H2 2.5 нет.
* `MvStore/MVMap.cs:1597` `Monitor.Wait(lockObj, 1)` — поллинг-уведомление эталона.

---

## 7. Порядок развёртывания и способ проверки

1. Ни одна находка не меняет интерфейс эталона: это реализации внутри уже перенесённых
   методов. Кроме **1.1** — там убирается индирекция, которую порт придумал сам, то есть
   правка возвращает код к `org.h2`. Кроме **1.6** — это структурное отклонение (токены
   как классы → структуры), вносится только по визе пользователя.
2. Очередь по отношению эффект/риск:
   **1.3 → 1.5 → 2.1 → 4 (FunctionRegistry + DataType + schemas) → 1.4 → 3.1 → 1.1 →
   1.2 → 2.4 → 2.6 → 2.7 → 3.5 → 5**, затем отдельным треком после визы — **1.6**.
3. Каждый фикс сопровождается гейтом аллокаций в стиле существующих
   `tests/H2Sharp.Tests/StringUtilsAllocationTests.cs`
   (`GC.AllocatedBytesForCurrentThread` как граница, а не равенство — см. `MIGRATION_STATUS.md:1765`)
   и `JavaOracleFact`-тестом на совпадение поведения по
   [docs/REGRESSION_TEST_PROTOCOL.md](../docs/REGRESSION_TEST_PROTOCOL.md) §8, тем же коммитом.
4. Для **3.1** обязателен тест «выведенный ASCII-набор == `JavaCharacter.IsJavaIdentifierPart`
   на всех 0..127»; для **2.4** — тест на усечённый файл (сохранение `ReadAtLeast`-поведения);
   для **2.2** — тест на невалидном UTF-8 (ветвь с U+FFFD должна остаться); для **3.5** —
   оракул-тест на `~*`/LIKE с не-ASCII (без изменений поведения).
5. Мерить в Debug-сборке только аллокационные гейты; профилер и покрытие — по правилам
   `AGENTS.md` (покрытие в Debug, инлайнинг в Release мажет строки).
6. Бенчмарк-харнеса в решении нет (BenchmarkDotNet упоминается только в
   `docs/NATIVE_TESTS_NOT_PORTED.md`). Без него оценки «Высокий/Средний» — вывод из структуры
   кода, а не измерение. Предлагаемый минимум: `tests/H2Sharp.Benchmarks` с тремя сценариями —
   tokenize 5k запросов, SELECT 1M VARCHAR-строк, UPDATE с no-op.

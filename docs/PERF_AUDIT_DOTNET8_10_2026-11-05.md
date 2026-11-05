# Перф-аудит H2Sharp (.NET 8 / .NET 10)

Дата: 2026-11-05.
Область: `src/H2Sharp` — 595 файлов, `net8.0;net10.0`.
Проверено по исходникам. Хороший задел уже есть: `Tokenizer.cs:51,1641,1646` — `SearchValues`, `Value/DataType.cs:186`, `Engine/Database.cs:136`, `FunctionRegistry` — `FrozenDictionary/Set`. Ниже — то, что осталось.

Формат каждой находки: место / проблема и влияние / современное решение / эффект.

## 1. Span / Memory вместо Substring / ToArray / new byte[]

### 1.1 Value.Core.cs:105-110 — GetReader() делает двойную копию CLOB

- **Уязвимое место:** `src/H2Sharp/Value/Value.Core.cs:110`:
```csharp
string value = GetString() ?? string.Empty;
return new StringReader(value.Substring((int)zeroBasedOffset, (int)length));
```
- **Проблема и влияние:** `GetString()` материализует весь CLOB + `Substring` копирует срез. На больших CLOB — 2 аллокации размером документа, давление на LOH/GC.
- **Современное решение (.NET 8/10):**
```csharp
return new CharSliceReader(GetString().AsMemory((int)zeroBasedOffset, (int)length));
// CharSliceReader : TextReader поверх ReadOnlyMemory<char>, без Substring
```
- **Ожидаемый эффект:** Высокий для CLOB-чтения.

### 1.2 Expressions/Functions/StringFunction1.cs:193 — Substring(i,4) в цикле HEX

- **Уязвимое место:** `src/H2Sharp/Expressions/Functions/StringFunction1.cs:193`:
```csharp
_ = builder.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
```
- **Проблема и влияние:** аллокация `string` на каждые 4 hex-символа в цикле `HEXTORAW`. O(n) временных строк.
- **Современное решение:**
```csharp
ReadOnlySpan<char> h = s.AsSpan(i, 4);
// ручной nibble-парсинг или int.Parse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture)
```
- **Ожидаемый эффект:** Высокий, hot path строковых функций.

### 1.3 Data/H2ValueMapper.cs:229-231,246 — MemoryStream.ToArray() + new byte[64K]

- **Уязвимое место:** `src/H2Sharp/Data/H2ValueMapper.cs:246` `byte[] buffer = new byte[64*1024];` + `copy.ToArray()` для BLOB-параметров.
- **Проблема и влияние:** весь стрим параметра копируется; буфер 64К на каждый файловый fallback; `GetByteCount(text)` считается дважды.
- **Современное решение:** `ArrayPool<byte>.Shared.Rent(64<<10)` + `try/finally Return`; писать в `ArrayBufferWriter<byte>` и отдать `WrittenSpan` в `ValueBlob.CreateSmall(span)`; `GetByteCount` — один раз в локальную.
- **Ожидаемый эффект:** Критический для bulk INSERT BLOB/CLOB.

### 1.4 MvStore/Db/ValueDataType.cs:909,929,975 — buff.Slice(...).ToArray() при чтении страниц

- **Уязвимое место:** `ValueDataType.cs:909,929,975` `byte[] small = buff.Slice(position, smallLen).ToArray();`
- **Проблема и влияние:** копия каждого small-LOB при каждом чтении строки `MVMap`. Самый горячий путь чтения.
- **Современное решение:** не копировать если потребитель умеет `ReadOnlySpan<byte>`; иначе `GC.AllocateUninitializedArray<byte>(len)` + `buff.Slice(...).CopyTo(bytes)` вместо `ToArray()`.
- **Ожидаемый эффект:** Критический.

### 1.5 MvStore/MVStore.cs:709,734, FileStore.cs:882 — Substring на мета-скане

- **Уязвимое место:** `MVStore.cs:709` `key.Substring(DataUtils.MetaName.Length)`, `FileStore.cs:882` `key.Substring(key.LastIndexOf('.')+1)`.
- **Проблема и влияние:** `Substring` на каждом ключе `meta` при open/repair.
- **Современное решение:**
```csharp
ReadOnlySpan<char> rest = key.AsSpan(DataUtils.MetaName.Length);
int id = DataUtils.ParseHexInt(rest);
```
- **Ожидаемый эффект:** Средний, холодный, но тривиальный.

### 1.6 Util/StringUtils.cs:1131-1160,2199 — new byte[length] + GetString(byte[],0,j)

- **Уязвимое место:** `UrlDecode`, `ConvertHexToBytes`: `byte[] buff = new byte[length]; return Encoding.UTF8.GetString(buff,0,j);`
- **Проблема и влияние:** временный массив длиной входа + старая перегрузка без `Span`.
- **Современное решение:** `byte[] rented = ArrayPool<byte>.Shared.Rent(length); ... Encoding.UTF8.GetString(rented.AsSpan(0,j));` для `len<=256` — `stackalloc byte[len]`.
- **Ожидаемый эффект:** Средний.

## 2. SearchValues<T> — SIMD вместо ручных циклов / каскадов IndexOf

Паттерн замены везде одинаковый:
```csharp
private static readonly SearchValues<char> S = SearchValues.Create(",\"\\");
int rel = s.AsSpan(i).IndexOfAny(S); // один SIMD-проход, zero-alloc
```

### 2.1 MvStore/DataUtils.cs:870,937-976,1092-1118 — парсинг map-заголовков

- **Уязвимое место:** `while(i<size){ c=s[i++]; if(c==',')... if(c=='"')... if(c=='\\')...}` + `value.IndexOf(',')<0 && value.IndexOf('"')<0`.
- **Проблема и влияние:** скалярные ветки на каждый символ чанка + два полных скана.
- **Современное решение:** `SearchValues.Create(",\"\\")`, `s.AsSpan().IndexOfAny(',', '"')` для 870.
- **Ожидаемый эффект:** Высокий, чтение store.

### 2.2 Util/DateTimeUtils.cs:942-945,972-997,174-235 + IntervalUtils.cs:324-418

- **Уязвимое место:** `s.IndexOf(' ')` затем `s.IndexOf('T')`; `s.IndexOf('+')` затем `s.IndexOf('-')` затем `s.IndexOf('[')`; `s.IndexOf(':')`/`s.IndexOf('.')` по 3 раза; 8 мест в `IntervalUtils` с `IndexOf(' ')/IndexOf(':')/IndexOf('.')`.
- **Проблема и влияние:** 2-4 последовательных скана одного суффикса на каждый TIMESTAMP/TIME/INTERVAL.
- **Современное решение:** `SearchValues.Create(" T")`, `SearchValues.Create("+- [")`, `SearchValues.Create(":.")`, `SearchValues.Create(" :.")`.
- **Ожидаемый эффект:** Высокий, парсинг даты/времени — hot path.

### 2.3 Util/StringUtils.cs:693-730,1497-1553,1191-1236

- **Уязвимое место:** `JavaEncode`: `if(c is >= ' ' and < 0x80 && c is not ('\t' or '\n' ...))`; `XmlText`: `switch(ch){case '<': case '>': case '&':...}`; `ArraySplit`: двойной цикл `separator/'\\'`.
- **Проблема и влияние:** набор из 7-9 символов проверяется ветками на каждый `char`.
- **Современное решение:** `SearchValues.Create("\t\n\f\r\"'\\")`, `SearchValues.Create("<>&'\"\r\n\t")`, `s.AsSpan(i).IndexOfAny(separatorChar,'\\')`.
- **Ожидаемый эффект:** Высокий для XmlText/JavaEncode, средний для ArraySplit.

### 2.4 Data/H2ParameterRewriter.cs:44-46, Data/H2Command.cs:474

- **Уязвимое место:** `if(text.IndexOf('$')<0){ if(text.IndexOf('?')<0) return (text,[]);}`
- **Проблема и влияние:** двойной скан всего SQL-текста до токенизации на каждую команду.
- **Современное решение:** `private static readonly SearchValues<char> ParamMarks = SearchValues.Create("$?"); if(text.AsSpan().IndexOfAny(ParamMarks)<0)...`
- **Ожидаемый эффект:** Высокий, gate парсера.

## 3. FrozenDictionary / FrozenSet

### 3.1 Expressions/Aggregate/Aggregate.cs:100 — единственный hot-path кандидат

- **Уязвимое место:** `private static readonly Dictionary<string,AggregateType> Aggregates = new(128);` заполняется только в `static` ctor, читается в `GetAggregateType()` из `Parser.cs:6403`.
- **Проблема и влияние:** `Dictionary` walk по бакетам на каждый резолв агрегата.
- **Современное решение:**
```csharp
private static readonly FrozenDictionary<string,AggregateType> Aggregates = CreateAggregates();
// builder Dictionary(128, StringComparer.Ordinal).ToFrozenDictionary(StringComparer.Ordinal)
```
- **Ожидаемый эффект:** Высокий. Семантика `Ordinal` сохранить — регистронезависимость дает верхний `AggregateRegistry` с `OrdinalIgnoreCase`.

### 3.2 Message/ErrorMessages.cs:40, Data/H2ConnectionStringBuilder.cs:26,35

- **Уязвимое место:** `Dictionary<string,string> Messages = Load();` + `HashSet<string> KnownKeys/CredentialKeys`.
- **Проблема и влияние:** write-once/read-many, сейчас обычный `Dictionary/HashSet`.
- **Современное решение:** `.ToFrozenDictionary(StringComparer.Ordinal)` / `.ToFrozenSet(StringComparer.OrdinalIgnoreCase)`.
- **Ожидаемый эффект:** Низкий — пути холодные (исключения, connection string), но конверсия бесплатна.

### 3.3 Value/DataType.cs:184 — убрать поле-билдер

- **Уязвимое место:** `private static readonly Dictionary<...> typesByNameBuilder` живет вечно после `static` ctor, все лукапы идут в `TypesByName:186` (`FrozenDictionary`).
- **Проблема и влияние:** минус один долгоживущий объект без изменения поведения.
- **Современное решение:** сделать билдер локальной переменной `static` ctor.
- **Ожидаемый эффект:** Низкий.

### 3.4 НЕ трогать

- `Store/FileSystem/MemoryFilePath.cs:29`, `FilePath.cs:43`, `MvStore/SingleFileStore.cs:27`, `DateTimeFunction.cs:223` — мутабельные под `lock`/`ConcurrentDictionary`.
- `Command/Parser.cs:5384-5409` `Words*` — позиционный матчинг 1-4 токенов, `FrozenSet.Contains` бессмысленен.
- `*Function.Names[]`, `SetTypes/Comparison/Trace` — индекс→имя, диспетчеризация уже через `FunctionRegistry.Functions` (`Frozen`).

## 4. Скрытые аллокации / JIT / GC

### 4.1 MvStore/Tx/TransactionMap.cs:260,286,332,409,424 — боксинг long ключа

- **Уязвимое место:** `public readonly MVMap<object,VersionedValue<object>> map;` + `map.Put((object)key!, ...)` / `map.Get(..., (object)key!)`.
- **Проблема и влияние:** `K` в реальности `long` (rowId) — каждый `Get/Put` боксит ~24B, миллионы раз на scan/update.
- **Современное решение:** типизированный `MVMap<long,...>` / специализированный `TransactionMap<long,V>` без `object`.
- **Ожидаемый эффект:** Критический.

### 4.2 Indexes/IndexCondition.cs:220,230 + Data/H2DataReader.cs:564-566

- **Уязвимое место:** `Comparer<Value>.Create((o1,o2)=>...)` с захватом `sortOrder/forward` на каждое `IN`; `ReadNumeric<T>(..., Func<int,T>x6)` — 6 лямбд на каждую числовую ячейку.
- **Проблема и влияние:** делегат + closure + `Comparer` / 6x`Func<>` на ячейку. Hot path чтения результата.
- **Современное решение:** статический кэшированный компаратор + `reverse`-флаг; в ридере — прямой `switch(cell.GetValueType())` без делегатов.
- **Ожидаемый эффект:** Высокий.

### 4.3 Task.FromResult на синхронном пути — Data/H2Batch.cs:140,144,148, H2DataReader.cs:631, Data/SyncTask.cs:45

- **Уязвимое место:** `=> Task.FromResult(ExecuteNonQuery());`, `SyncTask.Run(()=>GetFieldValue<T>(...))`.
- **Проблема и влияние:** `Task<T>` + `Func<T>`-closure на каждый async-вызов при синхронном движке.
- **Современное решение:** `ValueTask<T>` / `ValueTask.FromResult`, локальная функция без захвата, `cancellationToken.ThrowIfCancellationRequested()`.
- **Ожидаемый эффект:** Средний-высокий для ADO-чтения.

### 4.4 ToArray()/ToList() в циклах — Engine/SessionLocal.cs:397,1614, Command/Parser.Columns.cs:849, Result/LocalResult.cs:512, Tables/TableTypes.cs:1319

- **Уязвимое место:** `foreach(Table t in updates.ToArray())` на каждый `Commit`; `values.ToArray()` на каждую строку `VALUES`; `new Value?[n]+Array.Copy` на каждую строку результата.
- **Проблема и влияние:** копия коллекции на каждую итерацию/строку.
- **Современное решение:** итерировать напрямую если нет мутации; второй проход для удалений через арендованный `List<string>`; `AddRow(List<Expression>)`-перегрузка / `CollectionsMarshal.AsSpan` + кэш `VisibleColumns` с инвалидацией по DDL; пул строк `ArrayPool<Value?>`.
- **Ожидаемый эффект:** Высокий для bulk INSERT / SELECT.

### 4.5 ?? throw new ArgumentNullException / if(disposed) throw вместо ThrowIf*

- **Уязвимое место:** `Data/H2Connection.cs:127`, `Store/DataReader.cs:43`, `Indexes/Index.cs:125`, `Data/H2Transaction.cs:92,101,125`, `JavaLegacy/JavaRandom.cs:85,101`, `MvStore/MVStoreTool.cs:64,90` (`string.Format` с боксингом).
- **Проблема и влияние:** больше IL в hot-конструкторах, хуже инлайнинг; `string.Format("{0}",int)` боксит + `params object[]`.
- **Современное решение:**
```csharp
ArgumentNullException.ThrowIfNull(connectionString);
ObjectDisposedException.ThrowIf(disposed, this);
ArgumentOutOfRangeException.ThrowIfNegative(maxValue);
// диагностика: интерполяция + CultureInfo.InvariantCulture вместо string.Format
```
Паттерн уже используется в `CommandContainer.cs:44`, `H2DataSourceBuilder.cs:28` — распространить.
- **Ожидаемый эффект:** Низкий-средний по каждой точке, суммарно заметный в ADO hot path.

## Приоритет внедрения

1. Критический: 1.3, 1.4, 4.1.
2. Высокий: 1.1, 1.2, 2.1, 2.2, 2.4, 3.1, 4.2, 4.4.
3. Средний/низкий: остальное — дешево, делать пакетом.

# Перф-аудит H2Sharp (.NET 8 / .NET 10)

Дата: 2026-11-05. Перепроверка по исходникам: тот же день.
Область: `src/H2Sharp` — 595 файлов, `net8.0;net10.0`.
Статус перепроверки: 6 пунктов исходного аудита уже исправлены в коде — они помечены `[ИСПРАВЛЕНО]`. Остальное подтверждено чтением файлов.

Формат каждой находки: место / проблема и влияние / современное решение / эффект.

## 0. Уже исправлено — снять из работы

- `[ИСПРАВЛЕНО] 1.1 Value.Core.cs:110` — сейчас `new CharSliceReader(value.AsMemory(...))`, `Substring` нет. Остаток: сам `GetString()` материализует CLOB, но это семантика `Value`, не аллокация среза.
- `[ИСПРАВЛЕНО] 1.2-hex StringFunction1.cs:188-199 HexToRaw` — сейчас `s.AsSpan()` + `int.TryParse(span.Slice(i,4), AllowHexSpecifier)`, `Substring(i,4)` нет. Остался только `Space` (см. 1.2 ниже).
- `[ИСПРАВЛЕНО] 1.3-бинарный путь H2ValueMapper.cs:233-279` — сейчас `GC.AllocateUninitializedArray` + прямое чтение в целевой массив, файловый fallback через `ArrayPool.Shared.Rent/Return`. Двойного `GetByteCount` нет.
- `[ИСПРАВЛЕНО] 1.5 MVStore.cs:709,734 + FileStore.cs:882` — сейчас `key.AsSpan(...)` + `DataUtils.ParseHexInt(ReadOnlySpan<char>)` + `SequenceEqual`. `Substring` на горячем мета-скане убран.
- `[ИСПРАВЛЕНО] DataType.cs:327-328` — `TypesByName = builder.ToFrozenDictionary(...); typesByNameBuilder = null;` — статическое поле-билдер уже освобождается.
- `[ИСПРАВЛЕНО] H2ParameterRewriter.cs:44` — сейчас `text.AsSpan().IndexOf('$')`, сканирования на `'?'` нет. Остаток: `H2Command.cs:474` все еще `commandText.IndexOf('$')` строковой перегрузкой (см. 2.4).
- `[ИСПРАВЛЕНО] H2Connection.cs:127` — сейчас `ArgumentNullException.ThrowIfNull(connectionString)`.

## 1. Span / Memory вместо Substring / ToArray / new byte[]

### 1.1 [ИСПРАВЛЕНО] Value.Core.cs:105-110 — GetReader() — Substring убран

- **Было:** `new StringReader(value.Substring(...))`.
- **Стало (проверено):** `Value.Core.cs:110` — `return new CharSliceReader(value.AsMemory((int)zeroBasedOffset, (int)length));`.
- **Остаток:** сам `GetString()` материализует значение — это семантика `Value`, не правится спанами.
- **Ожидаемый эффект:** — (снять).

### 1.2 [ЧАСТИЧНО] Expressions/Functions/StringFunction1.cs:157-163 Space — new byte[] остался

- **Уязвимое место:** `src/H2Sharp/Expressions/Functions/StringFunction1.cs:159-161`:
```csharp
byte[] chars = new byte[Math.Max(0, v.GetInt())];
Array.Fill(chars, (byte)' ');
v = ValueVarchar.Get(Encoding.Latin1.GetString(chars), session);
```
Hex-ветка (`HexToRaw`, `StringFunction1.cs:188-199`) уже на спане — не трогать.
- **Проблема и влияние:** временный `byte[]` размером `n` + декод Latin1 только ради строки из пробелов. `SPACE(n)` с большим `n` кладет массив в LOH.
- **Современное решение (.NET 8/10):**
```csharp
v = ValueVarchar.Get(new string(' ', Math.Max(0, v.GetInt())), session);
// или string.Create(n, ..., static (sp, _) => sp.Fill(' '))
```
- **Ожидаемый эффект:** Средний.

### 1.3 [ПОДТВЕРЖДЕНО ЧАСТИЧНО] Data/H2ValueMapper.cs — остался ReadText через string

- **Уязвимое место:** `src/H2Sharp/Data/H2ValueMapper.cs:296-317` `ReadText`: `string text = reader.ReadToEnd(); ... File.WriteAllText(fileName, text); GetByteCount(text)`.
- **Что уже исправлено:** бинарный путь `226-279` — `GC.AllocateUninitializedArray` + `ArrayPool.Shared.Rent(64*1024)` с `Return` (проверено).
- **Проблема и влияние:** текстовый путь дважды материализует CLOB (string + файл) и считает байты отдельно.
- **Современное решение:** стримить `TextReader` через `Encoder`/`StreamWriter` в файл за один проход с подсчетом байтов на лету; inline-ветку оставить.
- **Ожидаемый эффект:** Высокий для больших CLOB-параметров; бинарный путь — снять.

### 1.4 MvStore/Db/ValueDataType.cs:909,929,975 — buff.Slice(...).ToArray() при чтении страниц

- **Уязвимое место:** `ValueDataType.cs:909,929,975` `byte[] small = buff.Slice(position, smallLen).ToArray();`
- **Проблема и влияние:** копия каждого small-LOB при каждом чтении строки `MVMap`. Самый горячий путь чтения.
- **Современное решение:** не копировать если потребитель умеет `ReadOnlySpan<byte>`; иначе `GC.AllocateUninitializedArray<byte>(len)` + `buff.Slice(...).CopyTo(bytes)` вместо `ToArray()`.
- **Ожидаемый эффект:** Критический.

### 1.5 [ИСПРАВЛЕНО] MvStore/MVStore.cs:709,734, FileStore.cs:882 — Substring убран

- **Проверено:** `MVStore.cs:709` `ReadOnlySpan<char> mapName = key.AsSpan(...)`, `:734` `ReadOnlySpan<char> mapIdStr = key.AsSpan(...)`, `FileStore.cs:882` `ReadOnlySpan<char> mapIdStr = key.AsSpan(...)` + `ParseHexInt(span)`.
- **Остаток (низкий):** `MVStore.cs:928` `set.Add(x.Substring(DataUtils.MetaName.Length))`, `FileStore.cs:2039` `entry.Key.Substring(DataUtils.LayoutChunk.Length)`, `MVStoreTool.cs:451`, `TimeZoneProvider.cs:236,241`, `CompareModeDefault.cs:56`, `CacheLRU.cs:89` — холодные пути, править по случаю через `AsSpan`.
- **Ожидаемый эффект:** — (горячее снято).

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

### 2.4 [ЧАСТИЧНО] Data/H2Command.cs:474 — строковой IndexOf('$') остался

- **Проверено:** `H2ParameterRewriter.cs:44` уже `text.AsSpan().IndexOf('$')`, сканирования `'?'` нет — снимать.
- **Уязвимое место:** `src/H2Sharp/Data/H2Command.cs:474` `commandText.IndexOf('$')` — строковая перегрузка вместо спановой.
- **Современное решение:** `commandText.AsSpan().IndexOf('$')`; при желании `SearchValues.Create("$?")` не нужен — `'?'` уже не ищется.
- **Ожидаемый эффект:** Низкий (одна проверка на команду).

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

### 3.3 [ИСПРАВЛЕНО] Value/DataType.cs:184,327-328 — билдер уже освобождается

- **Проверено:** `TypesByName = typesByNameBuilder!.ToFrozenDictionary(StringComparer.Ordinal); typesByNameBuilder = null;`
- **Ожидаемый эффект:** — (снять).

### 3.4 НЕ трогать

- `Store/FileSystem/MemoryFilePath.cs:29`, `FilePath.cs:43`, `MvStore/SingleFileStore.cs:27`, `DateTimeFunction.cs:223` — мутабельные под `lock`/`ConcurrentDictionary`.
- `Command/Parser.cs:5384-5409` `Words*` — позиционный матчинг 1-4 токенов, `FrozenSet.Contains` бессмысленен.
- `*Function.Names[]`, `SetTypes/Comparison/Trace` — индекс→имя, диспетчеризация уже через `FunctionRegistry.Functions` (`Frozen`).

## 4. Скрытые аллокации / JIT / GC (все подтверждены чтением)

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

### 4.5 [ЧАСТИЧНО] ThrowIf* — H2Connection.cs:127 уже исправлен

- **Проверено:** `Data/H2Connection.cs:127` уже `ArgumentNullException.ThrowIfNull(connectionString)`. Паттерн также есть в `CommandContainer.cs:44`, `H2DataSourceBuilder.cs:28`.
- **Остаток (подтвержден):** `Data/H2Transaction.cs:92,101,125` `if(disposed) throw` → `ObjectDisposedException.ThrowIf(disposed, this)`; `JavaLegacy/JavaRandom.cs:85,101` → `ThrowIfNegative`; `MvStore/MVStoreTool.cs:64,90` `string.Format` → интерполяция + `InvariantCulture`.
- **Ожидаемый эффект:** Низкий-средний, суммарно заметный в ADO hot path.

## Приоритет внедрения (после перепроверки)

1. Критический: 4.1 (боксинг TransactionMap), 1.4 (ValueDataType Slice.ToArray — подтвержден `ValueDataType.cs:909,929,975`).
2. Высокий: 2.1, 2.2, 2.3, 3.1, 4.2, 4.4 (все подтверждены: `DataUtils.cs:870`, `DateTimeUtils.cs:942-945,972`, `Aggregate.cs:100`, `IndexCondition.cs:220,230`, `H2DataReader.cs:564`, `SessionLocal.cs:397,1614`, `Select.cs:731`).
3. Средний/низкий: 1.2-Space, 1.6-UrlDecode (`StringUtils.cs:1131,1160` подтвержден), 3.2, остаток 4.5, холодные `Substring` (`MVStore.cs:928`, `FileStore.cs:2039`).
4. Снято: 1.1, 1.2-hex, 1.3-бинарный, 1.5-горячий, 3.3, H2ParameterRewriter-двойной скан, H2Connection-ThrowIfNull.

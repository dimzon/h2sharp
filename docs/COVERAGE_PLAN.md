# План дальнейших работ по покрытию H2Sharp

Состояние на коммите `acdd0fa`: **92.66% строк (80424/86798)**, **88.81% ветвей
(33125/37298)**. Непокрыто **6374 строки в 393 файлах**.

## 1. Математика цели 95%

Для 95% строк нужно `0.95 × 86798 ≈ 82458` покрытых, то есть **+2034 строки** — примерно
треть всего остатка. Оставшиеся 2/3 — защитные, гоночные и коррупционные ветки, которые
без специального harness недостижимы. Поэтому план делится на волны с разной ценой строки.

## 2. Карта остатка

| Категория | Строк (оценка) | Характер | Инструмент |
|---|---|---|---|
| Прямые API-тесты (public/internal члены) | ~1100–1300 | низкий риск, детерминировано | xUnit + `InternalsVisibleTo` |
| SQL-поверхности (Parser, Tokenizer, Select, Query, Aggregate, функции) | ~1200–1500 | средний риск, много форм | SQL/скрипты, `EXPLAIN`, view-SQL |
| Fault/corruption (FileStore, Chunk, RandomAccessStore, SingleFileStore, Store) | ~400–500 | высокий риск, нужен harness | повреждённые файлы, MVStoreTool |
| Гонки/закрытые карты (Transaction, MVPrimaryIndex, TxDecisionMaker) | ~150 | практически недостижимо | не покрывать |
| TODO-заглушки непортированного | ~100 | это долги, не покрытие | не покрывать |
| Длинный хвост (389 файлов по 1–30 строк) | ~2300–2600 | смешанный, дешёвые точечные тесты | спринты по 30–60 строк |

Крупнейшие файлы (непокрыто/всего): `Parser.cs` 758/7229, `FileStore.cs` 197/1508,
`Select.cs` 108/1332, `TableTypes.cs` 97/1040, `MVMap.cs` 94/1170, `Page.cs` 93/983,
`ObjectDataType.cs` 92/894, `MVStore.cs` 83/1037, `Aggregate.cs` 77/1100,
`TransactionStore.cs` 63/603, `RandomAccessStore.cs` 62/591, `Tokenizer.cs` 61/1496,
`Transaction.cs` 58/431, `H2Connection.cs` 57/555, `Database.cs` 53/799,
`Query.cs` 49/632, `MVSecondaryIndex.cs` 49/290, `CompareLike.cs` 47/484,
`Schema.cs` 47/401, `TxDecisionMaker.cs` 47/184.

## 3. Волны

### Волна 1 — s185–s190: прямые юнит-спринты (уже в `PROMPT_COVERAGE_SPRINTS.md`)

| Спринт | Файл | Ожидание |
|---|---|---|
| s185 | `TransactionStore.cs` (auto-Init, CloseImmediately) | +4 |
| s186 | `H2Connection.cs` (форматы, UDF 2/3/4, GetSchema-гарды) | +20 |
| s187 | `Utils.cs` (IndexOf/CopyBytes/ParseBoolean/executor) | +12 |
| s188 | `EWKBUtils.cs` (короткие буферы) | +13 |
| s189 | `Page.cs` (память страницы, повторная запись) | +10 |
| s190 | `CompareLike.cs` + `Comparison.cs` (LIKE-формы) | +16 |

Итог волны: **≈ +75 строк**, 92.66% → ~92.74%.

### Волна 2 — s191–s200: прямые API (низкий риск)

| Спринт | Цель | Что покрывать | Ожидание |
|---|---|---|---|
| s191 | `ObjectDataType.cs` | round-trip каждого inner-типа (bool/byte/short/int/long/float/double/char/string/byte[]/UUID/BigInteger), `Compare` для byte[]/объектов, `CreateStorage` | +40–55 |
| s192 | `TableTypes.cs` | `IndexColumn`/`Column`/`Table`-аксессоры, `ValueRow`-конверсия (818–820), `SetKey` из значения (904–906), `GetTableType`/временность (1255+) | +35–45 |
| s193 | `MVSecondaryIndex.cs` | индексы по составным ключам, `Find`/`GetCost`/`NeedRebuild`, cursor-пути | +25–35 |
| s194 | `StringUtils.cs` + `WriteBuffer.cs` | парсеры имён/чисел, varint/varint-границы, big-endian чтение | +30–40 |
| s195 | `H2ValueMapper.cs` + `Value.Conversion.Families.cs` | CLR↔Value для всех семейств (даты, интервалы, decimal, binary) | +40–55 |
| s196 | `InformationSchemaTable.cs` + `MetaRecord.cs` | мета-строки (INDEXES, SETTINGS, USERS-без D-004), доступ к колонкам | +30–40 |
| s197 | `Aggregate.cs` | `MODE` с дубликатами (792–800), DISTINCT-агрегаты, ошибки типов | +25–35 |
| s198 | `ConstraintReferential.cs` | `ON DELETE/UPDATE CASCADE/SET NULL/SET DEFAULT/RESTRICT` на данных | +25–35 |
| s199 | `DateTimeFunction.cs` | `EXTRACT`, форматирование, границы дат | +20–30 |
| s200 | `MVStoreTool.cs` | дамп с битым footer, info на плохом block size, repair-fail | +15–25 |

Итог волны: **≈ +300–400**, 92.74% → **≈ 93.1–93.2%**.

### Волна 3 — s201–s215: Parser/Tokenizer и SQL-поверхности (главный резерв)

`Parser.cs` 758 + `Tokenizer.cs` 61 + `Select.cs` 108 + `Query.cs` 49 + `Aggregate.cs`
остаток — **≈ 1000 строк**. Это не «по строке», а по формам SQL. Порядок:

1. **s201–s203: DDL-формы Parser** — `CREATE INDEX ... WITH/ENGINE/COMMENT`,
   `ALTER TABLE ...` (RENAME COLUMN, SET DEFAULT, DROP EXPRESSION), `CREATE TABLE ...
   ENGINE`, `ON COMMIT`, `DEFAULT VALUES`, `CREATE SYNONYM`.
2. **s204–s206: DML/query-формы** — `MERGE ... USING` варианты, `UPDATE ... SET (a,b)=(...)`,
   `INSERT ... DEFAULT VALUES`, `WITH`/CTE-остатки, `TABLE(...)`.
3. **s207–s209: Tokenizer** — комментарии, dollar-quoted, юникод-идентификаторы,
   числа с экспонентой/подчёркиваниями, операторы.
4. **s210–s212: Select/Query** — `EXPLAIN`-формы, view-SQL (`VIEW_DEFINITION`),
   `FOR UPDATE`, `WITH TIES`, `OFFSET/FETCH` выражения, `DISTINCT ON`.
5. **s213–s215: скриптовая конформантность** — расширять `ScriptConformanceTests`
   новыми скриптами `reference/h2/h2/src/test/org/h2/test/scripts/**` (сейчас закреплено
   228 из 319; оставшиеся блокируют D-004/D-029/D-037 и XML-отклонения — брать только
   разблокированные).

Итог волны: **≈ +500–700**, → **≈ 93.7–94.0%**. Главный риск — каждая форма требует
чтения эталонного `Parser.java`; запрещено угадывать.

### Волна 4 — s216–s230: длинный хвост (389 файлов, 1–30 строк каждый)

Спринты по 2–4 файла за раз, критерий выбора — самые дешёвые по `uncovered.py`
(простые геттеры/ветки без внешних зависимостей). Примеры: `TableView.cs` 34,
`ExpressionColumn.cs` 35, `BinaryOperation.cs` 37, `ValueDataType.cs` 37,
`Database.cs` 53, `SessionLocal.cs` 39, `MVTable.cs` 44, `MVSecondaryIndex`-остатки,
`TableFilter.cs` 40, `ConstraintReferential`-остатки.

Итог волны: **≈ +700–900**, → **≈ 94.5–94.9%**.

### Волна 5 (опционально) — s231+: fault-injection harness

Только по отдельному решению: генератор повреждённых файлов на базе `MVStoreTool`
(дамп → порча байтов → открытие/repair) для `FileStore.cs` (197), `Chunk.cs` (41),
`RandomAccessStore.cs` (62), `SingleFileStore.cs` (38), `Store.cs` (42), плюс
часть `Page.cs`. Потенциал **+200–300**, риск высокий: тесты должны падать по коду
ошибки и не флапать.

## 4. Проекция

| После | Строк покрыто | Процент |
|---|---|---|
| сейчас | 80424 | 92.66% |
| волна 1 | ~80500 | ~92.74% |
| волна 2 | ~80850 | ~93.15% |
| волна 3 | ~81400 | ~93.78% |
| волна 4 | ~82200 | ~94.7% |
| волна 5 | ~82450+ | **95%+** |

Без волны 5 реалистичный потолок — **94.5–94.8%**; 95% достигается только вместе с
fault-injection или за счёт дополнительных SQL-форм сверх оценки.

## 5. Что не покрывать (зафиксировано)

- `Transaction.cs` 210–211, 227, 387–391, 408–412, 497–504, 520–527, 555–559, 583–590,
  622–625, 676–717, 744–774 — переполнение лога, гонки, фабрикация исключений.
- `MVPrimaryIndex.cs` 133–135, 207–213, 249–251, 277–279, 291–293, 448–463, 518–530,
  707–731 — гонки/закрытые карты/недостижимые DECFLOAT-ветки.
- `TxDecisionMaker.cs` большинство — race-пути.
- `TODOException`-заглушки непортированного (`Aggregate.createAggregateData` для
  непортированных типов, `Select.getRowCount`, `Schema.add/rename/remove` unported kinds,
  `MergeUsing.collectDependencies`) — это долги, а не цели покрытия.

## 6. Правила исполнения

Действуют все правила `docs/PROMPT_COVERAGE_SPRINTS.md`: Debug-only, полный пересъём
пяти наборов в новую `sNNN` (не смешивать сборки), гейт шести наборов
`Category!=BlockedByPort`, отчёт в `MIGRATION_STATUS.md`, коммит на русском, `git push`.
Один спринт = одна цель. Долг `Query.GetCost` (long vs double) не трогать — он записан
отдельным разделом и маркером `TODO(debt)`.

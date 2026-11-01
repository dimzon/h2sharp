# LOB-in-database: план реализации (отложен)

Статус: **отложен** по решению пользователя (2026-11-01). TODO в коде
(`ValueDataType.write: LOB in the database`, `ValueLob.copyToResult`) остаются
маркерами. Вопросы совместимости формата с эталоном — ниже.

## Зачем

Сейчас большие LOB всегда держатся в памяти (`LobDataInMemory`) или во
внешних файлах (`LobDataFile`); ветка «LOB в таблице базы» (`LobDataDatabase`)
недостижима. Эталон хранит большие LOB в MVMap-хранилище (LobStorageMap) с
привязкой к таблице и референс-счётчиком.

## Ориентир

`org.h2.mvstore.db.LobStorageMap`, `org.h2.store.LobStorageFrontend`,
`org.h2.value.lob.LobDataDatabase`, `org.h2.mvstore.StreamStore`.

## Этапы

1. **Формат и значение.** `LobDataDatabase` (handler/tableId/lobId, getInputStream,
   remove, isLinkedToTable); ветки `ValueDataType` для `-3` (BLOB:
   `varInt(-3)+varInt(tableId)+varLong(lobId)+varLong(octetLength)`;
   CLOB дополнительно `varLong(charLength)`); roundtrip-тесты формата.
2. **StreamStore.** Порт минимального `StreamStore` (блоки, префиксы длины,
   refcount), совместимого с форматом файлов эталона.
3. **LobStorageMap.** Функциональные методы: `CreateBlob/CreateClob`,
   `GetInputStream(lobId, tableId, byteCount)`, `CopyLob`, `RemoveLob`,
   `RemoveAllForTable` на четырёх картах (`lobMap`, `tempLobMap`, `refMap`,
   stream-данные); счётчик `nextLobId`; очередь отложенной очистки.
4. **Связка.** `DataHandler.GetLobStorage`/`ReadLob`; `Database` создаёт
   инстанс `LobStorageMap` к `MvStore`; фронтенд выбирает inplace/storage по
   `MaxLengthInplaceLob`.
5. **Интеграция В DML.** Вставка создаёт storage-значения с tableId; удаление
   строк → `RemoveLob`; дроп таблицы → `RemoveAllForTable`; `CopyToResult`
   получает реальную семантику.
6. **Оракул.** Дифференциальные тесты против jar: формат `-3`, поведение
   create/get/remove/copy, открытие файла, записанного эталоном.

## Риски

- Формат карт должен совпадать с эталонным, иначе файлы H2 не откроются;
  «своя» схема — отступление.
- Асинхронная очистка и Close/crash-семантика.
- Маршрутизация всех путей создания `ValueBlob/Clob` через фронтенд.

## Проверка

Каждый этап зелёный: Data/Engine/MvStore/core + оракул-тесты этапа; после
этапа 6 полный conformance-срез.

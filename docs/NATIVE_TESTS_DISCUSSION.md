# Обсуждение по родным тестам H2

Отдельный документ для вопросов, которые обсуждаются в конце забега по переносу родных
тестов (`tests/H2Sharp.Native.Tests`). Раньше этот список жил в `MIGRATION_STATUS.md`;
теперь он здесь, а в статусе — только ссылка.

Правила работы с этим списком:

* Каждый пункт — это **не** пропуск «навсегда», а зафиксированный вопрос: либо тест
  ждёт портовой работы (инструмент, сэм, поверхность), либо у его предмета нет
  ADO.NET-формы, и нужно решение (вычеркнуть, перенести иначе, оставить).
* Когда вопрос решён — пункт переносится в соответствующую запись статуса с пометкой
  решения, а отсюда удаляется.
* Решения «вне объёма» (как CSV) здесь не живут — они фиксируются сразу в статусе.

---

## Решение по объёму порта (2026-10-24)

* **Порт — исключительно встраиваемая БД.** Сетевых
возможностей (TCP-сервер, AUTO_SERVER, кластер, консоль) нет и не будет. Следствие для
родных тестов: тесты сервера и сети не переносятся — пакет `server/` (TestAutoServer,
TestInit, TestJakartaWeb, TestNestedLoop, TestWeb) и `TestCluster`; ветки
`config.networked` в перенесённых тестах остаются мёртвым кодом, как в эталоне.
`jdbcx/` (XA, ConnectionPool, DataSource — JEE-поверхность) — тоже вне объёма
встраиваемой БД, но в списке обсуждения ниже, чтобы решение было явным.

## Вопросы к обсуждению

* **TestZloty.testZloty — пропущен целиком.** Тест скармливает движку подклассы
  java.math.BigDecimal (с инвертированным compareTo и чужим toString) и ждёт
  INVALID_CLASS_2: H2 сериализует чужой класс как JAVA_OBJECT и отказывает ему в
  DECIMAL-параметре. В ADO.NET параметр несёт значение, а не класс: вопрос «чужой ли
  это класс» не существует, INVALID_CLASS_2 недостижим. Порт сечас примет подкласс как
  свой BigDecimal. Вопрос к обсуждению: (а) принять, что тест не имеет ADO.NET-формы, и
  вычеркнуть; (б) завести в провайдере явный отказ для типов-не-из-списка (это уже
  отступление); (в) ограничить параметр только известными типами значений.
  Для теста были сделаны: BigDecimal без sealed, публичный конструктор строки (как в
  JDK) и виртуальный CompareTo — сами по себе верны эталону (java.math.BigDecimal не
  final), но если тест вычёркиваем, их можно и вернуть (обсудить).
* **jdbcx/ (TestConnectionPool, TestDataSource, TestXA, TestXASimple) — не переносятся
  при решении «только embedded»?** javax.sql.DataSource и XA-транзакции — JEE-поверхность
  для серверов пулов; для встраиваемой БД потребителей у них нет. Предлагаю вычеркнуть
  вместе с server/ — обсудить.
* **TestOpenClose: testErrorMessageWrongSplit, testBackup и
  testBackupWithYoungDeadChunks — пропущены.** Первому нужна split-файловая система
  (`split:` URL и .part-файлы), вторым — BACKUP TO и инструмент Restore; всё это не
  перенесено. Остальное перенесено (блокировка файла, DB_CLOSE_DELAY, конкурентные
  вставки, переоткрытие с слушателем, старый файл 1.1 → FILE_VERSION_ERROR_1).
* **TestOutOfMemory — не перенесён.** Все три половины стоят на JVM-механике
  управляемого OOM: eatMemory/freeMemory (закладка кучи до отказа и восстановление),
  FilePathMem-регистрация поверх memFS, и ребёнок с `-Xmx128m`. В .NET управляемый OOM
  нельзя ни вызвать детерминированно внутри движка, ни восстановиться после него:
  OutOfMemoryException — не состояние, из которого процесс гарантированно выходит.
  Вопрос: пропустить целиком, или перенести testMVStoreUsingInMemoryFileSystem против
  MVStore-порта без инъекции OOM (проверка открытия/закрытия/переоткрытия).
* **TestMemoryUsage: testOpenCloseConnections — пропущен** — та же eatMemory-инъекция
  OOM. Остальное перенесено: замеры памяти (create index, CLOB при WRITE_DELAY=0),
  переоткрытия и цикл insert/update/select/delete с трассировкой.
* **TestCompatibilityOracle: updatable-половина testVarchar — пропущена.**
  updateString/updateNString/updateObject + updateRow на CONCUR_UPDATABLE курсоре — у
  читателя ADO.NET нет записи через курсор; .NET-вызыватель обновляет командой. Вопрос:
  перенести как отдельный тест «обновление через команду» или ждать updatable-поверхности.
* **TestFunctionOverload: meta.getProcedures-проверки — пропущены.** Провайдер пока
  не имеет поверхности DatabaseMetaData; сами алиасы и вызовы перенесены. Вопрос:
  перенести getProcedures на INFORMATION_SCHEMA.FUNCTION_ALIASES (там нет
  SPECIFIC_NAME как колонки — эталон собирает её из имени и номера) или ждать
  metadata-поверхности.
* **TestConnection: client-info и autocommit-runner'ы — пропущены.**
  setClientInfo/getClientInfo — JDBC-метаданные соединения (у ADO.NET их нет);
  commit/rollback-on-autocommit runner'ы крутятся вокруг JDBC-семантики autocommit и
  системного свойства FORCE_AUTOCOMMIT_OFF_ON_COMMIT. Остальное перенесено: изоляция
  через ADO-поверхность, SET SCHEMA/SCHEMA(), lock timeout, отказ неизвестных настроек,
  и testTimeZone — дефолтная таймзона JVM теперь четвёртый сэм IH2Test
  (TimeZoneProviderProvider, каждый сеанс под своей зоной; .NET не может менять
  TimeZoneInfo.Local процесса) — H2Connection, Engine.OpenSession и
  DelegatedCastDataProvider проводят его до сеанса; `;TIME ZONE=` в URL — как было.
* **TestIntPerfectHash — не перенесён** — построен на org.h2.util.IntPerfectHash (и
  util.BitArray), которых в порту нет. Перенести вместе с IntPerfectHash.
* **TestTraceSystem — не перенесён** — построен на org.h2.engine.TraceSystem (адаптивные
  уровни, запись в файлы trace.db); в порту есть только Trace/TraceWriter без системы.
  Перенести вместе с TraceSystem.
* **TestLocale — не перенесён** — меняет Locale.setDefault и ждёт турецко-чувствительного
  «i»→«I» и императорского японского календаря. Порт использует инвариантные
  преобразования (ToUpperInvariant/JavaCharacter.ToUpperCase), и сэма локали-как-в-JVM
  нет (в отличие от таймзоны). Перенести вместе с решением «какую культуру держать
  дефолтной» — вопрос в обсуждение.
* **TestMode — не перенесён** — проверяет supportedClientInfoPropertiesRegEx для семи
  режимов (DB2/Derby/HSQLDB/MSSQLServer/MySQL/Oracle/PostgreSQL); это та же JDBC
  client-info-поверхность, что отложена в TestConnection — перенести вместе с её
  решением.
* **TestAnsCompression — не перенесён** — построен на org.h2.util.AnsCompression +
  BitStream.Huffman/Out + BinaryArithmeticStream.Huffman/Out; в порту нет ни одного из
  них (compression-пакет — только CompressTool). Перенести вместе с ANS-компрессором.
* **TestMemoryEstimator — отложен** — строит TestDataType поверх MvStore-иерархии
  DataType<T> (BasicDataType<Integer>); переносим, но требует заведение тестового типа
  данных — сделать при очередном заходе в MvStore-пакет.
* **TestCsv — вне объёма (решение 2026-10-24).** Построен целиком на инструменте
  org.h2.tools.Csv; решено, что Csv-инструмент в порт не входит и не будет — CSV не
  покрываем. Тест вычеркнут из работы, не «ожидает».
* **TestFullText — вероятно не перенесён** — стоит на org.h2.fulltext (FullText.init,
  Lucene-половина); разбирать при переносе fulltext-инструмента.
* **TestBatchUpdates — не перенесён.** Предмет теста — семантика JDBC-batch: массивы
  int[]/long[] счётчиков из executeBatch, BatchUpdateException с отказами по элементам,
  clearBatch, SUCCESS_NO_INFO. В ADO.NET batch нет вовсе (перенесённые тесты пишут цикл
  выполнений), ни массивов, ни отчёта по элементам из этой семантики не остаётся.
  Вопрос: вычеркнуть (ADO-вызыватель пишет цикл и проверяет каждый результат сам) или
  перенести как цикл с теми же данными (coffee-таблица) — но это будет другой тест.
* **TestLobApi — не перенесён.** Целиком стоит на JDBC-модели LOB-объектов:
  conn.createBlob/createClob/createNClob, rs.getBlob/getClob (хэндлы, живущие после
  закрытия результата и коммита), Blob.setBytes/setBinaryStream/position, Clob.setString/
  getSubString, toString-рендеринги ('x', X'00', <closed>), FEATURE_NOT_SUPPORTED_1-отказы
  на position/truncate. В ADO.NET этой модели нет: параметр несёт byte[]/строку/Stream/
  TextReader, читатель материализует значение. Вопрос: вычеркнуть, или перенести
  содержательную половину (round-trip byte[]/Stream/TextReader на длинах 0/1/100/100000)
  как отдельный тест значений LOB.
* **TestStatement: JDBC-only поверхности — пропущены** (переносимая половина —
  executeUpdate-счётчики, execute-через RecordsAffected, сэйвпойнты по имени,
  REGEXP-подготовленный оператор поверх перестройки индекса, batch как цикл,
  multi-command через NextResult, рост trace-файла): unwrap/type-map,
  holdability/poolable/fetch-size/max-field-size, getMoreResults/getUpdateCount,
  warnings, closeOnCompletion, generated keys у merge (у провайдера нет поверхности
  generated keys — отдельный вопрос), и enquoteIdentifier/isSimpleIdentifier (хелперы
  кавычек JDBC Statement). executeQuery-отказы (METHOD_ONLY_ALLOWED_FOR_QUERY) в порту
  не выражаются: провайдер отвечает пустым update-count читателем вместо отказа.
* **TestNativeSQL — не перенесён.** Тест целиком про Connection.nativeSQL — JDBC-
  обработку escape-ов `{d '...'}`, `{fn ...}`, `{oj ...}`. В ADO.NET такой поверхности
  нет (параметры типизованы, escape-ов не существует), метода на H2Connection тоже нет.
  Вопрос: вычеркнуть (ADO.NET-вызыватель так не пишет), или перенести translateSQL
  как отдельный хелпер, если он когда-нибудь понадобится провайдеру.
* **TestCancel — не перенесён.** Стоит на поверхности отмены: Statement.cancel /
  setQueryTimeout / MAX_QUERY_TIMEOUT. В порту `H2Command.Cancel()` — пустая заглушка,
  CommandTimeout не связан с движком. Это портовая работа (SessionLocal.cancel, проверка
  в цикле выполнения, проводка CommandTimeout), тест перенесётся вслед за ней.
  Замечание: пустой Cancel() — заглушка, которая «отвечает», надо либо реализовать,
  либо бросать TODOException.
* **TestSQLXML — не перенесён.** Целиком про java.sql.SQLXML (createSQLXML,
  rs.getSQLXML, DOM/SAX/StAX Source/Result). В базовом ADO.NET поверхности нет
  (SQLXML живёт в System.Data.SqlTypes у SqlClient). Вопрос: вычеркнуть или перенести
  против GetTextReader/GetStream (что ADO-вызыватель и делает с CLOB).
* **TestOptimizations: testOptimizeInJoinSelect — пропущен** — алиас возвращает
  java.sql.ResultSet (org.h2.tools.SimpleResultSet); типа для C#-двойника нет, алиас
  остаётся Java-строкой как в эталоне, тест останавливается на резолве алиаса, пока
  алиасная машина не ответит. Остальные ~1200 строк перенесены: EXPLAIN-планы
  (index sorted, PRIMARY_KEY, covering index, OR→IN, константная конверсия '5'→5),
  rowid-оптимизации, query-cache (REUSE_RESULTS, timestamp, конкурентное
  использование, сброс параметров), min/max/count поверх Dictionary+SortedSet,
  IN-предикат на всех комбинациях ASC/DESC-индексов, NULLS FIRST/LAST,
  OPTIMIZE_REUSE_RESULTS-тайминги.
* **TestTransaction: фрагмент testNestedResultSets — смягчён.** Эталон переиспользует
  один Statement: второй executeQuery закрывает первый набор, и старый набор отвечает
  OBJECT_CLOSED. Команда ADO.NET отказывает второму открытому читателю вовсе
  (InvalidOperationException), поэтому фрагмент идёт двумя командами и проверка
  закрытого читателя выпала. Остальные ~1200 строк перенесены: уровни изоляции через
  BeginTransaction(level) с коммитом-перезаходом, сэйвпойнты по имени, FOR UPDATE
  (включая SKIP LOCKED/NOWAIT/WAIT), конкурентные batch-гонки UPDATE/MERGE USING/DELETE
  как циклы, INFORMATION_SCHEMA.SESSIONS.ISOLATION_LEVEL.
* **TestSequence: testConcurrentCreate — пропущен** — гоняет CREATE/DROP TRIGGER,
  чей класс реализует org.h2.api.Trigger; в порту триггеров нет. Перенести вместе с
  Trigger API.
* **TestPowerOff — не перенесён.** Стоит целиком на power-off сэме движка:
  Database.setInitialPowerOffCount, счётчики сессии (setPowerOffCount), memFS-поверх —
  в порту нет ни одного из них. Перенести вместе с сэмом (это портовая работа, не
  тестовая).
* **TestView: testSubQueryViewIndexCache — пропущен.** Интроспекция view-кэша индексов
  сессии (getSession().getViewIndexCache/clearViewIndexCache) — внутреннее состояние
  движка, у ADO-поверхности его нет. Остальной файл перенесён.
* **TestMultiDimension — не перенесён.** Тест целиком стоит на инструменте
  `org.h2.tools.MultiDimension` (getInstance/normalize/interleave/deinterleave/
  generatePreparedQuery/getResult), которого в порту нет; перенос инструмента — отдельная
  работа по порту, тест перенести вместе с ним.
* **TestTempTables: testTempFileResultSet и testTempTableResultSet — пропущены.**
  Обе половины гоняют TYPE_SCROLL_INSENSITIVE курсоры (beforeFirst и повторное чтение
  открытого набора). Читатель ADO.NET forward-only; .NET-вызыватель буферизует сам.
  Переносима часть без скролла (удержание temp-таблицы открытым читателем), скролл —
  нет. Вопрос: оставить пропущенным или перенести удержание-без-скролла как отдельный
  тест.
* **TestDriver.testDriverObject и testURLs — пропущены.** Суть — механика
  DriverManager: register/deregister, getDriver по URL, acceptsURL(null) → SQLException.
  Маппинг-предложение: Driver.Load/Unload регистрируют H2ProviderFactory в
  DbProviderFactories (инвариант H2Sharp). Не совпадает: getDriver по URL у реестра
  нет (инвариант вместо URL), «не зарегистрирован» отвечает своим исключением, а не
  SQLException; acceptsURL(null) с URL_FORMAT_ERROR_2 остаётся, но проверяется мимо
  реестра. Класс Driver перенесён и остаётся. Вопрос: принимаем ли маппинг
  DriverManager → DbProviderFactories как осознанное отступление (тогда тесты
  переписываются под него) или Driver остаётся без регистрации-теста.

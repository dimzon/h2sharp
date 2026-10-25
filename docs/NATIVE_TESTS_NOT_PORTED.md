# Реестр неперенесённых родных тестов H2

Документ отвечает на вопрос «что ещё не перенесено и почему». Он парный к
`docs/NATIVE_TESTS_DISCUSSION.md`: там — открытые вопросы и принятые решения, здесь —
полный перечень файлов из `reference/h2/h2/src/test/org/h2/test`, которых нет в
`tests/H2Sharp.Native.Tests`, с причиной для каждого.

Состояние на 2026-10-25: **перенесено 100 файлов из 300** (в счёт идут только
`*.java`, кроме `package-info`; `TestAll`, `TestBase`, `TestDb` — инфраструктура,
перенесённая в тестовый проект). Ниже — оставшиеся 200.

Категории:

* **A — вне объёма порта.** Сеть/сервер, инструменты, JVM-механики, примеры
  приложений. Решение зафиксировано в `docs/NATIVE_TESTS_DISCUSSION.md`
  («Решение по объёму порта»).
* **B — нужен порт движка.** Тест требует класса или функции, которых в
  `src/H2Sharp` нет; часть уже заведена issue.
* **C — JDBC-only поверхность.** У конструкции нет ADO.NET-формы (или решение по
  ней отложено до появления поверхности провайдера).
* **D — не тест, а вспомогательный файл** (раннер, helper, data-класс).
* **E — отложено решением** (дизайн провайдера или зависимость), см. дискуссию.

## db — 21 файл

| Файл | Кат. | Причина |
|---|---|---|
| `Db` | D | раннер/конфигурация TestAll, не тест |
| `TaskDef` | D | описание задачи для подпроцессного раннера |
| `TaskProcess` | D | подпроцессный раннер (JVM) |
| `TestBackup` | B | `BACKUP TO` и инструмент `Restore` не портированы |
| `TestCluster` | A | кластер/сеть |
| `TestCsv` | A | CSV-инструмент вне объёма |
| `TestDateStorage` | B | таймзона JVM (`TimeZone.setDefault`), `ValueTimestamp`, `TestDate` |
| `TestFullText` | B | пакет fulltext не портирован |
| `TestFunctions` | B | `SimpleResultSet`, `AggregateFunction`, алиасы на Java-исходниках, `ToCharFunction`/`ToDateParser`, Blob/Clob |
| `TestLinkedTable` | B | linked table API отсутствует |
| `TestLob` | B/C | JDBC-модель Blob/Clob; содержательная половина — `TestLobRoundTrip` |
| `TestLobObject` | D | helper-класс для `TestLob`, не тест |
| `TestMultiDimension` | B | `org.h2.tools.MultiDimension` не портирован |
| `TestOutOfMemory` | A/B | управляемый OOM JVM (`eatMemory`, `-Xmx`), решение — не переносить |
| `TestPowerOff` | B | power-off сэм движка отсутствует |
| `TestRunscript` | B | инструмент `RunScript` отсутствует |
| `TestSpatial` | B | зависимость JTS (Java Topology Suite) |
| `TestTableEngines` | B | API подключаемых table engines |
| `TestTriggersConstraints` | B | `Trigger` API отсутствует |
| `TestCluster` | A | см. выше |
| `TestAlter*`, `TestView*` и др. | — | перенесены |

## jdbc — 8 файлов

| Файл | Кат. | Причина |
|---|---|---|
| `TestBatchUpdates` | C | JDBC batch: `int[]` счётчиков, `getNextException`, `supportsBatchUpdates`; batch-как-цикл уже покрыт `TestStatement` |
| `TestCallableStatement` | C | `{call ...}` + `registerOutParameter`; решение отложено до output-параметров провайдера |
| `TestCancel` | B | отмена и query timeout не проведены в движок (работа запланирована) |
| `TestGetGeneratedKeys` | C | поверхности generated keys у `DbCommand` нет |
| `TestJavaObjectSerializer` | B | serializer-поверхности нет |
| `TestLobApi` | C | JDBC LOB-объекты; содержательная половина — `TestLobRoundTrip` |
| `TestMetaData` | C | `DatabaseMetaData` отсутствует |
| `TestNativeSQL` | C | JDBC escape-процессинг; вычеркнут решением |
| `TestResultSet` | C | scrollable/updatable половины; геттеры — `TestResultSetGetters` |
| `TestUpdatableResultSet` | C | updatable-курсор |
| `TestUrlJavaObjectSerializer` | B | serializer-поверхности нет |

## unit — 44 файла

| Файл | Кат. | Причина |
|---|---|---|
| `TestAnsCompression` | B | `AnsCompression`/`BitStream` отсутствуют |
| `TestAutoReconnect` | A | сетевой auto-reconnect |
| `TestBinaryArithmeticStream` | B | класса нет |
| `TestBitStream` | B | `BitStream` отсутствует |
| `TestBnf` | B | пакет `bnf` отсутствует |
| `TestCharsetCollator` | E | cp500 требует `CodePagesEncodingProvider`; решение по кодовым страницам не принято |
| `TestClassLoaderLeak` | A | JVM classloader/`WeakReference`; вычеркнут |
| `TestDate` | B | `TimeZone.setDefault`, `LegacyDateTimeUtils` |
| `TestDateTimeUtils` | B | `LegacyDateTimeUtils` отсутствует |
| `TestExit` | B/A | подпроцесс + `DatabaseEventListener` (#65) |
| `TestFileLock` | B | `FileLock` есть, но тест требует `TraceSystem` |
| `TestFileLockProcess` | A/B | подпроцессная блокировка файла |
| `TestFileSystem` | E/B | в порте только file/memFS/split; rec/zip/encrypt/async-схемы отсутствуют |
| `TestFtp` | A | FTP-сервер |
| `TestGeometryUtils` | B | JTS |
| `TestIntPerfectHash` | B | `IntPerfectHash` отсутствует |
| `TestJakartaServlet` | A | servlet-контейнер |
| `TestJmx` | A | JMX |
| `TestLocale` | E | сэма локали нет; решение по дефолтной культуре не принято |
| `TestMVTempResult` | A/B | `-Xmx`-механика |
| `TestMemoryEstimator` | B | `BasicDataType` (тестовый тип MvStore) отсутствует |
| `TestMemoryUnmapper` | A | JVM direct ByteBuffer unmapping |
| `TestMode` | C | JDBC client-info properties (та же поверхность, что в `TestConnection`) |
| `TestNetUtils` | A | сеть |
| `TestObjectDeserialization` | A | Java-сериализация; вычеркнут |
| `TestPageStoreCoverage` | A | PageStore вне объёма |
| `TestPerfectHash` | B | `PerfectHash` отсутствует |
| `TestPgServer` | A | протокол PostgreSQL-сервера |
| `TestRecovery` | B | инструмент `Recover` отсутствует |
| `TestReopen` | B | recording file system (`rec:`) отсутствует |
| `TestSampleApps` | A | примеры приложений |
| `TestScriptReader` | B | `ScriptReader` отсутствует |
| `TestSecurity` | B | пакет `security` отсутствует |
| `TestServlet` | A | servlet-контейнер |
| `TestShell` | A | интерактивная консоль |
| `TestSort` | B | `InPlaceStableMergeSort`/`InPlaceStableQuicksort` отсутствуют |
| `TestTimeStampWithTimeZone` | B | `LegacyDateTimeUtils`/`JSR310Utils` |
| `TestTools` | A/B | tools-пакет, сеть, `SimpleResultSet` |
| `TestTraceSystem` | E/B | `TraceSystem` отсутствует; решение — `System.Diagnostics` — не принято |
| `TestUpgrade` | A | загрузка другой версии сборки/jar |
| `TestValue` | B | `LegacyDateTimeUtils`, `DataHandler`, `ValueToObjectConverter2`, арифметика Modulus |
| `TestValueMemory` | B | `DataHandler`/`MemoryFootprint` |

## store — 28 файлов

Категория **B**: тесты самого MVStore-слоя. Порт содержит `MvStore`, но не
тест-вспомогательные и часть типов, на которые эти тесты опираются
(`BasicDataType`, `OffHeapStore`, `rtree`/`Spatial`, `DataUtils`, `MVStoreTool`,
`FilePathReorderWrites` и др.). Это отдельная партия работы по движку, а не
тестовая.

`CalculateHashConstant`, `CalculateHashConstantLong`, `FreeSpaceList`,
`FreeSpaceTree`, `RowDataType`, `SequenceMap`, `TestBenchmark`,
`TestCacheConcurrentLIRS`, `TestCacheLIRS`, `TestCacheLongKeyLIRS`,
`TestDataUtils`, `TestDefrag`, `TestFreeSpace`, `TestKillProcessWhileWriting`,
`TestMVRTree`, `TestMVStore`, `TestMVStoreBenchmark`,
`TestMVStoreCachePerformance`, `TestMVStoreConcurrent`,
`TestMVStoreStopCompact`, `TestMVStoreTool`, `TestMVTableEngine`,
`TestObjectDataType`, `TestRandomMapOps`, `TestShardedMap`, `TestSpinLock`,
`TestStreamStore`, `TestTransactionStore`.

## synth — 26 файлов

Категория **B/A**: генератор случайного SQL и crash/fuzz-тесты
(`BnfRandom`, `TestRandomSQL`, `TestCrashAPI`, `TestKill*`, `TestPowerOffFs*`,
`TestDiskFull`, `TestHalt*`, `TestJoin*`, `TestLimit`, `TestMultiThreaded`,
`TestOuterJoins`, `TestRandomCompare`, `TestReleaseSelectLock`,
`TestSimpleIndex`, `TestBtreeIndex`, `TestConcurrentUpdate`,
`TestFuzzOptimizations`, `TestThreads`, `TestTimer`, `OutputCatcher`, `sql/`,
`thread/`). Требуют BNF-генератора, подпроцессов и файловых сбоев; часть — вне
объёма встроенной БД.

## trace — 4 файла

Категория **E/B**: `TestTraceSystem`, `TestTrace`, `TestTraceLevel` и helper'ы
опираются на `TraceSystem`/`trace.db`. Решение «встроить в
`System.Diagnostics`» не принято (см. дискуссию).

## coverage — 3 файла, bench — 10 файлов

Категория **A/D**: покрытие PageStore (`TestPageStoreCoverage`,
`TestCoverage`) и бенчмарки (`TestBenchmark`, `TestMVStoreBenchmark` и др.).
Измерения производительности и покрытия не входят в набор корректности.

## server — 6 файлов, jdbcx — 5 файлов

Категория **A**: TCP/SSL-сервер, веб-консоль, авто-сервер; XA,
`ConnectionPool`, `DataSource` — JEE-поверхность. Решение зафиксировано.

## poweroff — 6 файлов, recover — 1 файл

Категория **B**: тесты power-off сэма и инструмента `Recover`; обоих в порте нет.

## utils — 8 файлов, scripts — 3 файла, auth — 2, ap — 1, todo — 5

Категория **D/A/B**:

* `utils/`: `FilePathDebug`, `FilePathReorderWrites`, `FilePathUnstable`,
  `MemoryFootprint`, `OutputCatcher`, `RandomDataUtils`, `ResultVerifier`,
  `SelfDestructor` — вспомогательные классы для других тестов, не тесты.
* `scripts/`: `TestScript`, `TestScriptGeneration`, `TestScriptReader` —
  обвязка скриптов; `ScriptReader` не портирован.
* `auth/TestAuthentication`, `auth/MyLoginModule` — пакет `security.auth` и
  `javax.security` отсутствуют (B).
* `ap/TestAnnotationProcessor` — JVM annotation processor (A).
* `todo/` — черновики/заготовки H2, не входят в набор (D).

## Что это значит для счёта

Из 200 оставшихся файлов:

* примерно 110 — вне объёма или helper'ы (A/D);
* примерно 60 — требуют порта движка/API (B), у части уже есть issue
  (#57–#65 и пункты в `MIGRATION_STATUS.md`);
* примерно 20 — JDBC-only поверхности (C);
* 3 — отложены решением (E: charset collation, locale, trace).

Ни один оставшийся файл не переносится «просто так» на текущем API: то, что
компилировалось, перенесено (см. `docs/NATIVE_TESTS_DISCUSSION.md`, вторая
партия), а остальное перечислено выше с причиной.

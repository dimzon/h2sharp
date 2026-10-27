# Отложенный спринт: .NET-расширения, DI и необязательные провайдеры

Статус: **реализация начата**: S0-каркас, RegisterUdf, RegisterHashAlgorithm,
BCL hashes, H2Sharp.Hashes (все алгоритмы CryptoHives вне BCL), H2Sharp.Crypto
(ENCRYPT/DECRYPT: AES/XTEA/FOG) и H2Sharp.JavaLegacy (JavaRandom + ORA_HASH). Остальные пункты — по TODO. В этот спринт объединены UDF, SQL aliases, триггеры,
trace/diagnostics, внешние hash-примитивы и совместимые функции вроде ORA_HASH.
Уточнение очередности пользователя: **все хэши, включая базовый HASH ядра,
выполняются позже**; текущий conformance продолжается с нехэшовыми функциями.

Обязательная архитектурная граница и правило сохранения соответствующего ей
кода — [ADR-001](ADR_DOTNET_EXTENSIONS.md). Решение принято; отложена реализация,
а не действие ADR. AGENTS.md ссылается на него для последующих сессий.

## 1. Архитектурная граница

Ядро исполняет H2 2.5: Prepared/Expression, Schema/DbObject, правила SQL,
преобразования Value, NULL, транзакции и metadata. Реализации расширений
поставляются .NET-объектами, делегатами и необязательными пакетами.

DI — способ создания этих объектов и управления сроком их жизни. SQL не
становится service locator; во время выполнения строки не сканируются assemblies
и не переинтерпретируются имена сервисов. Метаданные вызова/делегат готовятся при
регистрации или подготовке выражения, а не reflection на каждой строке.

Основные соответствия эталону:

- `schema.FunctionAlias`, `schema.UserDefinedFunction`, `function.JavaFunction`:
  .NET UDF и aliases размещаются в существующей структуре схемы и выражений;
- `command.ddl.CreateFunctionAlias`/`DropFunctionAlias`: SQL lifecycle alias;
- `api.Trigger`, `schema.TriggerObject`: lifecycle и точки вызова триггеров;
- `message.Trace`/`TraceSystem`/`TraceWriter`/`TraceWriterAdapter`: диагностика.

Платформенные .NET API и отклонения оформляются отдельно. Не вводится параллельная
структура SQL-объектов, конкурирующая со Schema/DbObject.

## 2. S0 — общий API конфигурации и DI

Основная точка настройки утверждена: **RegisterXXXX через эксплицитный
регистрационный интерфейс H2Connection только при Closed**, последовательность
«создали → настроили → открыли». DI и прочие builders — надстройки.
Пример — IH2Test; production registration имеет отдельный интерфейс и не
появляется в обычном IntelliSense connection/command.

RegisterUdf использует стандартные BCL Func-перегрузки: без аргументов
`Func<TResult>`, один аргумент `Func<TArgument, TResult>`, далее нужная арность.
Это реализации UDF, не DI-фабрики. Все перегрузки идут через общий механизм
проверки/регистрации; собственные публичные delegate-типы не вводятся.

- [ ] Определить конфигурацию расширений для базы/data source, без глобального
  изменяемого реестра, протекающего между независимыми базами.
- [ ] Реализовать общий guard состояния для всех RegisterXXXX: Closed/not disposed.
- [ ] Реализовать типизированные RegisterUdf Func-перегрузки и общий путь
  получения SQL metadata, без отдельной логики/ветвлений на каждую арность.
- [ ] Проверить create/register/open, отказ на открытом connection, Close/reopen
  и допустимую повторную настройку после Close без изменения активной сессии.
- [ ] Зафиксировать правила общей именованной базы при разных регистрациях
  нескольких connections; runtime-объекты и schema metadata не смешиваются.
- [ ] Ядру достаточно BCL `IServiceProvider`; удобные `IServiceCollection`
  extension methods поставляются отдельным DI-пакетом.
- [ ] Определить область жизни обработчиков: база, соединение/сессия, команда.
  Scoped зависимости не захватываются singleton-объектом базы.
- [ ] Определить политику дубликатов/переопределений имён, qualified schema names,
  обновления registry и инвалидации подготовленных команд.
- [ ] Регистрация пользователя и регистрация NuGet-пакета используют один API.

Приёмка: два data source/контейнера не делят обработчики; создание/освобождение
DI scope подтверждается тестами, включая ошибки и закрытие соединения.

## 3. S1 — криптографические примитивы через общий .NET API

- [ ] Единая точка выбора алгоритма в движке:
  **`Dictionary<string, Func<System.Security.Cryptography.HashAlgorithm>>`**.
- [ ] Фабрика создаёт новый экземпляр на операцию; изменяемое состояние digest
  не разделяется между параллельными запросами.
- [ ] Внутренний адаптер не является типом публичной фабрики. Возвращаемый объект
  используется как обычный HashAlgorithm: ComputeHash, streaming/TransformBlock,
  Initialize/reuse и Dispose.
- [ ] Основные алгоритмы обеспечиваются BCL. Новые алгоритмы могут регистрироваться
  внешним провайдером, без добавления switch/if в HASH.
- [ ] Согласовать имя, API и packaging регистрации алгоритмов, включая aliases
  алгоритмических имён и явное переопределение.

На проверенной Windows/.NET 10.0.12 BCL поддерживает MD5, SHA-1, SHA-256/384/512,
SHA3-256/384/512; SHA-224 и SHA3-224 отсутствуют. Нельзя подменять SHA-224
усечённым SHA-256. Доступность SHA3 BCL на другой платформе проверяется провайдером.

## 4. S2 — необязательные NuGet-провайдеры hashes

Кандидаты исследованы:

| Пакет | Наблюдения |
|---|---|
| [CryptoHives.Foundation.Security.Cryptography 0.7.37](https://www.nuget.org/packages/CryptoHives.Foundation.Security.Cryptography/0.7.37) | выбран; MIT; алгоритмы сразу являются `System.Security.Cryptography.HashAlgorithm`, без адаптера; SHA-224, SHA3-224, SHA-512/t, SHAKE/cSHAKE/TurboSHAKE, Keccak, BLAKE2/3, KT, ASCON, Kupyna, LSH, Streebog, RIPEMD-160, SM3, Whirlpool |
| [BouncyCastle.Cryptography 2.7.0](https://www.nuget.org/packages/BouncyCastle.Cryptography/2.7.0) | поддерживаемая библиотека, MIT, .NET 6/.NET Standard 2.0; требует адаптер digest → HashAlgorithm |
| [HashLib4CSharp 1.5.0](https://www.nuget.org/packages/HashLib4CSharp/1.5.0) | .NET Standard 2.1, широкий список hashes; последняя NuGet-публикация 2020 года |

Ядро не получает PackageReference на провайдера. `H2Sharp.Hashes` регистрирует
все fixed-output алгоритмы пакета, отсутствующие в BCL; `ParallelHash` — статический
one-shot API и в `HashAlgorithm`-registry не входит. Регистрация явная:
`UseH2Hashes()` на закрытом соединении; `UseH2Hashes(overrideBcl: true)` дополнительно
заменяет BCL-реализации MD5, SHA-1, SHA-2 и SHA3-256/384/512.

- [x] Пакет отсутствует: база и BCL HASH работают; вызов отсутствующего алгоритма
  даёт ясную SQL-ошибку с инструкцией, какой пакет и как подключить.
- [x] Пакет установлен/зарегистрирован: алгоритмы появляются без правок ядра.
- [x] Выбрать явный вызов регистрации или гарантированный startup integration.
  Выбран явный `UseH2Hashes()`; PackageReference сам по себе ничего не грузит.
- [ ] Проверить empty, Unicode bytes, длинные потоки, chunking, reuse, iterations
  и параллельность against JVM и известные криптографические векторы.

## 5. S3 — произвольные .NET UDF

- [ ] Объединить обычные FunctionName(arg, ...) builtins/UDF под общим
  readParameters и data-driven factory dispatcher: расширяется registry,
  parser не получает ветви на каждую функцию.
- [ ] Сохранить эталонные special-syntax readers, schema/alias precedence,
  arity и Expression/Prepared; не писать альтернативный SQL parser/AST.

- [ ] Реализация делегатами, static методами и DI-created instance methods.
- [ ] Проверить Func без аргументов и с несколькими аргументами; DI-надстройка
  получает instance handler и регистрирует его через тот же connection API.
- [ ] Установить typed mapping SQL Value ↔ .NET: числа, bool, string, byte[],
  temporal значения, nullable/SQL NULL и errors.
- [ ] Перегрузки, число аргументов/optional arguments/varargs соответствуют
  эталонным правилам FunctionAlias либо имеют явно утверждённое .NET-отличие.
- [ ] Deterministic/read-only metadata явно заданы. По умолчанию произвольный
  обработчик не считается deterministic и не вычисляется при constant folding.
- [ ] Ошибки .NET UDF сохраняют cause и проходят через SQL error surface.
- [ ] Вызов получает текущую сессию/соединение там, где это требуется, без
  открытия отдельной скрытой транзакции.
- [ ] Ссылки на конфигурируемые UDF восстанавливаются при открытии файловой базы;
  runtime-делегат или DI service instance не сериализуется как metadata.

Приёмка: scalar UDF с колонками/параметрами/NULL, qualified aliases, DI-зависимость,
недетерминированный счётчик, исключение и переподготовка команды после изменения.

### S3.1 — UUID через тот же механизм с default-реализацией

- [ ] UUID generation — заменяемая UDF/provider с default-реализацией обычной базы.
- [ ] Сохранить H2 2.5 SQL-имена, версии, тип результата и недетерминированность:
  генератор не constant-folded и не кэшируется как значение.
- [ ] Pre-Open регистрация позволяет внешний генератор; IH2Test UUID provider
  остаётся совместимым seam общего механизма.
- [ ] Проверить default/custom generation, несколько вызовов, ошибки и изоляцию
  connections, без правок исходных тестов без отдельной визы.

## 6. S4 — SQL aliases рядом с UDF

- [ ] Перенести CREATE/DROP ALIAS и FunctionAlias lifecycle из H2 2.5.
- [ ] Связать SQL alias со стабильным .NET-идентификатором зарегистрированной
  функции или метода; он не зависит от случайного assembly scanning.
- [ ] Проверить IF NOT EXISTS/IF EXISTS, schema, DETERMINISTIC, перегрузки,
  сохранение metadata и повторное открытие базы.
- [ ] Решение о runtime-компиляции .NET source отделено от вызова готового
  .NET-делегата; без него AS-source отказывает конкретным TODO.

## 7. S5 — ORA_HASH как обычная необязательная UDF

**ORA_HASH выведен из основного implementation scope** по указанию пользователя.
Он поставляется будущим пакетом на общем UDF API, без собственного пути
регистрации или отдельного плагинного фреймворка.

- [ ] Портировать формулу H2 2.5 HashFunction.oraHash в пакет UDF: UTF-8/bytes/LOB
  input, SHA-1, seed uint32 big-endian, старшие восемь bytes digest, снятие sign,
  modulo bucket+1. Не считать это обещанием бинарной совместимости с Oracle DB.
- [ ] Конструктор .NET HashAlgorithm остаётся фабрикой digest; ORA_HASH с
  bucket/seed и BIGINT-результатом — SQL UDF, не подмена этого интерфейса.
- [ ] Без пакета вызывается явная ошибка отсутствующей функции/провайдера;
  с пакетом все исходные ORA_HASH тесты проходят без изменения SQL/getter.

Существующие тесты ORA_HASH остаются в conformance и показывают отложенную
работу. Не удалять и не добавлять Skip/BlockedByPort без отдельного разрешения.

## 8. S6 — DI-триггеры

- [ ] Перенести интерфейс Trigger и schema.TriggerObject из H2 2.5; DI создаёт
  реализацию по регистрационному имени.
- [ ] CREATE/DROP TRIGGER связывают обработчик со Schema/Table metadata.
- [ ] Init/Fire/Close/Remove вызываются в эталонном порядке.
- [ ] BEFORE/AFTER и row/statement trigger, INSERT/UPDATE/DELETE/SELECT,
  oldRow/newRow и изменение newRow определяются H2, а не callback поверх SQL-текста.
- [ ] Ошибка отменяет операцию, работа с базой внутри Fire использует ту же
  транзакцию; rollback и закрытие проверяются отдельно.

## 9. S7 — trace/diagnostics через DI

- [ ] Подключаемый trace writer/sink создаётся через DI, сохраняя уровни,
  module names, errors и lifecycle TraceSystem/TraceWriter эталона.
- [ ] ILogger integration — отдельный необязательный adapter package.
- [ ] .NET ActivitySource/DiagnosticSource integration согласуется отдельным
  публичным API; SQL engine не зависит от конкретного logging framework.
- [ ] Без sink диагностика не ломает запрос; ошибка sink не меняет результат
  успешно выполненной операции. Closing/flush, concurrency и allocation cost
  проверяются тестами.

## 10. Контроль спринта

Каждый этап: чтение той же версии Java, согласованные .NET-отличия, отдельные
новые тесты, name-by-name regression/conformance сравнение и frozen checksums.
Diff старых тестов утверждается отдельно; plugins проверяются в настоящих
consumer-проектах с PackageReference и без него. Нет собственного crypto core,
SQL-дерева, схемного registry или regex translator вместо существующего эталона.

Сначала завершаются текущие conformance-пункты. Затем возвращаемся к S0/S1,
первый сквозной сценарий — DI → UDF → alias → ORA_HASH optional package.

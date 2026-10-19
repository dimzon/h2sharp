/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What a SELECT over built-in function calls answers, checked against the reference running the
 * same text. The parser's dispatcher (readFunction, readBuiltinFunctionIf,
 * readCompatibilityFunction) decides the expression, and both sides run the same statement over
 * the same rows, so a differing cell means the dispatcher took a different road.
 *
 * Not-ported pins sit beside the compared tests: a function whose branch is a TODO refuses by
 * name, and the pin asserts the refusal names what is missing instead of pretending the function
 * does not exist.
 */
using System.Collections.Generic;
using H2Sharp;
using H2Sharp.Api;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Message;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Db;
using H2Sharp.MvStore.Tx;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class BuiltinFunctionCallTests
{
    private static SessionLocal NewDualSession()
        => new(new Database(new ConnectionInfo("jdbc:h2:mem:select_dual")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };

    private static SessionLocal NewSeededSession(out TransactionStore store)
    {
        SessionLocal session = new SessionLocal(new Database(new ConnectionInfo("jdbc:h2:mem:func_text")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };

        store = new TransactionStore(MVStore.Open(null!));
        store.Init();

        Schemas.Schema schema = new(session.GetDatabase(), 1, "PUBLIC", false);
        session.GetDatabase().AddSchema(schema);

        Column id = new("ID", TypeInfo.TypeInteger, null, 0);
        id.SetNullable(false);
        Column name = new("NAME", TypeInfo.TypeVarchar, null, 1);
        MVTable table = new(session.GetDatabase(), schema, 0, "T", [id, name], persistIndexes: false, persistData: false);
        foreach (Column column in table.GetColumns())
        {
            column.SetTable(table, column.GetColumnId());
        }

        table.AddIndex(session, "I", 7, [new IndexColumn(id, ascending: true)], 1,
            IndexType.CreatePrimaryKey(persistent: false, hash: false), create: true, indexComment: null);
        table.AddRow(session, new DefaultRow([ValueInteger.Get(3), ValueVarchar.Get("c")]));
        table.AddRow(session, new DefaultRow([ValueInteger.Get(1), ValueVarchar.Get("b")]));
        table.AddRow(session, new DefaultRow([ValueInteger.Get(2), ValueVarchar.Get("b")]));
        table.AddRow(session, new DefaultRow([ValueInteger.Get(4), ValueNull.Instance]));
        schema.Add(table);
        return session;
    }

    private static List<string> RunText(SessionLocal session, string sql)
    {
        CommandContainer container = Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
        ResultInterface result = container.Query(-1);
        int visible = result.GetVisibleColumnCount();
        List<string> rows = [];
        while (result.Next())
        {
            Value?[] row = result.CurrentRow();
            List<string> cells = [];
            for (int i = 0; i < visible; i++)
            {
                Value? value = row[i];
                cells.Add(value is null or ValueNull ? "-" : value.GetString() ?? "-");
            }

            rows.Add("=" + string.Join(":", cells));
        }

        return rows;
    }

    private static List<string> RunJavaWithSetup(string dbName, string sql, string setup)
    {
        string head = """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:{DB}");
            java.sql.Statement stat = conn.createStatement();
            """.Replace("{DB}", dbName);
        string read = """
            java.sql.ResultSet rs = stat.executeQuery(inputs[0]);
            int columns = rs.getMetaData().getColumnCount();
            while (rs.next()) {
                StringBuilder line = new StringBuilder("=");
                for (int i = 1; i <= columns; i++) {
                    if (i > 1) {
                        line.append(':');
                    }
                    String s = rs.getString(i);
                    line.append(rs.wasNull() ? "-" : s);
                }
                System.out.println(line);
            }
            conn.close();
            """;
        return OracleRunner.Run(
            [sql],
            head + setup + read,
            numbers: null,
            extraJavaArgs: ["-Duser.language=en"]);
    }

    private static void CompareDual(string dbName, string sql, List<string>? expected = null)
    {
        SessionLocal session = NewDualSession();
        List<string> actual = RunText(session, sql);
        List<string> java = RunJavaWithSetup(dbName, sql, string.Empty);
        if (expected is not null)
        {
            Assert.Equal(expected, actual);
        }

        Assert.Equal(java, actual);
    }

    private static void CompareText(string dbName, string sql, List<string>? expected = null)
    {
        SessionLocal session = NewSeededSession(out TransactionStore store);
        try
        {
            List<string> actual = RunText(session, sql);
            List<string> java = RunJavaWithSetup(dbName, sql, """
                stat.execute("CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR)");
                stat.execute("INSERT INTO T VALUES (3, 'c'), (1, 'b'), (2, 'b'), (4, NULL)");
                """);
            if (expected is not null)
            {
                Assert.Equal(expected, actual);
            }

            Assert.Equal(java, actual);
        }
        finally
        {
            store.Dispose();
        }
    }

    private static SessionLocal NewDateTimeSession(out TransactionStore store)
    {
        SessionLocal session = new SessionLocal(new Database(new ConnectionInfo("jdbc:h2:mem:func_datetime")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };

        store = new TransactionStore(MVStore.Open(null!));
        store.Init();

        Schemas.Schema schema = new(session.GetDatabase(), 1, "PUBLIC", false);
        session.GetDatabase().AddSchema(schema);

        Column id = new("ID", TypeInfo.TypeInteger, null, 0);
        id.SetNullable(false);
        Column d = new("D", TypeInfo.TypeDate, null, 1);
        Column ts = new("TS", TypeInfo.TypeTimestamp, null, 2);
        Column tt = new("TT", TypeInfo.TypeTime, null, 3);
        Column i1 = new("I1", TypeInfo.TypeIntervalDayToSecond, null, 4);
        Column i2 = new("I2", TypeInfo.TypeIntervalYearToMonth, null, 5);
        MVTable table = new(session.GetDatabase(), schema, 0, "T", [id, d, ts, tt, i1, i2],
            persistIndexes: false, persistData: false);
        foreach (Column column in table.GetColumns())
        {
            column.SetTable(table, column.GetColumnId());
        }

        table.AddIndex(session, "I", 7, [new IndexColumn(id, ascending: true)], 1,
            IndexType.CreatePrimaryKey(persistent: false, hash: false), create: true, indexComment: null);
        table.AddRow(session, new DefaultRow([
            ValueInteger.Get(1),
            ValueDate.Parse("2004-02-29"),
            ValueTimestamp.Parse("2004-02-29 13:14:15.123456", session),
            ValueTime.Parse("13:14:15.123456", session),
            ValueInterval.From(IntervalQualifier.Day, false, 2, 0),
            ValueInterval.From(IntervalQualifier.YearToMonth, false, 1, 2),
        ]));
        table.AddRow(session, new DefaultRow([
            ValueInteger.Get(2),
            ValueDate.Parse("1999-12-31"),
            ValueTimestamp.Parse("1999-12-31 23:59:59.999999", session),
            ValueTime.Parse("00:00:00", session),
            ValueInterval.From(IntervalQualifier.Day, true, 1, 0),
            ValueInterval.From(IntervalQualifier.YearToMonth, true, 0, 5),
        ]));
        schema.Add(table);
        return session;
    }

    private static void CompareDateTime(string dbName, string sql)
    {
        SessionLocal session = NewDateTimeSession(out TransactionStore store);
        try
        {
            List<string> actual = RunText(session, sql);
            List<string> java = RunJavaWithSetup(dbName, sql, """
                stat.execute("CREATE TABLE T(ID INT PRIMARY KEY, D DATE, TS TIMESTAMP, TT TIME(6), "
                        + "I1 INTERVAL DAY, I2 INTERVAL YEAR TO MONTH)");
                stat.execute("INSERT INTO T VALUES (1, DATE '2004-02-29', "
                        + "TIMESTAMP '2004-02-29 13:14:15.123456', TIME '13:14:15.123456', "
                        + "INTERVAL '2' DAY, INTERVAL '1-2' YEAR TO MONTH)");
                stat.execute("INSERT INTO T VALUES (2, DATE '1999-12-31', "
                        + "TIMESTAMP '1999-12-31 23:59:59.999999', TIME '00:00:00', "
                        + "INTERVAL '-1' DAY, INTERVAL '-0-5' YEAR TO MONTH)");
                """);
            Assert.Equal(java, actual);
        }
        finally
        {
            store.Dispose();
        }
    }

    private static SessionLocal NewBinarySession(out TransactionStore store)
    {
        SessionLocal session = new SessionLocal(new Database(new ConnectionInfo("jdbc:h2:mem:func_binary")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };

        store = new TransactionStore(MVStore.Open(null!));
        store.Init();

        Schemas.Schema schema = new(session.GetDatabase(), 1, "PUBLIC", false);
        session.GetDatabase().AddSchema(schema);

        Column id = new("ID", TypeInfo.TypeInteger, null, 0);
        id.SetNullable(false);
        Column v = new("V", TypeInfo.TypeVarbinary, null, 1);
        Column w = new("W", TypeInfo.TypeVarbinary, null, 2);
        MVTable table = new(session.GetDatabase(), schema, 0, "B", [id, v, w],
            persistIndexes: false, persistData: false);
        foreach (Column column in table.GetColumns())
        {
            column.SetTable(table, column.GetColumnId());
        }

        table.AddIndex(session, "I", 7, [new IndexColumn(id, ascending: true)], 1,
            IndexType.CreatePrimaryKey(persistent: false, hash: false), create: true, indexComment: null);
        table.AddRow(session, new DefaultRow([
            ValueInteger.Get(1),
            ValueVarbinary.GetNoCopy([0x00, 0xff]),
            ValueVarbinary.GetNoCopy([0x0f, 0x0f]),
        ]));
        table.AddRow(session, new DefaultRow([
            ValueInteger.Get(2),
            ValueVarbinary.GetNoCopy([0x12, 0x34, 0x56, 0x78, 0x9a, 0xbc, 0xde, 0xf0]),
            ValueVarbinary.GetNoCopy([0x0f, 0x0f, 0x0f, 0x0f, 0x0f, 0x0f, 0x0f, 0x0f]),
        ]));
        schema.Add(table);
        return session;
    }

    private static void CompareBinary(string dbName, string sql)
    {
        SessionLocal session = NewBinarySession(out TransactionStore store);
        try
        {
            List<string> actual = RunText(session, sql);
            List<string> java = RunJavaWithSetup(dbName, sql, """
                stat.execute("CREATE TABLE B(ID INT PRIMARY KEY, V VARBINARY, W VARBINARY)");
                stat.execute("INSERT INTO B VALUES (1, X'00ff', X'0f0f'), "
                        + "(2, X'123456789abcdef0', X'0f0f0f0f0f0f0f0f')");
                """);
            Assert.Equal(java, actual);
        }
        finally
        {
            store.Dispose();
        }
    }

    private static void CompareRefusal(string dbName, string sql, int expectedCode)
    {
        SessionLocal session = NewDualSession();
        H2SharpException csharp = Assert.Throws<H2SharpException>(() => session.PrepareCommand(sql));
        List<string> java = OracleRunner.Run(
            [sql],
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:{DB}");
            try {
                java.sql.Statement stat = conn.createStatement();
                stat.executeQuery(inputs[0]);
                System.out.println("OK");
            } catch (java.sql.SQLException e) {
                System.out.println("ERR " + e.getErrorCode());
            }
            conn.close();
            """.Replace("{DB}", dbName),
            numbers: null,
            extraJavaArgs: ["-Duser.language=en"]);
        Assert.Equal(["ERR " + csharp.H2ErrorCode], java);
        Assert.Equal(expectedCode, csharp.H2ErrorCode);
    }

    [JavaOracleFact]
    public void AbsAndSignMatchReference()
    {
        CompareDual("func_abs", "SELECT ABS(-1), ABS(-1.5), ABS(1), SIGN(-3), SIGN(0), SIGN(3.5)");
    }

    [JavaOracleFact]
    public void ModFloorCeilMatchReference()
    {
        CompareDual(
            "func_mod",
            "SELECT MOD(10, 3), MOD(-10, 3), MOD(10, -3), FLOOR(1.9), FLOOR(-1.1), CEIL(1.1), CEIL(-1.9), CEILING(1.1)");
    }

    [JavaOracleFact]
    public void RoundRoundmagicTruncMatchReference()
    {
        CompareDual(
            "func_round",
            "SELECT ROUND(1.5), ROUND(2.5), ROUND(-1.5), ROUND(1.234, 2), ROUNDMAGIC(1.0), "
            + "TRUNC(1.9), TRUNC(-1.9), TRUNC(123.456, 1), TRUNCATE(1.9)");
    }

    [JavaOracleFact]
    public void TrigonometryMatchesReference()
    {
        CompareDual(
            "func_trig",
            "SELECT SIN(0), COS(0), TAN(0), ASIN(0), ACOS(1), ATAN(1), ATAN2(1, 1), COT(0.5), "
            + "SINH(0.5), COSH(0.5), TANH(0.5)");
    }

    [JavaOracleFact]
    public void LogExpSqrtPowerMatchReference()
    {
        CompareDual(
            "func_log",
            "SELECT LOG(100), LOG(100, 10), LOG10(1000), LN(1), EXP(0), SQRT(16), POWER(2, 10), POWER(2.0, 0.5)");
    }

    [JavaOracleFact]
    public void DegreesRadiansZeroPiMatchReference()
    {
        CompareDual("func_const", "SELECT DEGREES(PI()), RADIANS(180), PI(), ZERO()");
    }

    [JavaOracleFact]
    public void LengthFamilyMatchesReference()
    {
        CompareDual(
            "func_len",
            "SELECT LENGTH('abc'), CHAR_LENGTH('abc'), CHARACTER_LENGTH('abc'), OCTET_LENGTH('abc'), "
            + "BIT_LENGTH('abc'), LENGTH(NULL), LENGTH(''), LENGTH('ä')");
    }

    [JavaOracleFact]
    public void CoalesceFamilyMatchesReference()
    {
        CompareDual(
            "func_coalesce",
            "SELECT COALESCE(NULL, NULL, 3), COALESCE(1, 2), GREATEST(1, 3, 2), LEAST(1, 3, 2), "
            + "GREATEST(1, NULL, 2), LEAST(NULL, 2), GREATEST(NULL, NULL), NULLIF(1, 1), NULLIF(1, 2), "
            + "NULLIF(NULL, 1), IFNULL(NULL, 5), NVL(NULL, 6)");
    }

    [JavaOracleFact]
    public void ConcatMatchesReference()
    {
        CompareDual(
            "func_concat",
            "SELECT CONCAT('a', 'b'), CONCAT('a', NULL, 'b'), CONCAT_WS(',', 'a', 'b'), "
            + "CONCAT_WS('-', 'a', NULL, 'b')");
    }

    [JavaOracleFact]
    public void FunctionsOverRowsMatchReference()
    {
        CompareText(
            "func_rows",
            "SELECT ID, ABS(ID), LENGTH(NAME), COALESCE(NAME, 'z'), MOD(ID, 2) FROM T ORDER BY ID");
    }

    [JavaOracleFact]
    public void UnknownFunctionIsRefusedOnBothSides()
    {
        SessionLocal session = NewDualSession();
        H2SharpException csharp = Assert.Throws<H2SharpException>(
            () => session.PrepareCommand("SELECT NOSUCHFUNC(1)"));

        List<string> java = OracleRunner.Run(
            [],
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:func_none");
            try {
                java.sql.Statement stat = conn.createStatement();
                stat.executeQuery("SELECT NOSUCHFUNC(1)");
                System.out.println("OK");
            } catch (java.sql.SQLException e) {
                System.out.println("ERR " + e.getErrorCode());
            }
            conn.close();
            """,
            numbers: null,
            extraJavaArgs: ["-Duser.language=en"]);
        Assert.Equal(["ERR " + csharp.H2ErrorCode], java);
        Assert.Equal(90022, csharp.H2ErrorCode);
    }

    [JavaOracleFact]
    public void BitwiseFunctionsMatchReference()
    {
        CompareDual(
            "func_bitwise",
            "SELECT BITAND(5, 3), BITOR(5, 3), BITXOR(5, 3), BITNOT(5), BITNAND(5, 3), BITNOR(5, 3), "
            + "BITXNOR(5, 3), BITAND(5000000000, 3), BITOR(5000000000, 3), BITNOT(5000000000)");
    }

    [JavaOracleFact]
    public void BitShiftsRotatesGetAndCountMatchReference()
    {
        CompareDual(
            "func_bitops",
            "SELECT BITGET(5, 0), BITGET(5, 2), BITGET(5, 40), BITGET(5000000000, 35), BITCOUNT(255), "
            + "BITCOUNT(5000000000), LSHIFT(1, 4), LSHIFT(1, 64), LSHIFT(5000000000, 4), "
            + "RSHIFT(-16, 2), RSHIFT(-16, -2), ULSHIFT(-1, 1), URSHIFT(-16, 2), URSHIFT(5000000000, 2), "
            + "ROTATELEFT(1, 1), ROTATERIGHT(1, 1), ROTATELEFT(5000000000, 4), ROTATERIGHT(1, 65)");
    }

    [JavaOracleFact]
    public void BitNotRewritesMatchReference()
    {
        CompareDual(
            "func_bitnot_rewrites",
            "SELECT BITNOT(BITAND(5, 3)), BITNOT(BITOR(5, 3)), BITNOT(BITXOR(5, 3)), "
            + "BITNOT(BITNAND(5, 3)), BITNOT(BITNOR(5, 3)), BITNOT(BITXNOR(5, 3)), BITNOT(BITNOT(5))");
    }

    [JavaOracleFact]
    public void BitNotOverAggregateMatchesReference()
    {
        CompareText(
            "func_bitnot_aggregate",
            "SELECT BITNOT(BIT_AND_AGG(ID)), BITNOT(BIT_OR_AGG(ID)), BITNOT(BIT_XOR_AGG(ID)) FROM T");
    }

    [JavaOracleFact]
    public void BinaryBitFunctionsMatchReference()
    {
        CompareBinary(
            "func_bitwise_binary",
            "SELECT ID, RAWTOHEX(BITAND(V, W)), RAWTOHEX(BITOR(V, W)), RAWTOHEX(BITXOR(V, W)), "
            + "RAWTOHEX(BITNOT(V)), RAWTOHEX(BITNAND(V, W)), RAWTOHEX(BITNOR(V, W)), "
            + "RAWTOHEX(BITXNOR(V, W)), BITGET(V, 0), BITGET(V, 8), BITGET(V, 100), BITCOUNT(V), "
            + "RAWTOHEX(LSHIFT(V, 4)), RAWTOHEX(RSHIFT(V, -4)), RAWTOHEX(ULSHIFT(V, 4)), "
            + "RAWTOHEX(URSHIFT(V, 4)), RAWTOHEX(ROTATELEFT(V, 3)), RAWTOHEX(ROTATERIGHT(V, 3)), "
            + "RAWTOHEX(ROTATELEFT(V, 9)), RAWTOHEX(LSHIFT(V, 100)) FROM B ORDER BY ID");
    }

    [JavaOracleFact]
    public void StringFunctionsMatchReference()
    {
        CompareDual(
            "func_string",
            "SELECT LOCATE('b', 'abcabc'), LOCATE('b', 'abcabc', 3), LOCATE('b', 'abcabc', -1), "
            + "LOCATE('', 'abc'), LOCATE('', 'abc', 2), LOCATE('', 'abc', 5), LOCATE('', 'abc', -1), "
            + "LOCATE('', 'abc', -5), LOCATE('x', 'abc', 0), "
            + "LOCATE(NULL, 'abc'), LOCATE('a', NULL), LOCATE('a', 'abc', NULL), "
            + "INSERT('abcdef', 3, 2, 'XY'), INSERT('abcdef', 1, 0, 'XY'), INSERT('abcdef', 10, 2, 'XY'), "
            + "INSERT('abcdef', 3, 100, 'XY'), INSERT(NULL, 2, 1, 'X'), INSERT('abc', 2, 1, NULL), "
            + "REPLACE('abcabc', 'b', 'X'), REPLACE('abcabc', 'b'), REPLACE('abc', 'x', 'y'), "
            + "REPLACE('abc', 'b', NULL), REPLACE(NULL, 'b', 'x'), "
            + "LPAD('abc', 7, 'xy'), LPAD('abc', 7), LPAD('abcdef', 3, 'x'), LPAD('abc', -1, 'x'), "
            + "LPAD('abc', 5, NULL), RPAD('abc', 7, 'xy'), RPAD('abc', 2, 'x'), RPAD('abc', 0), "
            + "TRANSLATE('abcdef', 'cd', 'XY'), TRANSLATE('abcdef', 'cde', 'X'), TRANSLATE('abc', '', 'X'), "
            + "TRANSLATE(NULL, 'a', 'b'), TRANSLATE('abc', NULL, 'b'), TRANSLATE('abc', 'a', NULL)");
    }

    [JavaOracleFact]
    public void StringFunction1MatchReference()
    {
        CompareDual(
            "func_string1",
            "SELECT UPPER('aBc'), LOWER('AbC'), ASCII('A'), ASCII(''), CHAR(65), CHAR(9731), "
            + "STRINGENCODE('a b+c'), STRINGDECODE('a+b%2Bc'), STRINGDECODE(STRINGENCODE('a b')), "
            + "RAWTOHEX(STRINGTOUTF8('abc')), UTF8TOSTRING(STRINGTOUTF8('abc')), "
            + "HEXTORAW('00410042'), RAWTOHEX('AB'), RAWTOHEX(HEXTORAW('0041')), RAWTOHEX(X'00ff'), "
            + "SPACE(3), SPACE(0), SPACE(-1), QUOTE_IDENT('a\"b'), QUOTE_IDENT('simple')");
    }

    [JavaOracleFact]
    public void StringFunction2MatchReference()
    {
        CompareDual(
            "func_string2",
            "SELECT LEFT('abc', 2), LEFT('abc', 10), LEFT('abc', 0), LEFT('abc', -1), "
            + "RIGHT('abc', 2), RIGHT('abc', 10), RIGHT('abc', 0), RIGHT('abc', -1), "
            + "REPEAT('ab', 3), REPEAT('ab', 0), REPEAT('ab', -2), REPEAT('', 5)");
    }

    [JavaOracleFact]
    public void CompatibilityStringFunctionsMatchReference()
    {
        CompareDual(
            "func_string_compat",
            "SELECT INSTR('abcabc', 'b'), INSTR('abcabc', 'b', 3), INSTR('abc', ''), INSTR('', ''), "
            + "LCASE('AbC'), UCASE('aBc')");
    }

    [JavaOracleFact]
    public void InvalidHexAndBitArgumentsAreRefusedOnBothSides()
    {
        CompareRefusal("func_hex_odd", "SELECT HEXTORAW('ABC')", 22018);
        CompareRefusal("func_hex_bad", "SELECT HEXTORAW('zzzz')", 22018);
        CompareRefusal("func_bit_type", "SELECT BITAND(1.5, 1)", 90008);
    }

    [JavaOracleFact]
    public void ExtractDateFieldsMatchReference()
    {
        CompareDateTime(
            "func_extract_date",
            "SELECT ID, EXTRACT(YEAR FROM D), EXTRACT(MONTH FROM D), EXTRACT(DAY FROM D), "
            + "EXTRACT(QUARTER FROM D), EXTRACT(DAY_OF_YEAR FROM D), EXTRACT(ISO_DAY_OF_WEEK FROM D), "
            + "EXTRACT(ISO_WEEK FROM D), EXTRACT(ISO_YEAR FROM D) FROM T ORDER BY ID");
    }

    [JavaOracleFact]
    public void ExtractTimeFieldsMatchReference()
    {
        CompareDateTime(
            "func_extract_time",
            "SELECT ID, EXTRACT(HOUR FROM TS), EXTRACT(MINUTE FROM TS), EXTRACT(SECOND FROM TS), "
            + "EXTRACT(MILLISECOND FROM TS), EXTRACT(MICROSECOND FROM TS), EXTRACT(NANOSECOND FROM TS), "
            + "EXTRACT(CENTURY FROM D), EXTRACT(DECADE FROM D), EXTRACT(MILLENNIUM FROM D) FROM T ORDER BY ID");
    }

    [JavaOracleFact]
    public void CompatibilityFieldsMatchReference()
    {
        CompareDateTime(
            "func_extract_compat",
            "SELECT ID, YEAR(D), MONTH(D), DAY(D), HOUR(TS), MINUTE(TS), SECOND(TS), QUARTER(D), "
            + "ISO_DAY_OF_WEEK(D), ISO_WEEK(D), ISO_YEAR(D), DAY_OF_YEAR(D), DAYOFYEAR(D) FROM T ORDER BY ID");
    }

    [JavaOracleFact]
    public void ExtractEpochMatchesReference()
    {
        CompareDateTime(
            "func_extract_epoch",
            "SELECT ID, EXTRACT(EPOCH FROM TS), EXTRACT(EPOCH FROM D), EXTRACT(EPOCH FROM TT), "
            + "EXTRACT(EPOCH FROM I1), EXTRACT(EPOCH FROM I2) FROM T ORDER BY ID");
    }

    [JavaOracleFact]
    public void DateTruncDateFieldsMatchReference()
    {
        CompareDateTime(
            "func_trunc_date",
            "SELECT ID, DATE_TRUNC('YEAR', D), DATE_TRUNC('QUARTER', D), DATE_TRUNC('MONTH', D), "
            + "DATE_TRUNC('ISO_WEEK', D), DATE_TRUNC('ISO_WEEK_YEAR', D) FROM T ORDER BY ID");
    }

    [JavaOracleFact]
    public void DateTruncTimestampFieldsMatchReference()
    {
        CompareDateTime(
            "func_trunc_ts",
            "SELECT ID, DATE_TRUNC('DAY', TS), DATE_TRUNC('HOUR', TS), DATE_TRUNC('MINUTE', TS), "
            + "DATE_TRUNC('SECOND', TS), DATE_TRUNC('MILLISECOND', TS), DATE_TRUNC('MICROSECOND', TS), "
            + "DATE_TRUNC('CENTURY', D), DATE_TRUNC('DECADE', D), DATE_TRUNC('MILLENNIUM', D) FROM T ORDER BY ID");
    }

    [JavaOracleFact]
    public void DateaddMatchesReference()
    {
        CompareDateTime(
            "func_dateadd",
            "SELECT ID, DATEADD('MONTH', 1, D), DATEADD('YEAR', -1, D), DATEADD('QUARTER', 1, D), "
            + "DATEADD(DAY, 1, D), DATEADD('DAY', 1, TS), DATEADD('HOUR', 1, TS), "
            + "DATEADD('NANOSECOND', 1, TT), DATEADD('SECOND', 1, TT) FROM T ORDER BY ID");
    }

    [JavaOracleFact]
    public void DatediffMatchesReference()
    {
        CompareDateTime(
            "func_datediff",
            "SELECT ID, DATEDIFF('YEAR', D, DATEADD('YEAR', 1, D)), DATEDIFF('MONTH', D, DATEADD('MONTH', 1, D)), "
            + "DATEDIFF('DAY', D, DATEADD('DAY', 1, D)), DATEDIFF('HOUR', TS, DATEADD('HOUR', 1, TS)), "
            + "DATEDIFF('SECOND', TS, DATEADD('SECOND', 1, TS)), DATEDIFF(DAY, D, D) FROM T ORDER BY ID");
    }

    [JavaOracleFact]
    public void LastDayMatchesReference()
    {
        CompareDateTime(
            "func_last_day",
            "SELECT ID, LAST_DAY(D), LAST_DAY(TS) FROM T ORDER BY ID");
    }

    [JavaOracleFact]
    public void InvalidDateTimeFieldsAreRefusedOnBothSides()
    {
        CompareRefusal("func_field_invalid", "SELECT EXTRACT('NOPE' FROM 1)", 90008);
        CompareRefusal("func_field_invalid2", "SELECT DATE_TRUNC('NOPE', 1)", 90008);
        CompareRefusal("func_field_invalid3", "SELECT DATEADD(FOO, 1, 1)", 90008);
        CompareRefusal("func_field_quoted", "SELECT DATEADD(\"DAY\", 1, 1)", 42001);
    }

    [JavaOracleFact]
    public void SubstringFunctionsMatchReference()
    {
        CompareDual(
            "func_substring",
            "SELECT SUBSTRING('abcdef' FROM 2), SUBSTRING('abcdef' FROM 2 FOR 3), SUBSTRING('abcdef', 2), "
            + "SUBSTRING('abcdef', 2, 3), SUBSTRING('abcdef' FOR 3), SUBSTRING('abcdef' FROM -1), "
            + "SUBSTRING('abcdef' FROM 0 FOR 3), SUBSTRING('abcdef' FROM 3 FOR 100), "
            + "SUBSTRING('abcdef' FROM 20), SUBSTRING('abcdef' FROM 5 FOR 0), "
            + "SUBSTRING('abcdef' FROM -20 FOR 3), SUBSTRING(NULL FROM 1), SUBSTRING('abc' FROM NULL), "
            + "SUBSTRING('abc' FROM 1 FOR NULL), SUBSTR('abcdef', 2, 3), SUBSTR('abcdef' FROM 2 FOR 2)");
    }

    [JavaOracleFact]
    public void SubstringOfBinaryMatchesReference()
    {
        CompareBinary(
            "func_substring_binary",
            "SELECT ID, RAWTOHEX(SUBSTRING(V FROM 2)), RAWTOHEX(SUBSTRING(V, 1, 2)), "
            + "RAWTOHEX(SUBSTRING(V FROM -1)), RAWTOHEX(SUBSTRING(V FROM 0 FOR 2)), "
            + "RAWTOHEX(SUBSTRING(V FROM 3 FOR 100)), RAWTOHEX(SUBSTRING(V FROM 20)) FROM B ORDER BY ID");
    }

    [JavaOracleFact]
    public void TrimFunctionsMatchReference()
    {
        CompareDual(
            "func_trim",
            "SELECT TRIM('  ab  '), TRIM(LEADING 'x' FROM 'xxabxx'), TRIM(TRAILING 'x' FROM 'xxabxx'), "
            + "TRIM(BOTH 'x' FROM 'xxabxx'), TRIM('x' FROM 'xxabxx'), TRIM(FROM '  ab  '), "
            + "TRIM(BOTH FROM '  ab  '), TRIM(LEADING FROM '  ab  '), TRIM(TRAILING FROM '  ab  '), "
            + "TRIM('xxabxx', 'x'), TRIM('xxabxx', 'xy'), "
            + "LTRIM('xxabxx', 'x'), RTRIM('xxabxx', 'x'), BTRIM('xxabxx', 'x'), "
            + "LTRIM('  ab  '), RTRIM('  ab  '), BTRIM('  ab  '), TRIM(NULL), "
            + "TRIM('x' FROM NULL), TRIM(NULL FROM 'abc'), LTRIM(NULL, 'x'), RTRIM('abc', NULL)");
    }

    [JavaOracleFact]
    public void SoundexFunctionsMatchReference()
    {
        CompareDual(
            "func_soundex",
            "SELECT SOUNDEX('Robert'), SOUNDEX('Rupert'), SOUNDEX('Tymczak'), SOUNDEX('Pfister'), "
            + "SOUNDEX('Ashcraft'), SOUNDEX('aBc'), SOUNDEX(''), SOUNDEX(NULL), "
            + "DIFFERENCE('Robert', 'Rupert'), DIFFERENCE('abc', 'xyz'), DIFFERENCE(NULL, 'abc')");
    }

    [JavaOracleFact]
    public void DayMonthNamesMatchReference()
    {
        CompareDateTime(
            "func_day_month_name",
            "SELECT ID, DAYNAME(D), MONTHNAME(D), DAYNAME(TS), MONTHNAME(TS) FROM T ORDER BY ID");
    }

    [JavaOracleFact]
    public void DayMonthNamesBeforeEpochMatchReference()
    {
        CompareDateTime(
            "func_day_month_name_old",
            "SELECT ID, DAYNAME(DATEADD('YEAR', -70, D)), MONTHNAME(DATEADD('YEAR', -70, D)), "
            + "DAYNAME(DATEADD('YEAR', -35, D)), MONTHNAME(DATEADD('YEAR', -35, D)) FROM T ORDER BY ID");
    }

    [JavaOracleFact]
    public void RandomFunctionsMatchReference()
    {
        CompareDual(
            "func_random",
            "SELECT RAND(1), RAND(1), RAND(2), RAND(0), RAND(-1), RAND(2147483647), RAND(-2147483648), "
            + "RAND(NULL), RAND() >= 0, RAND() < 1, "
            + "OCTET_LENGTH(SECURE_RAND(0)), OCTET_LENGTH(SECURE_RAND(4)), OCTET_LENGTH(SECURE_RAND(-1)), "
            + "OCTET_LENGTH(RANDOM_UUID()), OCTET_LENGTH(RANDOM_UUID(4)), OCTET_LENGTH(UUID()), "
            + "OCTET_LENGTH(RANDOM_UUID(7)), CHAR_LENGTH(RAWTOHEX(RANDOM_UUID()))");
    }

    [JavaOracleFact]
    public void CompressFunctionsMatchReference()
    {
        CompareDual(
            "func_compress",
            "SELECT RAWTOHEX(COMPRESS('Hello, world! Hello, world!')), "
            + "RAWTOHEX(COMPRESS('Hello, world! Hello, world!', 'LZF')), "
            + "RAWTOHEX(COMPRESS('Hello, world! Hello, world!', 'NO')), "
            + "RAWTOHEX(COMPRESS('abc')), RAWTOHEX(COMPRESS('')), "
            + "RAWTOHEX(EXPAND(COMPRESS('abcdefghabcdefghabcdefghabcdefgh', 'DEFLATE'))), "
            + "RAWTOHEX(EXPAND(COMPRESS('abcdefghabcdefghabcdefghabcdefgh'))), "
            + "RAWTOHEX(EXPAND(COMPRESS('abcdefghabcdefghabcdefghabcdefgh', 'NO')))");
    }

    [JavaOracleFact]
    public void InvalidCompressionArgumentsAreRefusedOnBothSides()
    {
        CompareRefusal("func_compress_algorithm", "SELECT COMPRESS('abcdef', 'NOPE')", 90103);
        CompareRefusal("func_expand_empty", "SELECT EXPAND(X'')", 90104);
        CompareRefusal("func_expand_truncated", "SELECT EXPAND(X'0201')", 90104);
        CompareRefusal("func_expand_algorithm", "SELECT EXPAND(X'07')", 90103);
    }

    [Fact]
    public void WindowFunctionsWaitForOver()
    {
        SessionLocal session = NewDualSession();
        TODOException e = Assert.Throws<TODOException>(
            () => session.PrepareCommand("SELECT ROW_NUMBER()"));
        Assert.Contains("window functions", e.Message);
    }

    [Fact]
    public void RemainingKeywordFunctionsWaitForReadTerm()
    {
        SessionLocal session = NewDualSession();
        TODOException e = Assert.Throws<TODOException>(
            () => session.PrepareCommand("SELECT ROW(1)"));
        Assert.Contains("ROW", e.Message);
    }

    [Fact]
    public void AggregateOutsideSelectWaitsForReadAggregate()
    {
        SessionLocal session = NewSeededSession(out TransactionStore store);
        try
        {
            TODOException e = Assert.Throws<TODOException>(
                () => session.PrepareCommand("SELECT ID FROM T WHERE COUNT(*) > 1"));
            Assert.Contains("aggregate calls outside the select list", e.Message);
        }
        finally
        {
            store.Dispose();
        }
    }
}

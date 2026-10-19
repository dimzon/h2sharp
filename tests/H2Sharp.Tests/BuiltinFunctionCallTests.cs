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

    [Fact]
    public void BitFunctionsWaitForBitFunction()
    {
        SessionLocal session = NewDualSession();
        TODOException e = Assert.Throws<TODOException>(
            () => session.PrepareCommand("SELECT BITAND(1, 1)"));
        Assert.Contains("BitFunction", e.Message);
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

    [Fact]
    public void WindowFunctionsWaitForOver()
    {
        SessionLocal session = NewDualSession();
        TODOException e = Assert.Throws<TODOException>(
            () => session.PrepareCommand("SELECT ROW_NUMBER()"));
        Assert.Contains("window functions", e.Message);
    }

    [Fact]
    public void KeywordFunctionWaitsForReadTerm()
    {
        SessionLocal session = NewDualSession();
        TODOException e = Assert.Throws<TODOException>(
            () => session.PrepareCommand("SELECT LEFT('abc', 1)"));
        Assert.Contains("LEFT", e.Message);
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

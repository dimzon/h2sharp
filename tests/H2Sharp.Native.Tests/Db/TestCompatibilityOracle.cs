// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestCompatibilityOracle.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands; setString/setNString/setTimestamp
// are parameter values. The SimpleResultSet the reference builds for assertResult is the
// expected rows array itself — the comparison walks both sides row by row and renders
// each value the way the reference renders it (Java's Timestamp.toString for a DateTime,
// Arrays.toString for a byte[]). The java.sql.Types expectation of
// checkSequenceTypeWithMode is the declared type name the reader answers.
//
// Modified (deferred, see MIGRATION_STATUS.md): the updatable half of testVarchar
// (updateString/updateRow on a CONCUR_UPDATABLE result set) has no ADO.NET shape — the
// reader is read-only and a .NET caller updates through a command. The part is skipped;
// the parameter half of the test is ported. The reference's main() entry is the runner;
// xUnit runs Test() directly.
using System.Globalization;
using System.Linq;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Test Oracle compatibility mode.
/// </summary>
public sealed class TestCompatibilityOracle : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        TestNotNullSyntax();
        TestTreatEmptyStringsAsNull();
        TestDecimalScale();
        TestPoundSymbolInColumnName();
        TestToDate();
        TestSpecialTypes();
        TestDate();
        TestSequenceNextval();
        TestVarchar();
        DeleteDb("oracle");
    }

    private void TestNotNullSyntax()
    {
        DeleteDb("oracle");
        using DbConnection conn = GetConnection("oracle;MODE=Oracle");
        DbCommand stat = conn.CreateCommand();
        // Some other variation (oracle syntax)
        Execute(stat, "create table T (C int not null enable)");
        Execute(stat, "insert into T values(1)");
        Execute(stat, "drop table T");
        Execute(stat, "create table T (C int not null enable validate)");
        Execute(stat, "insert into T values(1)");
        Execute(stat, "drop table T");
        // can set NULL
        // can set NULL even with 'not null syntax' (oracle)
        Execute(stat, "create table T (C int not null disable)");
        Execute(stat, "insert into T values(null)");
        Execute(stat, "drop table T");
        // can set NULL even with 'not null syntax' (oracle)
        Execute(stat, "create table T (C int not null enable novalidate)");
        Execute(stat, "insert into T values(null)");
        Execute(stat, "drop table T");

        // Some other variation with oracle syntax
        Execute(stat, "create table T (C int not null)");
        Execute(stat, "insert into T values(1)");
        Execute(stat, "alter table T modify C not null");
        Execute(stat, "insert into T values(1)");
        Execute(stat, "alter table T modify C not null enable");
        Execute(stat, "insert into T values(1)");
        Execute(stat, "alter table T modify C not null enable validate");
        Execute(stat, "insert into T values(1)");
        Execute(stat, "drop table T");
        // can set NULL
        Execute(stat, "create table T (C int null)");
        Execute(stat, "insert into T values(null)");
        Execute(stat, "alter table T modify C null enable");
        Execute(stat, "alter table T modify C null enable validate");
        Execute(stat, "insert into T values(null)");
        // can set NULL even with 'not null syntax' (oracle)
        Execute(stat, "alter table T modify C not null disable");
        Execute(stat, "insert into T values(null)");
        // can set NULL even with 'not null syntax' (oracle)
        Execute(stat, "alter table T modify C not null enable novalidate");
        Execute(stat, "insert into T values(null)");
        Execute(stat, "drop table T");
    }

    private void TestSpecialTypes()
    {
        // Test VARCHAR, VARCHAR2 with CHAR and BYTE
        DeleteDb("oracle");
        using DbConnection conn = GetConnection("oracle;MODE=Oracle");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table T (ID NUMBER)");
        Execute(stat, "alter table T add A_1 VARCHAR(1)");
        Execute(stat, "alter table T add A_2 VARCHAR2(1)");
        Execute(stat, "alter table T add B_1 VARCHAR(1 byte)"); // with BYTE
        Execute(stat, "alter table T add B_2 VARCHAR2(1 byte)");
        Execute(stat, "alter table T add C_1 VARCHAR(1 char)"); // with CHAR
        Execute(stat, "alter table T add C_2 VARCHAR2(1 char)");
        Execute(stat, "alter table T add B_255 VARCHAR(255 byte)");
        Execute(stat, "alter table T add C_255 VARCHAR(255 char)");
        Execute(stat, "drop table T");
    }

    private void TestTreatEmptyStringsAsNull()
    {
        DeleteDb("oracle");
        using DbConnection conn = GetConnection("oracle;MODE=Oracle");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE A (ID NUMBER, X VARCHAR2(1))");
        Execute(stat, "INSERT INTO A VALUES (1, 'a')");
        Execute(stat, "INSERT INTO A VALUES (2, '')");
        Execute(stat, "INSERT INTO A VALUES (3, ' ')");
        AssertResult("3", stat, "SELECT COUNT(*) FROM A");
        AssertResult("1", stat, "SELECT COUNT(*) FROM A WHERE X IS NULL");
        AssertResult("2", stat, "SELECT COUNT(*) FROM A WHERE TRIM(X) IS NULL");
        AssertResult("0", stat, "SELECT COUNT(*) FROM A WHERE X = ''");
        AssertResult(new object?[][] { [1, "a"], [2, null], [3, " "] },
            stat, "SELECT * FROM A");
        AssertResult(new object?[][] { [1, "a"], [2, null], [3, null] },
            stat, "SELECT ID, TRIM(X) FROM A");

        Execute(stat, "CREATE TABLE B (ID NUMBER, X NUMBER)");
        Execute(stat, "INSERT INTO B VALUES (1, '5')");
        Execute(stat, "INSERT INTO B VALUES (2, '')");
        AssertResult("2", stat, "SELECT COUNT(*) FROM B");
        AssertResult("1", stat, "SELECT COUNT(*) FROM B WHERE X IS NULL");
        AssertResult("0", stat, "SELECT COUNT(*) FROM B WHERE X = ''");
        AssertResult(new object?[][] { [1, 5], [2, null] },
            stat, "SELECT * FROM B");

        Execute(stat, "CREATE TABLE C (ID NUMBER, X TIMESTAMP)");
        Execute(stat, "INSERT INTO C VALUES (1, '1979-11-12')");
        Execute(stat, "INSERT INTO C VALUES (2, '')");
        AssertResult("2", stat, "SELECT COUNT(*) FROM C");
        AssertResult("1", stat, "SELECT COUNT(*) FROM C WHERE X IS NULL");
        AssertResult("0", stat, "SELECT COUNT(*) FROM C WHERE X = ''");
        AssertResult(new object?[][] { [1, "1979-11-12 00:00:00.0"], [2, null] },
            stat, "SELECT * FROM C");

        Execute(stat, "CREATE TABLE D (ID NUMBER, X VARCHAR2(1))");
        Execute(stat, "INSERT INTO D VALUES (1, 'a')");
        Execute(stat, "SET @FOO = ''");
        Execute(stat, "INSERT INTO D VALUES (2, @FOO)");
        AssertResult("2", stat, "SELECT COUNT(*) FROM D");
        AssertResult("1", stat, "SELECT COUNT(*) FROM D WHERE X IS NULL");
        AssertResult("0", stat, "SELECT COUNT(*) FROM D WHERE X = ''");
        AssertResult(new object?[][] { [1, "a"], [2, null] },
            stat, "SELECT * FROM D");

        Execute(stat, "CREATE TABLE E (ID NUMBER, X RAW(1))");
        Execute(stat, "INSERT INTO E VALUES (1, HEXTORAW('0A'))");
        Execute(stat, "INSERT INTO E VALUES (2, '')");
        AssertResult("2", stat, "SELECT COUNT(*) FROM E");
        AssertResult("1", stat, "SELECT COUNT(*) FROM E WHERE X IS NULL");
        AssertResult("0", stat, "SELECT COUNT(*) FROM E WHERE X = ''");
        AssertResult(new object?[][] { [1, new byte[] { 10 }], [2, null] },
            stat, "SELECT * FROM E");

        Execute(stat, "CREATE TABLE F (ID NUMBER, X VARCHAR2(1))");
        Execute(stat, "INSERT INTO F VALUES (1, 'a')");
        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "INSERT INTO F VALUES (2, ?)";
            prep.Parameters.Add(new H2Parameter { Value = "" });
            _ = prep.ExecuteNonQuery();
        }

        AssertResult("2", stat, "SELECT COUNT(*) FROM F");
        AssertResult("1", stat, "SELECT COUNT(*) FROM F WHERE X IS NULL");
        AssertResult("0", stat, "SELECT COUNT(*) FROM F WHERE X = ''");
        AssertResult(new object?[][] { [1, "a"], [2, null] }, stat, "SELECT * FROM F");
    }

    private void TestDecimalScale()
    {
        DeleteDb("oracle");
        using DbConnection conn = GetConnection("oracle;MODE=Oracle");
        DbCommand stat = conn.CreateCommand();

        Execute(stat, "CREATE TABLE A (ID NUMBER, X DECIMAL(9,5))");
        Execute(stat, "INSERT INTO A VALUES (1, 2)");
        Execute(stat, "INSERT INTO A VALUES (2, 4.3)");
        Execute(stat, "INSERT INTO A VALUES (3, '6.78')");
        AssertResult("3", stat, "SELECT COUNT(*) FROM A");
        AssertResult(new object?[][] { [1, 2], [2, 4.3], [3, 6.78] },
            stat, "SELECT * FROM A");
    }

    /// <summary>
    /// Test the # in a column name for oracle compatibility.
    /// </summary>
    private void TestPoundSymbolInColumnName()
    {
        DeleteDb("oracle");
        using DbConnection conn = GetConnection("oracle;MODE=Oracle");
        DbCommand stat = conn.CreateCommand();

        Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY, U##NAME VARCHAR(255))");
        Execute(stat, "INSERT INTO TEST VALUES(1, 'Hello'), (2, 'HelloWorld'), (3, 'HelloWorldWorld')");

        AssertResult("1", stat, "SELECT ID FROM TEST where U##NAME ='Hello'");
    }

    private void TestToDate()
    {
        if (Config.Ci || !CultureInfo.CurrentCulture.TwoLetterISOLanguageName.Equals("en",
            StringComparison.Ordinal))
        {
            return;
        }

        DeleteDb("oracle");
        using DbConnection conn = GetConnection("oracle;MODE=Oracle");
        DbCommand stat = conn.CreateCommand();

        Execute(stat, "CREATE TABLE DATE_TABLE (ID NUMBER PRIMARY KEY, TEST_VAL TIMESTAMP)");
        Execute(stat, "INSERT INTO DATE_TABLE VALUES (1, "
            + "to_date('31-DEC-9999 23:59:59','DD-MON-RRRR HH24:MI:SS'))");
        Execute(stat, "INSERT INTO DATE_TABLE VALUES (2, "
            + "to_date('01-JAN-0001 00:00:00','DD-MON-RRRR HH24:MI:SS'))");

        AssertResultDate("9999-12-31T23:59:59", stat, "SELECT TEST_VAL FROM DATE_TABLE WHERE ID=1");
        AssertResultDate("0001-01-01T00:00:00", stat, "SELECT TEST_VAL FROM DATE_TABLE WHERE ID=2");
    }

    private void TestDate()
    {
        DeleteDb("oracle");
        using DbConnection conn = GetConnection("oracle;MODE=Oracle");
        DbCommand stat = conn.CreateCommand();

        DateTime t1 = DateTime.Parse("2011-02-03 12:11:10", CultureInfo.InvariantCulture);
        DateTime t2 = DateTime.Parse("1999-10-15 13:14:15", CultureInfo.InvariantCulture);
        DateTime t3 = DateTime.Parse("2030-11-22 11:22:33", CultureInfo.InvariantCulture);
        DateTime t4 = DateTime.Parse("2018-01-10 22:10:01", CultureInfo.InvariantCulture);

        Execute(stat, "CREATE TABLE TEST (ID INT PRIMARY KEY, D DATE)");
        Execute(stat, "INSERT INTO TEST VALUES(1, TIMESTAMP '2011-02-03 12:11:10.1')");
        Execute(stat, "INSERT INTO TEST VALUES(2, CAST ('1999-10-15 13:14:15.1' AS DATE))");
        Execute(stat, "INSERT INTO TEST VALUES(3, '2030-11-22 11:22:33.1')");
        using (DbCommand ps = conn.CreateCommand())
        {
            ps.CommandText = "INSERT INTO TEST VALUES (?, ?)";
            ps.Parameters.Add(new H2Parameter { Value = 4 });
            ps.Parameters.Add(new H2Parameter { Value = DateTime.Parse("2018-01-10 22:10:01.1", CultureInfo.InvariantCulture) });
            _ = ps.ExecuteNonQuery();
        }

        stat.CommandText = "SELECT D FROM TEST ORDER BY ID";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(t1, rs.GetDateTime(0));
            AssertTrue(rs.Read());
            AssertEquals(t2, rs.GetDateTime(0));
            AssertTrue(rs.Read());
            AssertEquals(t3, rs.GetDateTime(0));
            AssertTrue(rs.Read());
            AssertEquals(t4, rs.GetDateTime(0));
            AssertFalse(rs.Read());
        }
    }

    private void TestSequenceNextval()
    {
        // Test NEXTVAL without Oracle MODE should return BIGINT
        CheckSequenceTypeWithMode("REGULAR", "BIGINT", false);
        // Test NEXTVAL with Oracle MODE should return DECIMAL
        CheckSequenceTypeWithMode("Oracle", "NUMERIC", true);
    }

    private void CheckSequenceTypeWithMode(string mode, string expectedTypeName,
        bool usePseudoColumn)
    {
        DeleteDb("oracle");
        using DbConnection conn = GetConnection("oracle;MODE=" + mode);
        DbCommand stat = conn.CreateCommand();

        Execute(stat, "CREATE SEQUENCE seq");
        stat.CommandText = usePseudoColumn ? "SELECT seq.NEXTVAL FROM DUAL" : "VALUES NEXT VALUE FOR seq";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            // Check type: the reference compares the java.sql.Types code; the port names
            // the declared type instead.
            AssertEquals(expectedTypeName, rs.GetDataTypeName(0));
        }
    }

    private void TestVarchar()
    {
        DeleteDb("oracle");
        using DbConnection conn = GetConnection("oracle;MODE=Oracle");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY, V VARCHAR) AS VALUES (1, 'a')");
        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "UPDATE TEST SET V = ? WHERE ID = ?";
            prep.Parameters.Add(new H2Parameter());
            prep.Parameters.Add(new H2Parameter());
            prep.Parameters[1].Value = 1;
            prep.Parameters[0].Value = "";
            _ = prep.ExecuteNonQuery();
            stat.CommandText = "SELECT V FROM TEST";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertNull(rs.IsDBNull(0) ? null : rs.GetString(0));
                AssertFalse(rs.Read());
            }

            prep.Parameters[0].Value = "";
            _ = prep.ExecuteNonQuery();
            stat.CommandText = "SELECT V FROM TEST";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertNull(rs.IsDBNull(0) ? null : rs.GetString(0));
                AssertFalse(rs.Read());
            }
        }

        // The updatable result set half (updateString/updateRow) has no ADO.NET shape;
        // see the header and MIGRATION_STATUS.md.
        stat.CommandText = "SELECT V FROM TEST";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertNull(rs.IsDBNull(0) ? null : rs.GetString(0));
            AssertFalse(rs.Read());
        }
    }

    private void AssertResultDate(string expected, DbCommand stat, string sql)
    {
        stat.CommandText = sql;
        using (DbDataReader rs = stat.ExecuteReader())
        {
            if (rs.Read())
            {
                AssertEquals(DateTime.Parse(expected, CultureInfo.InvariantCulture),
                    rs.GetDateTime(0));
            }
            else
            {
                AssertEquals(expected, null);
            }
        }
    }

    private void AssertResult(object?[][] expectedRowsOfValues, DbCommand stat, string sql)
    {
        stat.CommandText = sql;
        using (DbDataReader actual = stat.ExecuteReader())
        {
            int expectedColumnCount = expectedRowsOfValues[0].Length;
            AssertEquals(expectedColumnCount, actual.FieldCount);
            int row = 0;
            while (true)
            {
                bool actualNext = actual.Read();
                bool expectedNext = row < expectedRowsOfValues.Length;
                if (!expectedNext && !actualNext)
                {
                    return;
                }

                if (expectedNext != actualNext)
                {
                    Fail("number of rows in actual and expected results sets does not match");
                }

                for (int i = 0; i < expectedColumnCount; i++)
                {
                    string? expectedString = ColumnResultToString(expectedRowsOfValues[row][i]);
                    string? actualString = ColumnResultToString(actual.IsDBNull(i)
                        ? null : actual.GetValue(i));
                    AssertEquals(expectedString, actualString);
                }

                row++;
            }
        }
    }

    /// <summary>
    /// The reference's columnResultToString: the text a value's toString answers, with
    /// Java's Timestamp and Arrays renderings for the types the provider maps differently.
    /// </summary>
    /// <param name="o">the value</param>
    /// <returns>the rendered value</returns>
    private static string? ColumnResultToString(object? o)
    {
        if (o is null)
        {
            return null;
        }

        if (o is byte[] bytes)
        {
            // java.util.Arrays.toString
            return "[" + string.Join(", ", bytes) + "]";
        }

        if (o is object?[] array)
        {
            return "[" + string.Join(", ", array.Select(o => ColumnResultToString(o))) + "]";
        }

        if (o is DateTime timestamp)
        {
            // java.sql.Timestamp.toString: yyyy-mm-dd hh:mm:ss.fffffffff, with at least
            // one fractional digit.
            string fraction = timestamp.ToString(".fffffff", CultureInfo.InvariantCulture)
                .TrimEnd('0');
            return timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                + (fraction.Length > 1 ? fraction : ".0");
        }

        return o.ToString();
    }
}

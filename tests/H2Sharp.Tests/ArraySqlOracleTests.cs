/* Copyright 2026 H2Sharp contributors. Licensed under MPL 2.0 or EPL 1.0. */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ArraySqlOracleTests
{
    [JavaOracleFact]
    public void ArrayDdlLiteralsAssignmentAndMetadataMatchJdbc()
    {
        string[] statements =
        [
            "CREATE TABLE T(ID INT, A INTEGER ARRAY[3], B VARCHAR(8) ARRAY, C INTEGER ARRAY ARRAY)",
            "INSERT INTO T VALUES (1, ARRAY[1, 2, 3], ARRAY['a', NULL, ''], ARRAY[ARRAY[1, 2], ARRAY[3]])",
            "INSERT INTO T VALUES (2, ARRAY[], ARRAY[], ARRAY[])",
            "INSERT INTO T VALUES (3, NULL, NULL, NULL)",
            "SELECT A, B, C FROM T ORDER BY ID",
            "SELECT COLUMN_NAME, DATA_TYPE, MAXIMUM_CARDINALITY FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'T' ORDER BY ORDINAL_POSITION",
            "SELECT ARRAY[1, 2.5, NULL], ARRAY[], CAST(1 AS INTEGER ARRAY)",
            "SELECT ARRAY_LENGTH(ARRAY[1, 2, 3]), CARDINALITY(ARRAY[])",
            "SELECT ARRAY_AGG(X) FROM SYSTEM_RANGE(1, 3)",
            "SELECT ARRAY[X, X + 1] FROM SYSTEM_RANGE(1, 3)",
            "UPDATE T SET A = ARRAY[7, 8] WHERE ID = 1",
            "SELECT A FROM T WHERE ID = 1",
            "INSERT INTO T(ID, A) VALUES (4, ARRAY[1, 2, 3, 4])",
            "INSERT INTO T(ID, A) VALUES (4, ARRAY['bad'])",
            "SELECT COUNT(*) FROM T",
            "CREATE TABLE BAD(A INTEGER ARRAY[65537])",
            "CREATE TABLE ZERO(A INTEGER ARRAY[0])",
            "INSERT INTO ZERO VALUES (ARRAY[])",
            "INSERT INTO ZERO VALUES (ARRAY[1])",
            "CREATE TABLE MULTI(A CHARACTER VARYING(10) ARRAY, B TIME(3) ARRAY, C TIMESTAMP(3) ARRAY)",
            "SELECT COLUMN_NAME, DATA_TYPE, MAXIMUM_CARDINALITY FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'MULTI' ORDER BY ORDINAL_POSITION",
        ];
        CompareStatements(statements);
    }

    private static void CompareStatements(string[] statements)
    {
        using H2Connection connection = Open();
        List<string> port = [];
        foreach (string sql in statements)
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            Record(command, port);
        }

        List<string> java = OracleRunner.Run(statements,
            JavaFormatter + """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:array_sql");
            for (String sql : inputs) {
                try (java.sql.Statement s = conn.createStatement()) {
                    if (s.execute(sql)) {
                        try (java.sql.ResultSet r = s.getResultSet()) {
                            int count = r.getMetaData().getColumnCount();
                            StringBuilder types = new StringBuilder("=T:");
                            for (int i = 1; i <= count; i++) {
                                if (i > 1) types.append('|');
                                types.append(r.getMetaData().getColumnTypeName(i));
                            }
                            System.out.println(types);
                            while (r.next()) {
                                StringBuilder line = new StringBuilder("=R:");
                                for (int i = 1; i <= count; i++) {
                                    if (i > 1) line.append('|');
                                    Object v = r.getObject(i);
                                    line.append(v == null ? "NULL" : Formatter.render(v));
                                }
                                System.out.println(line);
                            }
                        }
                    } else System.out.println("=U:" + s.getUpdateCount());
                } catch (java.sql.SQLException e) { System.out.println("=E:" + e.getErrorCode()); }
            }
            conn.close();
            """);
        Assert.True(java.SequenceEqual(port), "JVM:\n" + string.Join('\n', java) + "\nPort:\n" + string.Join('\n', port));
    }

    [JavaOracleFact]
    public void ArrayOperationsAndBoundsMatchJdbc()
    {
        CompareStatements([
            "SELECT (ARRAY[1, 2, 3])[2], ARRAY_GET(ARRAY[10, 20], 1)",
            "SELECT (ARRAY[ARRAY[1, 2], ARRAY[3, 4]])[2][1]",
            "SELECT ARRAY_GET(ARRAY[], 1)", "SELECT ARRAY_GET(ARRAY[1], 0)",
            "SELECT ARRAY_GET(ARRAY[1], -1)", "SELECT ARRAY_GET(ARRAY[1], 2)",
            "SELECT ARRAY_GET(CAST(NULL AS INT ARRAY), 1), ARRAY_GET(ARRAY[1], NULL)",
            "SELECT ARRAY[1, 2] || 3, 0 || ARRAY[1, 2], ARRAY[1] || ARRAY[2] || ARRAY[3]",
            "SELECT ARRAY_CAT(ARRAY[1], ARRAY[2]), ARRAY_APPEND(ARRAY[1], 2)",
            "SELECT ARRAY[1] || NULL, ARRAY_APPEND(ARRAY[1], NULL), ARRAY[] || ARRAY[]",
            "SELECT 'a' || 'b' || 'c', X'01' || X'02'",
            "SELECT ARRAY_CONTAINS(ARRAY[1, NULL, 2], NULL), ARRAY_CONTAINS(ARRAY[1], 2), ARRAY_CONTAINS(NULL, 1)",
            "SELECT TRIM_ARRAY(ARRAY[1, 2, 3], 0), TRIM_ARRAY(ARRAY[1, 2, 3], 3), TRIM_ARRAY(ARRAY[1, 2, 3], 1)",
            "SELECT TRIM_ARRAY(NULL, -1)", "SELECT TRIM_ARRAY(ARRAY[1], 2)",
            "SELECT ARRAY_SLICE(ARRAY[1, 2, 3], 1, 2), ARRAY_SLICE(ARRAY[1, 2, 3], 0, 2), ARRAY_SLICE(ARRAY[1, 2, 3], 2, 4)",
            "SELECT ARRAY_SLICE(ARRAY[1, 2, 3], 2, 1), ARRAY_SLICE(ARRAY[1, 2, 3], 3, 1)",
            "SELECT ARRAY[1, 2] = ARRAY[1, 2], ARRAY[1, 2] > ARRAY[1, 1], ARRAY[1, NULL] = ARRAY[1, NULL]",
            "SELECT ARRAY_MAX_CARDINALITY(CAST(ARRAY[1] AS INT ARRAY[5]))",
            "SELECT CAST((JSON '[10, 20]')[1] AS VARCHAR)",
            "SELECT CAST((JSON '[10, 20]')[3] AS VARCHAR)",
            "SELECT ARRAY_REMOVE(ARRAY[1, 2], 1)",
        ]);
    }

    [JavaOracleFact]
    public void UnnestTableConstructorAndArrayQueryMatchJdbc()
    {
        CompareStatements([
            "SELECT * FROM UNNEST(ARRAY[1, NULL, 3]) WITH ORDINALITY",
            "SELECT * FROM UNNEST(ARRAY[1, 2], ARRAY['a']) WITH ORDINALITY",
            "SELECT * FROM UNNEST(ARRAY[], CAST(NULL AS INT ARRAY))",
            "SELECT U.V, U.N FROM UNNEST(ARRAY['a', 'b']) WITH ORDINALITY AS U(V, N) ORDER BY N",
            "SELECT * FROM TABLE(V INT = ARRAY[1, 2], S VARCHAR = ARRAY['a'])",
            "SELECT * FROM TABLE_DISTINCT(V INT = ARRAY[1, 1, 2, NULL, NULL])",
            "SELECT * FROM TABLE(V INT = ('1', '2'))",
            "SELECT * FROM UNNEST()",
            "SELECT ARRAY(SELECT X FROM SYSTEM_RANGE(1, 3)) AS A",
            "SELECT ARRAY(SELECT X FROM SYSTEM_RANGE(1, 3) WHERE X > 10) AS A",
            "SELECT (ARRAY(SELECT X FROM SYSTEM_RANGE(1, 3)))[2]",
            "SELECT ARRAY(SELECT X, X FROM SYSTEM_RANGE(1, 3)) AS A",
            "CREATE TABLE T(ID INT, A INT ARRAY)",
            "INSERT INTO T VALUES (1, ARRAY[10, 20]), (2, ARRAY[30])",
            "SELECT ID, ARRAY(SELECT X FROM SYSTEM_RANGE(1, 3) WHERE X <= T.ID) AS A FROM T ORDER BY ID",
            "SELECT T.ID, U.C1 FROM T, UNNEST(T.A) U",
            "SELECT ARRAY(VALUES (1), (2), (3)) AS A",
            "SELECT ARRAY(SELECT 1 UNION ALL SELECT 2) AS A",
        ]);
    }

    [JavaOracleFact]
    public void QuantifiedArrayComparisonsAndElementAssignmentMatchJdbc()
    {
        CompareStatements([
            "SELECT 2 = ANY(ARRAY[1, 2]), 2 = SOME(ARRAY[1, 3]), 2 <> ALL(ARRAY[1, 3])",
            "SELECT NULL = ANY(ARRAY[]), NULL = ALL(ARRAY[]), 1 = ANY(NULL), 1 = ALL(NULL)",
            "SELECT 2 = ANY(ARRAY[1, NULL]), 2 <> ALL(ARRAY[1, NULL]), 2 = ANY(ARRAY[NULL, 2])",
            "SELECT NULL IS NOT DISTINCT FROM ANY(ARRAY[1, NULL]), NULL IS DISTINCT FROM ALL(ARRAY[1, NULL])",
            "SELECT NOT 1 = ANY(ARRAY[1]), NOT 1 = ALL(ARRAY[2])",
            "CREATE TABLE T(ID INT PRIMARY KEY, A INT ARRAY[5], B INT ARRAY ARRAY)",
            "INSERT INTO T VALUES (1, ARRAY[1, 2], ARRAY[ARRAY[1], ARRAY[2]]), (2, NULL, NULL)",
            "UPDATE T SET A[2] = 20 WHERE ID = 1",
            "UPDATE T SET A[5] = 50 WHERE ID = 1",
            "UPDATE T SET B[2][2] = 22 WHERE ID = 1",
            "SELECT A, B FROM T WHERE ID = 1",
            "UPDATE T SET A[6] = 60 WHERE ID = 1", "SELECT A FROM T WHERE ID = 1",
            "UPDATE T SET A[1] = 1 WHERE ID = 2", "SELECT A FROM T WHERE ID = 2",
            "SELECT ID FROM T WHERE ID = ANY(ARRAY[2, 1]) ORDER BY ID",
            "SELECT ID FROM T WHERE ID <> ALL(ARRAY[2])",
            "SELECT (1, 2) = ANY(ARRAY[ROW(1, 2), ROW(3, 4)])",
        ]);
    }

    [JavaOracleFact]
    public void ArrayAggregatesWithOrderingFilteringAndWindowFramesMatchJdbc()
    {
        CompareStatements([
            "CREATE TABLE T(ID INT, G INT, V INT)",
            "INSERT INTO T VALUES (1, 1, 20), (2, 1, NULL), (3, 1, 10), (4, 2, 30), (5, 2, 30)",
            "SELECT ARRAY_AGG(V ORDER BY V DESC NULLS LAST), ARRAY_AGG(DISTINCT V ORDER BY V) FROM T",
            "SELECT ARRAY_AGG(V) FILTER (WHERE ID < 4) FROM T",
            "SELECT ID, ARRAY_AGG(V) OVER () AS A FROM T ORDER BY ID",
            "SELECT ID, ARRAY_AGG(V) OVER (PARTITION BY G) AS A FROM T ORDER BY ID",
            "SELECT ID, ARRAY_AGG(V) OVER (ORDER BY ID ROWS BETWEEN 1 PRECEDING AND CURRENT ROW) AS A FROM T ORDER BY ID",
            "SELECT ID, ARRAY_AGG(V) OVER (ORDER BY V RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS A FROM T ORDER BY ID",
            "SELECT ID, ARRAY_AGG(V ORDER BY ID DESC) FILTER (WHERE V IS NOT NULL) OVER (PARTITION BY G) AS A FROM T ORDER BY ID",
            "SELECT ID, ARRAY_AGG(V) OVER (ORDER BY ID ROWS BETWEEN CURRENT ROW AND UNBOUNDED FOLLOWING) AS A FROM T ORDER BY ID",
            "SELECT ID, ARRAY_AGG(V) OVER (ORDER BY V GROUPS BETWEEN 1 PRECEDING AND 1 FOLLOWING EXCLUDE TIES) AS A FROM T ORDER BY ID",
            "SELECT ID, ARRAY_AGG(V) OVER (ORDER BY ID ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING EXCLUDE CURRENT ROW) AS A FROM T ORDER BY ID",
            "SELECT ID, ARRAY_AGG(V) OVER (ORDER BY ID RANGE BETWEEN 1 PRECEDING AND 1 FOLLOWING) AS A FROM T ORDER BY ID",
            "SELECT ID, ARRAY_AGG(V) OVER W AS A FROM T WINDOW W AS (PARTITION BY G) ORDER BY ID",
            "SELECT ARRAY_AGG(V) FROM T WHERE ID > 100",
            "SELECT ARRAY_AGG(V) OVER (ORDER BY ID ROWS BETWEEN 1 FOLLOWING AND 1 PRECEDING) AS A FROM T",
        ]);
    }

    [JavaOracleFact]
    public void ArrayAndEnumerableParametersMatchJdbcObjectArrays()
    {
        object[] inputs =
        [
            new[] { 1, 2, 3 }, new List<int> { 4, 5 }, Sequence(), Array.Empty<int>(),
            new int?[] { 1, null, 3 }, new int?[] { null, null },
            new string?[] { "a", null, "O'Brien" },
            new int[][] { [1, 2], [3] }, new byte[][] { [0, 128, 255], [] },
        ];
        string[] types = ["INTEGER", "INTEGER", "INTEGER", "INTEGER", "INTEGER", "INTEGER", "VARCHAR", "INTEGER ARRAY", "VARBINARY"];
        using H2Connection connection = Open();
        List<string> port = [];
        for (int i = 0; i < inputs.Length; i++)
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT CAST($a AS " + types[i] + " ARRAY)";
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = "a";
            parameter.Value = inputs[i];
            command.Parameters.Add(parameter);
            Record(command, port);
        }

        List<string> java = OracleRunner.Run(types,
            JavaFormatter + """
            Object[][] values = {
                {1, 2, 3}, {4, 5}, {6, 7}, {}, {1, null, 3}, {null, null},
                {"a", null, "O'Brien"}, {new Object[]{1, 2}, new Object[]{3}},
                {new byte[]{0, (byte)128, (byte)255}, new byte[]{}}
            };
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:array_params");
            for (int i = 0; i < values.length; i++) {
                try (java.sql.PreparedStatement s = conn.prepareStatement("SELECT CAST(? AS " + inputs[i] + " ARRAY)")) {
                    s.setObject(1, values[i]);
                    try (java.sql.ResultSet r = s.executeQuery()) {
                        System.out.println("=T:" + r.getMetaData().getColumnTypeName(1));
                        while (r.next()) System.out.println("=R:" + Formatter.render(r.getObject(1)));
                    }
                } catch (java.sql.SQLException e) { System.out.println("=E:" + e.getErrorCode()); }
            }
            conn.close();
            """);
        Assert.Equal(java, port);
    }

    [JavaOracleFact]
    public void Issue32NamedArrayParametersInAnyAndAllMatchJdbc()
    {
        object[] values = [new[] { 1, 2 }, new[] { 3 }, Array.Empty<int>(), new int?[] { 2, null },
            DBNull.Value, new List<int> { 1, 3 }];
        using H2Connection connection = Open();
        using DbCommand setup = connection.CreateCommand();
        setup.CommandText = "CREATE TABLE T(ID INT PRIMARY KEY)";
        setup.ExecuteNonQuery();
        setup.CommandText = "INSERT INTO T VALUES (1), (2), (3)";
        setup.ExecuteNonQuery();
        List<string> port = [];
        foreach (string op in new[] { "= ANY", "<> ALL" })
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT ID FROM T WHERE ID " + op + "($__ids_0) ORDER BY ID";
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = "__ids_0";
            parameter.DbType = DbType.Object;
            parameter.Value = values[0];
            command.Parameters.Add(parameter);
            command.Prepare();
            foreach (object value in values)
            {
                parameter.Value = value;
                Record(command, port);
            }
        }
        List<string> java = OracleRunner.Run([], """
            try (java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:issue32")) {
                conn.createStatement().execute("CREATE TABLE T(ID INT PRIMARY KEY)");
                conn.createStatement().execute("INSERT INTO T VALUES (1), (2), (3)");
                Object[][] values = { {1, 2}, {3}, {}, {2, null}, null, {1, 3} };
                for (String op : new String[]{"= ANY", "<> ALL"}) {
                    try (java.sql.PreparedStatement s = conn.prepareStatement("SELECT ID FROM T WHERE ID " + op + "(?) ORDER BY ID")) {
                        for (Object[] value : values) {
                            if (value == null) s.setNull(1, java.sql.Types.ARRAY);
                            else s.setObject(1, value);
                            try (java.sql.ResultSet r = s.executeQuery()) {
                                System.out.println("=T:" + r.getMetaData().getColumnTypeName(1));
                                while (r.next()) System.out.println("=R:" + r.getString(1));
                            }
                        }
                    }
                }
            }
            """);
        Assert.Equal(java, port);
    }

    [JavaOracleFact]
    public void RepeatedInAndAnyIndexScansStartFromTheFirstValueAgain()
    {
        CompareStatements([
            "CREATE TABLE T(ID INT PRIMARY KEY, N INT)",
            "INSERT INTO T VALUES (1, 10), (2, 20), (3, 30)",
            "SELECT ID FROM T WHERE ID IN (1, 2) ORDER BY ID",
            "SELECT ID FROM T WHERE ID IN (1, 2) ORDER BY ID",
            "SELECT ID FROM T WHERE ID IN (3) ORDER BY ID",
            "SELECT ID FROM T WHERE ID = ANY(ARRAY[1, 3]) ORDER BY ID",
            "SELECT ID FROM T WHERE ID = ANY(ARRAY[1, 3]) ORDER BY ID",
            "CREATE INDEX T_N ON T(N)",
            "SELECT ID FROM T WHERE N IN (10, 30) ORDER BY ID",
            "SELECT ID FROM T WHERE N IN (10, 30) ORDER BY ID",
            "SELECT ID FROM T WHERE N = ANY(ARRAY[20]) ORDER BY ID",
            "SELECT ID FROM T WHERE N = ANY(ARRAY[20]) ORDER BY ID",
        ]);
    }

    private const string JavaFormatter = """
        class Formatter {
        static String render(Object value) throws Exception {
            if (value == null) return "NULL";
            if (value instanceof java.sql.Array) return render(((java.sql.Array)value).getArray());
            if (value instanceof byte[]) return "0x" + java.util.HexFormat.of().formatHex((byte[])value);
            if (value instanceof Object[]) {
                StringBuilder b = new StringBuilder("[");
                Object[] elements = (Object[])value;
                for (int i = 0; i < elements.length; i++) {
                    if (i > 0) b.append(',');
                    b.append(render(elements[i]));
                }
                return b.append(']').toString();
            }
            if (value instanceof String) return "'" + value + "'";
            return value.toString();
        }
        }
        """;

    private static IEnumerable<int> Sequence()
    {
        yield return 6;
        yield return 7;
    }

    private static H2Connection Open()
    {
        H2Connection connection = new("Data Source=array_oracle_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        return connection;
    }

    private static void Record(DbCommand command, List<string> lines)
    {
        try
        {
            if (!command.CommandText.StartsWith("SELECT", StringComparison.Ordinal))
            {
                lines.Add("=U:" + command.ExecuteNonQuery().ToString(CultureInfo.InvariantCulture));
                return;
            }

            using DbDataReader reader = command.ExecuteReader();
            List<string> types = [];
            for (int i = 0; i < reader.FieldCount; i++) types.Add(reader.GetDataTypeName(i));
            lines.Add("=T:" + string.Join('|', types));
            while (reader.Read())
            {
                List<string> values = [];
                for (int i = 0; i < reader.FieldCount; i++) values.Add(Render(reader.GetValue(i)));
                lines.Add("=R:" + string.Join('|', values));
            }
        }
        catch (H2SharpException error)
        {
            lines.Add("=E:" + error.H2ErrorCode.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static string Render(object? value)
    {
        if (value is null or DBNull) return "NULL";
        if (value is bool boolean) return boolean ? "true" : "false";
        if (value is byte[] bytes) return "0x" + Convert.ToHexString(bytes).ToLowerInvariant();
        if (value is Array array)
        {
            List<string> elements = [];
            foreach (object? element in array) elements.Add(Render(element));
            return "[" + string.Join(',', elements) + "]";
        }

        if (value is string text) return "'" + text + "'";
        return Convert.ToString(value, CultureInfo.InvariantCulture)!;
    }
}

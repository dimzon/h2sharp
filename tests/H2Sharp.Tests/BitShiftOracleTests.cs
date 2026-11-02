/* Copyright 2026 H2Sharp contributors. Licensed under MPL 2.0 or EPL 1.0. */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// Shifts, rotates, BITGET and BITCOUNT over every integer width and over
/// binary strings, compared cell by cell with the reference.
/// </summary>
[Collection(OracleCollection.Name)]
public sealed class BitShiftOracleTests
{
    [JavaOracleFact]
    public void ShiftsOverSmallWidthsMatchJdbc()
    {
        CompareStatements([
            "SELECT LSHIFT(CAST(1 AS TINYINT), 2), RSHIFT(CAST(-8 AS TINYINT), 2)",
            "SELECT ULSHIFT(CAST(1 AS TINYINT), 2), URSHIFT(CAST(-8 AS TINYINT), 2)",
            "SELECT LSHIFT(CAST(1 AS TINYINT), 8), LSHIFT(CAST(1 AS TINYINT), -8)",
            "SELECT RSHIFT(CAST(-1 AS TINYINT), 9), URSHIFT(CAST(-1 AS TINYINT), 9)",
            "SELECT LSHIFT(CAST(1 AS SMALLINT), 8), RSHIFT(CAST(-512 AS SMALLINT), 3)",
            "SELECT ULSHIFT(CAST(3 AS SMALLINT), 4), URSHIFT(CAST(-512 AS SMALLINT), 3)",
            "SELECT LSHIFT(CAST(1 AS SMALLINT), 16), RSHIFT(CAST(-1 AS SMALLINT), 17)",
            "SELECT URSHIFT(CAST(-1 AS SMALLINT), 17), LSHIFT(CAST(1 AS SMALLINT), 0)",
            "SELECT LSHIFT(CAST(1 AS BIGINT), 40), RSHIFT(CAST(1099511627776 AS BIGINT), 40)",
            "SELECT URSHIFT(CAST(-1 AS BIGINT), 1), ULSHIFT(CAST(7 AS BIGINT), 60)",
            "SELECT LSHIFT(CAST(1 AS BIGINT), 64), RSHIFT(CAST(-1 AS BIGINT), 65)",
            "SELECT RSHIFT(CAST(16 AS INT), -9223372036854775808)",
            "SELECT ROTATELEFT(CAST(1 AS INT), 1), ROTATERIGHT(CAST(1 AS INT), 1)",
            "SELECT ROTATELEFT(CAST(1 AS BIGINT), 63), ROTATERIGHT(CAST(2 AS SMALLINT), 1)",
            "SELECT BITGET(CAST(5 AS TINYINT), 2), BITGET(CAST(5 AS TINYINT), 1)",
            "SELECT BITGET(CAST(5 AS SMALLINT), 0), BITGET(CAST(5 AS BIGINT), 63)",
            "SELECT BITGET(CAST(3 AS INT), -1), BITGET(CAST(3 AS INT), 100)",
            "SELECT BITCOUNT(CAST(7 AS TINYINT)), BITCOUNT(CAST(7 AS SMALLINT))",
            "SELECT BITCOUNT(CAST(-1 AS SMALLINT)), BITCOUNT(CAST(-1 AS BIGINT))",
            "SELECT BITCOUNT(CAST(0 AS BIGINT)), BITCOUNT(CAST(7 AS INT))",
        ]);
    }

    [JavaOracleFact]
    public void ShiftsOverBinaryMatchJdbc()
    {
        CompareStatements([
            "SELECT LSHIFT(X'0F', 4), RSHIFT(X'F0', 4)",
            "SELECT LSHIFT(X'0F3C', 4), RSHIFT(X'0F3C', 4)",
            "SELECT ULSHIFT(X'0F3C', 8), URSHIFT(X'F03C', 8)",
            "SELECT ROTATELEFT(X'0F3C', 4), ROTATERIGHT(X'0F3C', 4)",
            "SELECT LSHIFT(X'0F3C', 0), LSHIFT(X'', 4)",
            "SELECT BITGET(X'A0', 7), BITGET(X'A0', 0), BITGET(X'A0', 15)",
            "SELECT BITCOUNT(X'FF00'), BITCOUNT(X'')",
            "SELECT BITGET(CAST('AB' AS VARBINARY), 1), BITCOUNT(CAST('AB' AS VARBINARY))",
        ]);
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:bit_shift");
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

    private const string JavaFormatter = """
        class Formatter {
        static String render(Object value) throws Exception {
            if (value == null) return "NULL";
            if (value instanceof byte[]) return "0x" + java.util.HexFormat.of().formatHex((byte[])value);
            if (value instanceof String) return "'" + value + "'";
            return value.toString();
        }
        }
        """;

    private static H2Connection Open()
    {
        H2Connection connection = new("Data Source=bit_shift_oracle_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        return connection;
    }

    private static void Record(DbCommand command, List<string> lines)
    {
        try
        {
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
        return Convert.ToString(value, CultureInfo.InvariantCulture)!;
    }
}

/* Copyright 2026 H2Sharp contributors. Licensed under MPL 2.0 or EPL 1.0.
 * Datetime and interval arithmetic against the pinned H2 2.5 JDBC implementation. */
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class DateTimeArithmeticOracleTests
{
    [JavaOracleFact]
    public void DateTimeDifferencesAndExactHoursMatchJdbc()
    {
        CompareStatements([
            "CREATE TABLE T(ID INTEGER PRIMARY KEY, A TIMESTAMP(9), B TIMESTAMP(9))",
            "INSERT INTO T VALUES (1, TIMESTAMP '2024-02-10 09:00:00', TIMESTAMP '2024-05-30 12:00:00'), "
                + "(2, TIMESTAMP '2024-02-28 23:30:00', TIMESTAMP '2024-02-29 01:00:00'), "
                + "(3, TIMESTAMP '2024-03-01 00:00:00.000000001', TIMESTAMP '2024-02-29 23:59:59.999999999'), "
                + "(4, TIMESTAMP '2024-01-01 00:00:00', NULL)",
            "SELECT CAST(B - A AS VARCHAR), EXTRACT(EPOCH FROM (B - A)) / 3600 FROM T ORDER BY ID",
            "SELECT CAST(A - B AS VARCHAR) FROM T ORDER BY ID",
            "SELECT CAST(TIMESTAMP '2024-03-01 00:00:00' - TIMESTAMP '2024-03-01 00:00:00' AS VARCHAR)",
            "SELECT CAST(DATE '2024-03-01' - DATE '2024-02-28' AS VARCHAR)",
            "SELECT CAST(TIME '01:00:00.000000001' - TIME '23:30:00.000000002' AS VARCHAR)",
            "SELECT CAST(TIMESTAMP WITH TIME ZONE '2024-03-01 01:30:00+02:00' "
                + "- TIMESTAMP WITH TIME ZONE '2024-02-29 23:00:00+00:00' AS VARCHAR)",
            "SELECT CAST(TIME WITH TIME ZONE '01:00:00+02:00' - TIME WITH TIME ZONE '23:00:00-02:00' AS VARCHAR)",
            "SELECT CAST(TIMESTAMP '2024-03-01 01:00:00' - DATE '2024-02-29' AS VARCHAR)",
            "SELECT CAST((B - A) HOUR TO SECOND AS VARCHAR) FROM T ORDER BY ID",
            "SELECT CAST((TIMESTAMP '2024-03-31 12:00:00' - TIMESTAMP '2024-01-31 13:00:00') MONTH AS VARCHAR)",
            "SELECT CAST((TIMESTAMP '2024-01-31 13:00:00' - TIMESTAMP '2024-03-31 12:00:00') YEAR TO MONTH AS VARCHAR)",
            "SELECT CAST((TIMESTAMP '2024-03-01 00:00:01.9996' - TIMESTAMP '2024-03-01 00:00:00') SECOND(4, 3) AS VARCHAR)",
            "SELECT CAST((DATE '2024-05-30' - DATE '2024-02-10') DAY(2) AS VARCHAR)",
            "SELECT (1 - 2) DAY",
            "SELECT (DATE '2024-03-01' - 1) DAY",
            "SELECT CAST(TIMESTAMP '100000-03-01 00:00:00' - TIMESTAMP '-100000-03-01 00:00:00' AS VARCHAR)",
        ]);
    }

    [JavaOracleFact]
    public void IntervalArithmeticAndDateTimeAdditionMatchJdbc()
    {
        CompareStatements([
            "SELECT CAST(INTERVAL '1-2' YEAR TO MONTH + INTERVAL '3' MONTH AS VARCHAR)",
            "SELECT CAST(INTERVAL '1' DAY - INTERVAL '25' HOUR AS VARCHAR)",
            "SELECT CAST(INTERVAL '1.000000001' SECOND * 1.5 AS VARCHAR)",
            "SELECT CAST(1.5 * INTERVAL '1.000000001' SECOND AS VARCHAR)",
            "SELECT CAST(INTERVAL '3' HOUR / 2 AS VARCHAR)",
            "SELECT INTERVAL '3' HOUR / INTERVAL '90' MINUTE",
            "SELECT INTERVAL '1' YEAR / INTERVAL '3' MONTH",
            "SELECT CAST(INTERVAL '99999999999999999' DAY * 1.5 AS VARCHAR)",
            "SELECT INTERVAL '1' DAY + INTERVAL '1' MONTH",
            "SELECT INTERVAL '1' DAY / 0",
            "SELECT INTERVAL '1' DAY / 3",
            "SELECT CAST(TIMESTAMP '2024-02-29 23:30:00' + INTERVAL '1:30' HOUR TO MINUTE AS VARCHAR)",
            "SELECT CAST(INTERVAL '1:30' HOUR TO MINUTE + TIMESTAMP '2024-02-29 23:30:00' AS VARCHAR)",
            "SELECT CAST(TIMESTAMP '2024-03-01 00:30:00' - INTERVAL '1:30' HOUR TO MINUTE AS VARCHAR)",
            "SELECT CAST(DATE '2024-01-31' + INTERVAL '1' MONTH AS VARCHAR)",
            "SELECT CAST(DATE '2024-03-01' - INTERVAL '25' HOUR AS VARCHAR)",
            "SELECT CAST(TIME '23:00:00' + INTERVAL '30' MINUTE AS VARCHAR)",
            "SELECT TIME '23:00:00' + INTERVAL '2' HOUR",
            "SELECT TIME '00:30:00' - INTERVAL '1' HOUR",
            "SELECT TIME '12:00:00' + INTERVAL '1' YEAR",
            "SELECT CAST(TIME WITH TIME ZONE '23:00:00+02:00' + INTERVAL '30' MINUTE AS VARCHAR)",
            "SELECT CAST(TIMESTAMP WITH TIME ZONE '2024-02-29 23:30:00+02:00' + INTERVAL '1' HOUR AS VARCHAR)",
            "SELECT CAST(DATE '2024-02-29' + TIME '23:30:00' AS VARCHAR)",
            "SELECT CAST(TIME '23:30:00' + DATE '2024-02-29' AS VARCHAR)",
            "SELECT CAST(TIMESTAMP '2024-02-29 23:30:00' + TIME '01:00:00' AS VARCHAR)",
            "SELECT CAST(DATE '2024-02-29' + TIME WITH TIME ZONE '23:30:00+02:00' AS VARCHAR)",
            "SELECT DATE '2024-01-01' + DATE '2024-01-02'",
            "SELECT CAST(DATE '2024-02-28' + 2 AS VARCHAR), CAST(2 + DATE '2024-02-28' AS VARCHAR)",
            "SELECT CAST(TIMESTAMP '2024-02-29 23:30:00' + 0.0625 AS VARCHAR)",
            "SELECT CAST(TIMESTAMP '2024-03-01 01:00:00' - 0.0625 AS VARCHAR)",
            "SELECT CAST(TIMESTAMP '2024-03-01 00:00:00' + 0.00001736112 AS VARCHAR)",
            "SELECT CAST(TIMESTAMP '2024-03-01 00:00:00' - 0.00001736112 AS VARCHAR)",
            "SELECT CAST(TIME '02:00:00' * 2 AS VARCHAR), CAST(2 * TIME '02:00:00' AS VARCHAR), CAST(TIME '02:00:00' / 2 AS VARCHAR)",
            "SELECT CAST(INTERVAL - '1.9996' SECOND(4, 3) AS VARCHAR)",
            "SELECT INTERVAL 'bad' DAY",
            "SELECT INTERVAL '1'",
            "SELECT INTERVAL '1' DAY(0)",
            "SELECT INTERVAL '1' SECOND(4, 10)",
            "CREATE TABLE I(ID INT PRIMARY KEY, D TIMESTAMP(9), V INTERVAL DAY(9) TO SECOND(9))",
            "INSERT INTO I VALUES (1, TIMESTAMP '2024-02-29 23:30:00', INTERVAL '0 01:30:00.000000001' DAY TO SECOND), "
                + "(2, TIMESTAMP '2024-02-29 23:30:00', INTERVAL '-0 01:30:00.000000001' DAY TO SECOND), "
                + "(3, NULL, NULL)",
            "SELECT CAST(D + V AS VARCHAR), CAST(D - V AS VARCHAR), CAST(V * 1.5 AS VARCHAR) FROM I ORDER BY ID",
            "SELECT CAST(V + V AS VARCHAR), CAST(V / 2 AS VARCHAR) FROM I ORDER BY ID",
            "SELECT CAST(INTERVAL '1' YEAR AS VARCHAR), CAST(INTERVAL '1' MONTH AS VARCHAR), CAST(INTERVAL '1' DAY AS VARCHAR), "
                + "CAST(INTERVAL '1' HOUR AS VARCHAR), CAST(INTERVAL '1' MINUTE AS VARCHAR), CAST(INTERVAL '1.123456789' SECOND AS VARCHAR)",
            "SELECT CAST(INTERVAL '1-2' YEAR TO MONTH AS VARCHAR), CAST(INTERVAL '1 02' DAY TO HOUR AS VARCHAR), "
                + "CAST(INTERVAL '1 02:03' DAY TO MINUTE AS VARCHAR), CAST(INTERVAL '1 02:03:04.123456789' DAY TO SECOND AS VARCHAR), "
                + "CAST(INTERVAL '1:02' HOUR TO MINUTE AS VARCHAR), CAST(INTERVAL '1:02:03.123456789' HOUR TO SECOND AS VARCHAR), "
                + "CAST(INTERVAL '1:02.123456789' MINUTE TO SECOND AS VARCHAR)",
        ]);
    }

    [JavaOracleFact]
    public void BoundTimestampDifferencesKeepTheirTypesAndCanBeRebound()
    {
        const string Sql = "SELECT CAST(? AS TIMESTAMP(9)) - CAST(? AS TIMESTAMP(9)), "
            + "EXTRACT(EPOCH FROM (CAST(? AS TIMESTAMP(9)) - CAST(? AS TIMESTAMP(9)))) / 3600";
        string[] inputs = ["2024-02-29 01:00:00", "2024-02-28 23:30:00",
            "2024-02-28 23:30:00", "2024-02-29 01:00:00", OracleRunner.NullMarker, "2024-02-29 01:00:00"];
        using H2Connection connection = new("Data Source=datetime_parameters_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = Sql;
        for (int i = 0; i < 4; i++)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.DbType = DbType.String;
            parameter.Value = inputs[i % 2];
            command.Parameters.Add(parameter);
        }
        command.Prepare();
        List<string> port = [];
        for (int i = 0; i < inputs.Length; i += 2)
        {
            for (int p = 0; p < 4; p++)
                command.Parameters[p].Value = inputs[i + p % 2] == OracleRunner.NullMarker ? DBNull.Value : inputs[i + p % 2];
            using DbDataReader reader = command.ExecuteReader();
            DataTable schema = reader.GetSchemaTable()!;
            for (int c = 0; c < reader.FieldCount; c++)
                port.Add("=T:" + reader.GetDataTypeName(c) + '|' + schema.Rows[c]["NumericPrecision"] + '|' + schema.Rows[c]["NumericScale"]);
            Assert.True(reader.Read());
            port.Add("=R:" + (reader.IsDBNull(0) ? "NULL" : reader.GetString(0)) + '|'
                + (reader.IsDBNull(1) ? "NULL" : reader.GetString(1)));
        }
        List<string> java = OracleRunner.Run(inputs,
            """
            try (java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:datetime_parameters")) {
                try (java.sql.PreparedStatement s = conn.prepareStatement("SELECT CAST(? AS TIMESTAMP(9)) - CAST(? AS TIMESTAMP(9)), EXTRACT(EPOCH FROM (CAST(? AS TIMESTAMP(9)) - CAST(? AS TIMESTAMP(9)))) / 3600")) {
                    for (int i = 0; i < inputs.length; i += 2) {
                        for (int p = 0; p < 4; p++) s.setString(p + 1, nullable(inputs[i + p % 2]));
                        try (java.sql.ResultSet r = s.executeQuery()) {
                            java.sql.ResultSetMetaData m = r.getMetaData();
                            for (int c = 1; c <= m.getColumnCount(); c++) System.out.println("=T:" + m.getColumnTypeName(c) + "|" + m.getPrecision(c) + "|" + m.getScale(c));
                            r.next();
                            String a = r.getString(1), b = r.getString(2);
                            System.out.println("=R:" + (a == null ? "NULL" : a) + "|" + (b == null ? "NULL" : b));
                        }
                    }
                }
            }
            """);
        Assert.Equal(java, port);
    }

    [JavaOracleFact]
    public void BoundDurationMovesTheTimestampLikeJdbc()
    {
        string[] inputs = ["2024-02-28 23:30:00", "0 01:30:00", "2024-02-29 01:00:00", "-0 01:30:00"];
        using H2Connection connection = new("Data Source=interval_parameters_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT CAST(? AS TIMESTAMP) + CAST(? AS INTERVAL DAY TO SECOND)";
        for (int i = 0; i < 2; i++)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.DbType = DbType.String;
            parameter.Value = inputs[i];
            command.Parameters.Add(parameter);
        }
        command.Prepare();
        List<string> port = [];
        for (int i = 0; i < inputs.Length; i += 2)
        {
            command.Parameters[0].Value = inputs[i];
            command.Parameters[1].Value = inputs[i + 1];
            using DbDataReader reader = command.ExecuteReader();
            Assert.True(reader.Read());
            port.Add("=" + reader.GetString(0));
        }
        List<string> java = OracleRunner.Run(inputs,
            """
            try (java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:interval_parameters")) {
                try (java.sql.PreparedStatement s = conn.prepareStatement("SELECT CAST(? AS TIMESTAMP) + CAST(? AS INTERVAL DAY TO SECOND)")) {
                    for (int i = 0; i < inputs.length; i += 2) {
                        s.setString(1, inputs[i]); s.setString(2, inputs[i + 1]);
                        try (java.sql.ResultSet r = s.executeQuery()) { r.next(); System.out.println("=" + r.getString(1)); }
                    }
                }
            }
            """);
        Assert.Equal(java, port);
    }

    private static void CompareStatements(string[] statements)
    {
        using H2Connection connection = new("Data Source=datetime_arithmetic_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        List<string> port = [];
        for (int index = 0; index < statements.Length; index++)
        {
            string prefix = "=" + index.ToString(CultureInfo.InvariantCulture) + ':';
            using DbCommand command = connection.CreateCommand();
            command.CommandText = statements[index];
            try
            {
                if (!statements[index].StartsWith("SELECT", StringComparison.Ordinal))
                {
                    port.Add(prefix + "U:" + command.ExecuteNonQuery().ToString(CultureInfo.InvariantCulture));
                    continue;
                }

                using DbDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    List<string> cells = [];
                    for (int column = 0; column < reader.FieldCount; column++)
                        cells.Add(reader.IsDBNull(column) ? "NULL" : reader.GetString(column));
                    port.Add(prefix + "R:" + string.Join('|', cells));
                }
                port.Add(prefix + "Q");
            }
            catch (H2SharpException error)
            {
                port.Add(prefix + "E:" + error.H2ErrorCode.ToString(CultureInfo.InvariantCulture));
            }
        }

        List<string> java = OracleRunner.Run(statements,
            """
            try (java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:datetime_arithmetic")) {
                for (int i = 0; i < inputs.length; i++) {
                    try (java.sql.Statement s = conn.createStatement()) {
                        if (s.execute(inputs[i])) {
                            try (java.sql.ResultSet r = s.getResultSet()) {
                                while (r.next()) {
                                    StringBuilder line = new StringBuilder("=" + i + ":R:");
                                    for (int c = 1; c <= r.getMetaData().getColumnCount(); c++) {
                                        if (c > 1) line.append('|');
                                        String text = r.getString(c);
                                        line.append(r.wasNull() ? "NULL" : text);
                                    }
                                    System.out.println(line);
                                }
                                System.out.println("=" + i + ":Q");
                            }
                        } else System.out.println("=" + i + ":U:" + s.getUpdateCount());
                    } catch (java.sql.SQLException e) { System.out.println("=" + i + ":E:" + e.getErrorCode()); }
                }
            }
            """);
        Assert.True(java.SequenceEqual(port), "JVM:\n" + string.Join('\n', java) + "\nPort:\n" + string.Join('\n', port));
    }
}

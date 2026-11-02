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
/// DATE_TRUNC week fields and TIMEZONE extracts depend on the week fields of
/// the locale and on the session time zone, so they are compared with the
/// reference instead of pinned to hand-written answers.
/// </summary>
[Collection(OracleCollection.Name)]
public sealed class DateTruncOracleTests
{
    [JavaOracleFact]
    public void WeekTruncationsMatchJdbc()
    {
        CompareStatements([
            "SELECT CAST(DATE_TRUNC('WEEK', TIMESTAMP '2024-05-06 07:08:09') AS VARCHAR)",
            "SELECT CAST(DATE_TRUNC('WEEK', TIMESTAMP '2024-05-05 07:08:09') AS VARCHAR)",
            "SELECT CAST(DATE_TRUNC('WEEK_YEAR', TIMESTAMP '2024-05-06 07:08:09') AS VARCHAR)",
            "SELECT CAST(DATE_TRUNC('WEEK_YEAR', TIMESTAMP '2021-01-01 07:08:09') AS VARCHAR)",
            "SELECT EXTRACT(WEEK FROM DATE '2024-05-06')",
            "SELECT EXTRACT(WEEK FROM DATE '2024-01-01')",
            "SELECT EXTRACT(WEEK_YEAR FROM DATE '2021-01-01')",
            "SELECT EXTRACT(WEEK_YEAR FROM DATE '2024-12-31')",
            "SELECT EXTRACT(ISO_WEEK FROM DATE '2023-01-01')",
            "SELECT EXTRACT(ISO_WEEK_YEAR FROM DATE '2023-01-01')",
            "SELECT CAST(DATE_TRUNC('DECADE', DATE '1999-06-01') AS VARCHAR)",
            "SELECT CAST(DATE_TRUNC('CENTURY', DATE '1999-06-01') AS VARCHAR)",
        ]);
    }

    [JavaOracleFact]
    public void TimezoneExtractsMatchJdbc()
    {
        CompareStatements([
            "SELECT EXTRACT(TIMEZONE_HOUR FROM TIMESTAMP '2024-05-06 07:08:09')",
            "SELECT EXTRACT(TIMEZONE_MINUTE FROM TIMESTAMP '2024-05-06 07:08:09')",
            "SELECT EXTRACT(TIMEZONE_SECOND FROM TIMESTAMP '2024-05-06 07:08:09')",
            "SELECT EXTRACT(TIMEZONE_HOUR FROM TIME '10:00:00')",
            "SELECT EXTRACT(TIMEZONE_HOUR FROM TIMESTAMP WITH TIME ZONE '2024-05-06 07:08:09+05:30')",
            "SELECT EXTRACT(TIMEZONE_MINUTE FROM TIMESTAMP WITH TIME ZONE '2024-05-06 07:08:09+05:30')",
            "SELECT EXTRACT(TIMEZONE_SECOND FROM TIME WITH TIME ZONE '10:00:00+05:30')",
            "SELECT EXTRACT(TIMEZONE_HOUR FROM TIME WITH TIME ZONE '10:00:00+05:30')",
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
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:date_trunc");
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
                                    line.append(v == null ? "NULL" : v.toString());
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

    private static H2Connection Open()
    {
        H2Connection connection = new("Data Source=date_trunc_oracle_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
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
        return Convert.ToString(value, CultureInfo.InvariantCulture)!;
    }
}

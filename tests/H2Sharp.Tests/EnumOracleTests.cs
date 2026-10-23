/* Copyright 2026 H2Sharp contributors. Licensed under MPL 2.0 or EPL 1.0. */
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
public sealed class EnumOracleTests
{
    [JavaOracleFact]
    public void EnumLiteralsOrderingAndCatalogMatchJdbc()
    {
        CompareStatements([
            "CREATE TABLE E1(\"R\" ENUM('Guest','Member'))",
            "CREATE TABLE E2(\"R\" ENUM(Guest,Member))",
            "INSERT INTO E1 VALUES ('Guest'), ('Member'), ('Guest')",
            "INSERT INTO E1 VALUES ('Admin')",
            "SELECT \"R\" FROM E1 WHERE \"R\" = 'Guest'",
            "SELECT \"R\" FROM E1 ORDER BY \"R\"",
            "SELECT \"R\", CAST(\"R\" AS VARCHAR) FROM E1 ORDER BY \"R\"",
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.ENUM_VALUES WHERE OBJECT_NAME = 'E1'",
            "SELECT VALUE_NAME, VALUE_ORDINAL FROM INFORMATION_SCHEMA.ENUM_VALUES WHERE OBJECT_NAME = 'E1' ORDER BY VALUE_ORDINAL",
            "CREATE TABLE E3(\"R\" ENUM('a', 'a'))",
            "CREATE TABLE E4(\"R\" ENUM())",
            "CREATE TABLE E5(\"R\" ENUM(''))",
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:enum_oracle");
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
                                    line.append(v == null ? "NULL" : v instanceof String ? "'" + v + "'" : v.toString());
                                }
                                System.out.println(line);
                            }
                        }
                    } else System.out.println("=U:" + s.getUpdateCount());
                } catch (java.sql.SQLException e) { System.out.println("=E:" + e.getErrorCode()); }
            }
            conn.close();
            """);
        Assert.True(java.SequenceEqual(port),
            "JVM:\n" + string.Join('\n', java) + "\nPort:\n" + string.Join('\n', port));
    }

    private static H2Connection Open()
    {
        H2Connection connection = new("jdbc:h2:mem:enum_oracle_" + Guid.NewGuid().ToString("N"));
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
        if (value is string text) return "'" + text + "'";
        return Convert.ToString(value, CultureInfo.InvariantCulture)!;
    }
}

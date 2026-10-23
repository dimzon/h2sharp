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
public sealed class PositionOracleTests
{
    [JavaOracleFact]
    public void PositionFindsBytesAndStringsLikeJdbc()
    {
        CompareStatements([
            "SELECT POSITION(X'80' IN X'007F80FF')",
            "SELECT POSITION('b' IN 'abc'), POSITION('z' IN 'abc'), POSITION('b', 'abc')",
            "SELECT POSITION('' IN 'abc'), POSITION('b' IN '')",
            "SELECT POSITION(NULL IN 'abc'), POSITION('b' IN NULL)",
            "SELECT INSTR('abc', 'b'), INSTR('abc', 'b', 2), INSTR('abc', 'z')",
            "SELECT POSITION(X'FF' IN X'807F'), POSITION(X'7F' IN X'807F')",
            "SELECT POSITION(X'02' IN X'807F'), POSITION(X'02' IN X'007F')",
            "CREATE TABLE T(ID INT PRIMARY KEY, TOKEN VARBINARY)",
            "INSERT INTO T VALUES (1, X'007F'), (2, X'807F'), (3, NULL)",
            "SELECT ID FROM T WHERE POSITION(X'80' IN TOKEN) > 0 ORDER BY ID",
            "SELECT POSITION(X'80' IN TOKEN) FROM T ORDER BY ID",
            "SELECT ID FROM T WHERE POSITION('7F' IN TOKEN) > 0 ORDER BY ID",
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:position_oracle");
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
        Assert.True(java.SequenceEqual(port),
            "JVM:\n" + string.Join('\n', java) + "\nPort:\n" + string.Join('\n', port));
    }

    private static H2Connection Open()
    {
        H2Connection connection = new("jdbc:h2:mem:position_oracle_" + Guid.NewGuid().ToString("N"));
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
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    object value = reader.GetValue(i);
                    values.Add(value is DBNull ? "NULL" : Convert.ToString(value, CultureInfo.InvariantCulture)!);
                }

                lines.Add("=R:" + string.Join('|', values));
            }
        }
        catch (H2SharpException error)
        {
            lines.Add("=E:" + error.H2ErrorCode.ToString(CultureInfo.InvariantCulture));
        }
    }
}

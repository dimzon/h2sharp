/* Copyright 2026 H2Sharp contributors. Licensed under MPL 2.0 or EPL 1.0.
 * JSON caller values and parameter round trips against pinned H2 2.5 JDBC. */
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class JsonReadingOracleTests
{
    [JavaOracleFact]
    public void Issue31PlainStringAssignmentAndJsonLiteralMatchJdbcExactly()
    {
        string[] statements =
        [
            "CREATE TABLE J (\"Id\" INT PRIMARY KEY, \"Doc\" JSON)",
            "INSERT INTO J VALUES (1, '{\"a\": 1}')",
            "INSERT INTO J VALUES (2, JSON '{\"a\": 1}')",
            "SELECT \"Doc\" FROM J ORDER BY \"Id\"",
        ];
        using H2Connection connection = Open();
        using DbCommand command = connection.CreateCommand();
        for (int i = 0; i < statements.Length - 1; i++)
        {
            command.CommandText = statements[i];
            command.ExecuteNonQuery();
        }
        command.CommandText = statements[^1];
        List<string> port = [];
        using (DbDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                byte[] bytes = Assert.IsType<byte[]>(reader.GetValue(0));
                port.Add("=" + Convert.ToHexString(bytes).ToLowerInvariant() + '|' + OracleRunner.Hex(reader.GetString(0)));
            }
        }
        List<string> java = OracleRunner.Run(statements, """
            try (java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:issue31")) {
                for (int i = 0; i < inputs.length - 1; i++) conn.createStatement().execute(inputs[i]);
                try (java.sql.ResultSet r = conn.createStatement().executeQuery(inputs[inputs.length - 1])) {
                    while (r.next()) System.out.println("=" + org.h2.util.StringUtils.convertBytesToHex((byte[])r.getObject(1)) + "|" + hex(r.getString(1)));
                }
            }
            """);
        Assert.Equal(java, port);
        Assert.StartsWith("=227b5c22615c223a20317d22|", port[0]);
        Assert.StartsWith("=7b2261223a317d|", port[1]);
    }

    [JavaOracleFact]
    public void JsonObjectsTextBytesAndMetadataMatchJdbc()
    {
        string[] statements =
        [
            "CREATE TABLE J1(ID INT PRIMARY KEY, D JSON)",
            "INSERT INTO J1 VALUES (1, '{\"a\": 1}'), (2, JSON '{\"a\": 1}'), (3, JSON '[1, null, true]'), "
                + "(4, JSON 'null'), (5, NULL), (6, JSON '\"text\"'), (7, JSON '{\"текст\":\"привет 🎉\"}'), "
                + "(8, JSON '1.23456789012345678901234567890123456789'), (9, JSON '{}'), (10, JSON '[]')",
            "SELECT D FROM J1 ORDER BY ID",
            "SELECT JSON 'true', JSON 'false', JSON 'null', CAST(NULL AS JSON)",
            "SELECT CAST('42' AS JSON), CAST(42 AS JSON), CAST(X'7B2261223A317D' AS JSON)",
            "SELECT D, CAST(D AS VARCHAR) FROM J1 WHERE ID = 2",
        ];
        using H2Connection connection = Open();
        List<string> port = [];
        for (int i = 0; i < statements.Length; i++)
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = statements[i];
            if (!statements[i].StartsWith("SELECT", StringComparison.Ordinal))
                port.Add("=" + i + ":U:" + command.ExecuteNonQuery());
            else Record(command, port, i);
        }
        List<string> java = OracleRunner.Run(statements,
            """
            try (java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:json_reading")) {
                for (int i = 0; i < inputs.length; i++) {
                    try (java.sql.Statement s = conn.createStatement()) {
                        if (!s.execute(inputs[i])) { System.out.println("=" + i + ":U:" + s.getUpdateCount()); continue; }
                        try (java.sql.ResultSet r = s.getResultSet()) {
                            java.sql.ResultSetMetaData m = r.getMetaData();
                            for (int c = 1; c <= m.getColumnCount(); c++) {
                                String type = m.getColumnClassName(c).equals("[B") ? "BYTES" : "STRING";
                                System.out.println("=" + i + ":T:" + m.getColumnTypeName(c) + "|" + type);
                            }
                            while (r.next()) {
                                for (int c = 1; c <= m.getColumnCount(); c++) {
                                    Object value = r.getObject(c);
                                    if (value == null) { System.out.println("=" + i + ":R:NULL"); continue; }
                                    String text = r.getString(c);
                                    String converted = r.getObject(c, String.class);
                                    String line = "=" + i + ":R:" + hex(text) + "|" + hex(converted);
                                    if (value instanceof byte[]) {
                                        byte[] bytes = (byte[]) value;
                                        line += "|" + org.h2.util.StringUtils.convertBytesToHex(bytes)
                                                + "|" + org.h2.util.StringUtils.convertBytesToHex(r.getBytes(c));
                                    }
                                    System.out.println(line);
                                }
                            }
                        }
                    }
                }
            }
            """);
        Assert.True(java.SequenceEqual(port), "JVM:\n" + string.Join('\n', java) + "\nPort:\n" + string.Join('\n', port));
    }

    [JavaOracleFact]
    public void JsonBytesCanBeReadAndBoundAgainWithoutChangingTheValue()
    {
        string[] inputs = ["{\"a\": 1}", "[1, null, true]", "null", "\"text\"", "{\"текст\":\"привет 🎉\"}", "invalid"];
        using H2Connection connection = Open();
        using DbCommand setup = connection.CreateCommand();
        setup.CommandText = "CREATE TABLE J(ID INT PRIMARY KEY, D JSON)";
        setup.ExecuteNonQuery();
        using DbCommand insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO J VALUES (?, ?)";
        for (int i = 0; i < 2; i++) insert.Parameters.Add(insert.CreateParameter());
        insert.Parameters[0].Value = 0;
        insert.Parameters[1].Value = "initial";
        insert.Prepare();
        List<string> port = [];
        for (int mode = 0; mode < 2; mode++)
        {
            for (int i = 0; i < inputs.Length; i++)
            {
                int index = mode * inputs.Length + i;
                insert.Parameters[0].Value = index;
                insert.Parameters[1].DbType = mode == 0 ? DbType.String : DbType.Binary;
                insert.Parameters[1].Value = mode == 0 ? inputs[i] : Encoding.UTF8.GetBytes(inputs[i]);
                try
                {
                    insert.ExecuteNonQuery();
                    using DbCommand select = connection.CreateCommand();
                    select.CommandText = "SELECT D FROM J WHERE ID = " + index;
                    byte[] bytes = (byte[])select.ExecuteScalar()!;
                    port.Add("=" + index + ":R:" + Convert.ToHexString(bytes).ToLowerInvariant());
                    insert.Parameters[0].Value = 100 + index;
                    insert.Parameters[1].Value = bytes;
                    insert.Parameters[1].DbType = DbType.Binary;
                    insert.ExecuteNonQuery();
                    select.CommandText = "SELECT D FROM J WHERE ID = " + (100 + index);
                    port.Add("=" + index + ":COPY:" + Convert.ToHexString((byte[])select.ExecuteScalar()!).ToLowerInvariant());
                }
                catch (H2SharpException error) { port.Add("=" + index + ":E:" + error.H2ErrorCode); }
            }
        }
        List<string> java = OracleRunner.Run(inputs,
            """
            try (java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:json_roundtrip")) {
                conn.createStatement().execute("CREATE TABLE J(ID INT PRIMARY KEY, D JSON)");
                try (java.sql.PreparedStatement insert = conn.prepareStatement("INSERT INTO J VALUES (?, ?)")) {
                    for (int mode = 0; mode < 2; mode++) {
                        for (int i = 0; i < inputs.length; i++) {
                            int index = mode * inputs.length + i;
                            insert.setInt(1, index);
                            if (mode == 0) insert.setString(2, inputs[i]);
                            else insert.setBytes(2, inputs[i].getBytes(java.nio.charset.StandardCharsets.UTF_8));
                            try {
                                insert.executeUpdate();
                                byte[] bytes;
                                try (java.sql.ResultSet r = conn.createStatement().executeQuery("SELECT D FROM J WHERE ID = " + index)) {
                                    r.next(); bytes = (byte[]) r.getObject(1);
                                    System.out.println("=" + index + ":R:" + org.h2.util.StringUtils.convertBytesToHex(bytes));
                                }
                                insert.setInt(1, 100 + index); insert.setObject(2, bytes); insert.executeUpdate();
                                try (java.sql.ResultSet r = conn.createStatement().executeQuery("SELECT D FROM J WHERE ID = " + (100 + index))) {
                                    r.next(); System.out.println("=" + index + ":COPY:" + org.h2.util.StringUtils.convertBytesToHex((byte[]) r.getObject(1)));
                                }
                            } catch (java.sql.SQLException e) { System.out.println("=" + index + ":E:" + e.getErrorCode()); }
                        }
                    }
                }
            }
            """);
        Assert.Equal(java, port);
    }

    [JavaOracleFact]
    public void SystemTextJsonParametersMatchJdbcExplicitJsonType()
    {
        string[] inputs = ["{\"a\": 1, \"текст\": \"привет 🎉\"}", "[1, null, true, {\"b\":2}]",
            "null", "\"text\"", "1.23456789012345678901234567890123456789", "true", "false", "{}", "[]"];
        using H2Connection connection = Open();
        using DbCommand setup = connection.CreateCommand();
        setup.CommandText = "CREATE TABLE J(ID INT PRIMARY KEY, D JSON)";
        setup.ExecuteNonQuery();
        using DbCommand insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO J VALUES (?, ?)";
        for (int i = 0; i < 2; i++) insert.Parameters.Add(insert.CreateParameter());
        List<string> port = [];
        for (int mode = 0; mode < 2; mode++)
        {
            for (int i = 0; i < inputs.Length; i++)
            {
                using JsonDocument document = JsonDocument.Parse(inputs[i]);
                int index = mode * inputs.Length + i;
                insert.Parameters[0].Value = index;
                insert.Parameters[1].DbType = DbType.Object;
                insert.Parameters[1].Value = mode == 0 ? document : document.RootElement;
                insert.ExecuteNonQuery();
                using DbCommand select = connection.CreateCommand();
                select.CommandText = "SELECT D FROM J WHERE ID = " + index;
                port.Add("=" + index + ':' + Convert.ToHexString((byte[])select.ExecuteScalar()!).ToLowerInvariant());
            }
        }
        List<string> java = OracleRunner.Run(inputs,
            """
            try (java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:stj_parameters")) {
                conn.createStatement().execute("CREATE TABLE J(ID INT PRIMARY KEY, D JSON)");
                try (java.sql.PreparedStatement insert = conn.prepareStatement("INSERT INTO J VALUES (?, ?)")) {
                    for (int mode = 0; mode < 2; mode++) {
                        for (int i = 0; i < inputs.length; i++) {
                            int index = mode * inputs.length + i;
                            insert.setInt(1, index); insert.setObject(2, inputs[i], org.h2.api.H2Type.JSON);
                            insert.executeUpdate();
                            try (java.sql.ResultSet r = conn.createStatement().executeQuery("SELECT D FROM J WHERE ID = " + index)) {
                                r.next(); System.out.println("=" + index + ":" + org.h2.util.StringUtils.convertBytesToHex((byte[])r.getObject(1)));
                            }
                        }
                    }
                }
            }
            """);
        Assert.Equal(java, port);
    }

    private static H2Connection Open()
    {
        H2Connection connection = new("jdbc:h2:mem:json_read_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        return connection;
    }

    private static void Record(DbCommand command, List<string> lines, int index)
    {
        using DbDataReader reader = command.ExecuteReader();
        for (int c = 0; c < reader.FieldCount; c++)
            lines.Add("=" + index + ":T:" + reader.GetDataTypeName(c) + '|'
                + (reader.GetFieldType(c) == typeof(byte[]) ? "BYTES" : "STRING"));
        while (reader.Read())
        {
            for (int c = 0; c < reader.FieldCount; c++)
            {
                object value = reader.GetValue(c);
                if (value is DBNull) { lines.Add("=" + index + ":R:NULL"); continue; }
                string line = "=" + index + ":R:" + OracleRunner.Hex(reader.GetString(c)) + '|'
                    + OracleRunner.Hex(reader.GetFieldValue<string>(c));
                if (value is byte[] bytes)
                {
                    byte[] copy = new byte[reader.GetBytes(c, 0, null, 0, 0)];
                    Assert.Equal(copy.Length, reader.GetBytes(c, 0, copy, 0, copy.Length));
                    line += '|' + Convert.ToHexString(bytes).ToLowerInvariant() + '|'
                        + Convert.ToHexString(copy).ToLowerInvariant();
                }
                lines.Add(line);
            }
        }
    }
}

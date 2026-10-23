/* Copyright 2026 H2Sharp contributors. Licensed under MPL 2.0 or EPL 1.0.
 * Regex operators against the pinned H2 2.5 JDBC implementation.
 * The regex engine is the existing accepted .NET adaptation; the corpus uses
 * patterns shared by java.util.regex.Pattern and System.Text.RegularExpressions. */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class RegexOperatorOracleTests
{
    [JavaOracleFact]
    public void OperatorsFlagsNullsAndPrecedenceMatchJdbc()
    {
        string[] statements =
        [
            "SELECT 'abc' ~ '^a', 'ABC' ~* '^a'",
            "SELECT 'ABC' ~ '^a', 'abc' !~ '^a', 'ABC' !~* '^a', 'xabcx' ~ 'ab'",
            "SELECT '' ~ '', '' ~ '.', 'abc' ~ '^ab$', 'abc' ~ '^abc$'",
            "SELECT 'a' || CHR(10) || 'b' ~ 'a.b', 'a' || CHR(10) || 'b' ~ '(?s)a.b'",
            "SELECT 'a' || CHR(10) || 'b' ~ '^b', 'a' || CHR(10) || 'b' ~ '(?m)^b'",
            "SELECT 'ab' ~ '(?x)a b', 'ab' ~ 'a b', 'AbC' ~ '(?i)^abc$'",
            "SELECT 'x12z' ~ '[0-9]+', 'abc' ~ '[0-9]+', 'x12z' ~ '\\d+', 'ab cd' ~ '\\s', 'a_1' ~ '^\\w+$'",
            "SELECT NULL ~ 'a', 'abc' ~ NULL, NULL ~* 'a', 'abc' !~ NULL",
            "SELECT 'abc' ~ '[', 'abc' ~* '['",
            "SELECT 'ABC' ~* '(?-i)^a', 'ABC' ~* '(?-i)^A'",
            "SELECT 'a' || 'b' ~ '^ab$', 'abc' ~ '^a' || 'suffix'",
            "SELECT 'abc' ~ ('^' || 'a'), NOT 'abc' ~ '^a', 'abc' ~ '^a' AND 'ABC' ~* '^a'",
            "SELECT ('abc' ~ '^a') = TRUE, CASE WHEN 'ABC' ~* '^a' THEN 'match' ELSE 'miss' END",
            "CREATE TABLE T(ID INT PRIMARY KEY, TITLE VARCHAR, PATTERN VARCHAR)",
            "INSERT INTO T VALUES (1, 'abc', '^a'), (2, 'ABC', '^a'), (3, 'xabcx', 'ab'), (4, 'ab', '^ab$'), "
                + "(5, NULL, '^a'), (6, 'missing', NULL)",
            "SELECT ID FROM T WHERE TITLE ~ '^a' ORDER BY ID",
            "SELECT ID FROM T AS t WHERE t.TITLE ~* '^a' ORDER BY t.ID",
            "SELECT ID FROM T WHERE TITLE !~ '^a' ORDER BY ID",
            "SELECT ID FROM T WHERE TITLE !~* '^a' ORDER BY ID",
            "SELECT ID, TITLE ~ PATTERN, TITLE ~* PATTERN, TITLE !~ PATTERN, TITLE !~* PATTERN FROM T ORDER BY ID",
            "SELECT ID FROM T ORDER BY TITLE ~* '^a' NULLS LAST, ID",
            "SELECT COUNT(*) FROM T WHERE TITLE ~* '^a'",
            "SELECT ID FROM T WHERE TITLE ~ ('^' || 'a') ORDER BY ID",
            "SELECT CAST('ABC' AS VARCHAR_IGNORECASE) ~ '^a'",
        ];
        using H2Connection connection = Open();
        List<string> port = [];
        for (int i = 0; i < statements.Length; i++)
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = statements[i];
            Record(command, port, i);
        }
        List<string> java = OracleRunner.Run(statements,
            """
            try (java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:regex_operators")) {
                for (int i = 0; i < inputs.length; i++) {
                    try (java.sql.Statement s = conn.createStatement()) {
                        if (s.execute(inputs[i])) {
                            try (java.sql.ResultSet r = s.getResultSet()) {
                                int count = r.getMetaData().getColumnCount();
                                while (r.next()) {
                                    StringBuilder line = new StringBuilder("=" + i + ":R:");
                                    for (int c = 1; c <= count; c++) {
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

    [JavaOracleFact]
    public void ParameterPatternsCanBeReboundForAllFourOperators()
    {
        string[] inputs = ["^a", "ab", "(?i)^a", "[", OracleRunner.NullMarker];
        string[] operators = ["~", "~*", "!~", "!~*"];
        using H2Connection connection = Open();
        using DbCommand setup = connection.CreateCommand();
        setup.CommandText = "CREATE TABLE T(ID INT PRIMARY KEY, TITLE VARCHAR)";
        setup.ExecuteNonQuery();
        setup.CommandText = "INSERT INTO T VALUES (1, 'abc'), (2, 'ABC'), (3, 'xabcx'), (4, 'ab'), (5, NULL)";
        setup.ExecuteNonQuery();
        List<string> port = [];
        for (int op = 0; op < operators.Length; op++)
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT ID FROM T WHERE TITLE " + operators[op] + " ? ORDER BY ID";
            DbParameter parameter = command.CreateParameter();
            parameter.Value = inputs[0];
            command.Parameters.Add(parameter);
            command.Prepare();
            for (int i = 0; i < inputs.Length; i++)
            {
                parameter.Value = inputs[i] == OracleRunner.NullMarker ? DBNull.Value : inputs[i];
                Record(command, port, op * inputs.Length + i);
            }
        }
        List<string> java = OracleRunner.Run(inputs,
            """
            try (java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:regex_parameters")) {
                try (java.sql.Statement setup = conn.createStatement()) {
                    setup.execute("CREATE TABLE T(ID INT PRIMARY KEY, TITLE VARCHAR)");
                    setup.execute("INSERT INTO T VALUES (1, 'abc'), (2, 'ABC'), (3, 'xabcx'), (4, 'ab'), (5, NULL)");
                }
                String[] operators = {"~", "~*", "!~", "!~*"};
                for (int op = 0; op < operators.length; op++) {
                    try (java.sql.PreparedStatement s = conn.prepareStatement("SELECT ID FROM T WHERE TITLE " + operators[op] + " ? ORDER BY ID")) {
                        for (int i = 0; i < inputs.length; i++) {
                            int index = op * inputs.length + i;
                            s.setString(1, nullable(inputs[i]));
                            try (java.sql.ResultSet r = s.executeQuery()) {
                                while (r.next()) System.out.println("=" + index + ":R:" + r.getString(1));
                                System.out.println("=" + index + ":Q");
                            } catch (java.sql.SQLException e) { System.out.println("=" + index + ":E:" + e.getErrorCode()); }
                        }
                    }
                }
            }
            """);
        Assert.Equal(java, port);
    }

    private static H2Connection Open()
    {
        H2Connection connection = new("jdbc:h2:mem:regex_operator_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        return connection;
    }

    private static void Record(DbCommand command, List<string> lines, int index)
    {
        string prefix = "=" + index.ToString(CultureInfo.InvariantCulture) + ':';
        try
        {
            if (!command.CommandText.StartsWith("SELECT", StringComparison.Ordinal))
            {
                lines.Add(prefix + "U:" + command.ExecuteNonQuery().ToString(CultureInfo.InvariantCulture));
                return;
            }
            using DbDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                List<string> cells = [];
                for (int c = 0; c < reader.FieldCount; c++) cells.Add(reader.IsDBNull(c) ? "NULL" : reader.GetString(c));
                lines.Add(prefix + "R:" + string.Join('|', cells));
            }
            lines.Add(prefix + "Q");
        }
        catch (H2SharpException error) { lines.Add(prefix + "E:" + error.H2ErrorCode.ToString(CultureInfo.InvariantCulture)); }
    }
}

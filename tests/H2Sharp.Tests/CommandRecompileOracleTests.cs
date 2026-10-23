/* Copyright 2026 H2Sharp contributors. Licensed under MPL 2.0 or EPL 1.0.
 * Reusing the same command and live parameter objects against H2 2.5 JDBC. */
using System;
using System.Collections.Generic;
using System.Globalization;
using H2Sharp.Command;
using H2Sharp.Data;
using H2Sharp.Expressions;
using H2Sharp.Result;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class CommandRecompileOracleTests
{
    [JavaOracleFact]
    public void SameCommandCanChangeOrderPositionsAndRecoverAfterInvalidPositions()
    {
        string[] inputs = ["2", "1", "-2", "NULL", "0", "3", "2", "-2147483648", "1"];
        using H2Connection connection = Open();
        connection.Session!.PrepareCommand("CREATE TABLE T(ID INT PRIMARY KEY, N INT)").ExecuteUpdate(false);
        connection.Session.PrepareCommand("INSERT INTO T VALUES (1, 80), (2, 10), (3, 20), (4, NULL)").ExecuteUpdate(false);
        CommandContainer command = Assert.IsType<CommandContainer>(connection.Session.PrepareCommand(
            "SELECT ID, N FROM T ORDER BY ? NULLS LAST, ID"));
        Parameter parameter = Assert.IsType<Parameter>(Assert.Single(command.GetParameters()));
        Assert.True(command.GetPreparedCommand().IsPrepareAlways());
        List<string> port = [];
        for (int i = 0; i < inputs.Length; i++)
        {
            parameter.SetValue(inputs[i] == "NULL" ? ValueNull.Instance : ValueInteger.Get(int.Parse(inputs[i], CultureInfo.InvariantCulture)));
            Record(command, port, i);
            Assert.Same(parameter, Assert.Single(command.GetParameters()));
        }
        List<string> java = OracleRunner.Run(inputs,
            """
            try (java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:recompile_positions")) {
                conn.createStatement().execute("CREATE TABLE T(ID INT PRIMARY KEY, N INT)");
                conn.createStatement().execute("INSERT INTO T VALUES (1, 80), (2, 10), (3, 20), (4, NULL)");
                try (java.sql.PreparedStatement s = conn.prepareStatement("SELECT ID, N FROM T ORDER BY ? NULLS LAST, ID")) {
                    for (int i = 0; i < inputs.length; i++) {
                        if (inputs[i].equals("NULL")) s.setNull(1, java.sql.Types.INTEGER);
                        else s.setInt(1, Integer.parseInt(inputs[i]));
                        try (java.sql.ResultSet r = s.executeQuery()) {
                            while (r.next()) System.out.println("=" + i + ":R:" + r.getString(1) + "|" + r.getString(2));
                            System.out.println("=" + i + ":Q");
                        } catch (java.sql.SQLException e) { System.out.println("=" + i + ":E:" + e.getErrorCode()); }
                    }
                }
            }
            """);
        Assert.Equal(java, port);
    }

    [JavaOracleFact]
    public void RepeatedIndexedParametersAndSchemaChangesKeepTheirBinding()
    {
        string[] inputs = ["2", "1", "-2"];
        using H2Connection connection = Open();
        connection.Session!.PrepareCommand("CREATE TABLE T(ID INT PRIMARY KEY, N INT)").ExecuteUpdate(false);
        connection.Session.PrepareCommand("INSERT INTO T VALUES (1, 80), (2, 10), (3, 20)").ExecuteUpdate(false);
        CommandContainer command = Assert.IsType<CommandContainer>(connection.Session.PrepareCommand(
            "SELECT ID, N FROM T WHERE ?1 = ?1 ORDER BY ?2 DESC LIMIT ?3"));
        List<ParameterInterface> parameters = command.GetParameters();
        Assert.Equal(3, parameters.Count);
        parameters[0].SetValue(ValueInteger.Get(7), false);
        parameters[2].SetValue(ValueInteger.Get(2), false);
        List<string> port = [];
        for (int i = 0; i < inputs.Length; i++)
        {
            parameters[1].SetValue(ValueInteger.Get(int.Parse(inputs[i], CultureInfo.InvariantCulture)), false);
            if (i == 1) connection.Session.PrepareCommand("CREATE INDEX T_N ON T(N)").ExecuteUpdate(false);
            Record(command, port, i);
            for (int p = 0; p < 3; p++) Assert.Same(parameters[p], command.GetParameters()[p]);
        }
        // A non-forced query also recompiles after a metadata change and retains its parameters.
        CommandContainer plain = Assert.IsType<CommandContainer>(connection.Session.PrepareCommand("SELECT ID FROM T WHERE N > ? ORDER BY ID"));
        ParameterInterface filter = Assert.Single(plain.GetParameters());
        filter.SetValue(ValueInteger.Get(15), false);
        Record(plain, port, 3);
        connection.Session.PrepareCommand("DROP INDEX T_N").ExecuteUpdate(false);
        Assert.True(plain.GetPreparedCommand().NeedRecompile());
        Record(plain, port, 4);
        Assert.Same(filter, Assert.Single(plain.GetParameters()));
        Assert.False(plain.GetPreparedCommand().NeedRecompile());
        List<string> java = OracleRunner.Run(inputs,
            """
            try (java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:recompile_bindings")) {
                conn.createStatement().execute("CREATE TABLE T(ID INT PRIMARY KEY, N INT)");
                conn.createStatement().execute("INSERT INTO T VALUES (1, 80), (2, 10), (3, 20)");
                try (java.sql.PreparedStatement s = conn.prepareStatement("SELECT ID, N FROM T WHERE ?1 = ?1 ORDER BY ?2 DESC LIMIT ?3")) {
                    s.setInt(1, 7); s.setInt(3, 2);
                    for (int i = 0; i < inputs.length; i++) {
                        s.setInt(2, Integer.parseInt(inputs[i]));
                        if (i == 1) conn.createStatement().execute("CREATE INDEX T_N ON T(N)");
                        try (java.sql.ResultSet r = s.executeQuery()) {
                            while (r.next()) System.out.println("=" + i + ":R:" + r.getString(1) + "|" + r.getString(2));
                            System.out.println("=" + i + ":Q");
                        }
                    }
                }
                try (java.sql.PreparedStatement s = conn.prepareStatement("SELECT ID FROM T WHERE N > ? ORDER BY ID")) {
                    s.setInt(1, 15);
                    for (int i = 3; i < 5; i++) {
                        if (i == 4) conn.createStatement().execute("DROP INDEX T_N");
                        try (java.sql.ResultSet r = s.executeQuery()) {
                            while (r.next()) System.out.println("=" + i + ":R:" + r.getString(1));
                            System.out.println("=" + i + ":Q");
                        }
                    }
                }
            }
            """);
        Assert.Equal(java, port);
    }

    [JavaOracleFact]
    public void NestedPositionalOrderIsRecompiledWithOuterParameters()
    {
        string[] inputs = ["1", "2", "-2"];
        using H2Connection connection = Open();
        connection.Session!.PrepareCommand("CREATE TABLE T(ID INT PRIMARY KEY, N INT)").ExecuteUpdate(false);
        connection.Session.PrepareCommand("INSERT INTO T VALUES (1, 80), (2, 10), (3, 20)").ExecuteUpdate(false);
        CommandContainer command = Assert.IsType<CommandContainer>(connection.Session.PrepareCommand(
            "SELECT ID FROM T WHERE ID IN (SELECT ID FROM T ORDER BY ? LIMIT 1) ORDER BY ID"));
        ParameterInterface parameter = Assert.Single(command.GetParameters());
        List<string> port = [];
        for (int i = 0; i < inputs.Length; i++)
        {
            parameter.SetValue(ValueInteger.Get(int.Parse(inputs[i], CultureInfo.InvariantCulture)), false);
            Record(command, port, i);
        }
        List<string> java = OracleRunner.Run(inputs,
            """
            try (java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:recompile_nested")) {
                conn.createStatement().execute("CREATE TABLE T(ID INT PRIMARY KEY, N INT)");
                conn.createStatement().execute("INSERT INTO T VALUES (1, 80), (2, 10), (3, 20)");
                try (java.sql.PreparedStatement s = conn.prepareStatement("SELECT ID FROM T WHERE ID IN (SELECT ID FROM T ORDER BY ? LIMIT 1) ORDER BY ID")) {
                    for (int i = 0; i < inputs.length; i++) {
                        s.setInt(1, Integer.parseInt(inputs[i]));
                        try (java.sql.ResultSet r = s.executeQuery()) {
                            while (r.next()) System.out.println("=" + i + ":R:" + r.getString(1));
                            System.out.println("=" + i + ":Q");
                        } catch (java.sql.SQLException e) { System.out.println("=" + i + ":E:" + e.getErrorCode()); }
                    }
                }
            }
            """);
        Assert.Equal(java, port);
    }

    [JavaOracleFact]
    public void UpdateRecompilationPreservesParametersAndStatementTokens()
    {
        using H2Connection connection = Open();
        connection.Session!.PrepareCommand("CREATE TABLE T(ID INT PRIMARY KEY, N INT)").ExecuteUpdate(false);
        connection.Session.PrepareCommand("INSERT INTO T VALUES (1, 10), (2, 20)").ExecuteUpdate(false);
        const string Sql = "  UPDATE T SET N = ?1 WHERE ID = ?2  ";
        CommandContainer update = Assert.IsType<CommandContainer>(connection.Session.PrepareCommand(Sql));
        Assert.Equal(Sql.Trim(), update.GetPreparedCommand().GetSql());
        Assert.NotNull(update.GetPreparedCommand().GetSqlTokens());
        List<ParameterInterface> parameters = update.GetParameters();
        parameters[0].SetValue(ValueInteger.Get(99), false);
        parameters[1].SetValue(ValueInteger.Get(2), false);
        connection.Session.PrepareCommand("CREATE INDEX T_N ON T(N)").ExecuteUpdate(false);
        Assert.True(update.GetPreparedCommand().NeedRecompile());
        List<string> port = ["=U:" + update.ExecuteUpdate(false).GetUpdateCount()];
        Assert.Same(parameters[0], update.GetParameters()[0]);
        Assert.Same(parameters[1], update.GetParameters()[1]);
        parameters[0].SetValue(ValueInteger.Get(7), false);
        parameters[1].SetValue(ValueInteger.Get(1), false);
        connection.Session.PrepareCommand("DROP INDEX T_N").ExecuteUpdate(false);
        port.Add("=U:" + update.ExecuteUpdate(false).GetUpdateCount());
        Record(Assert.IsType<CommandContainer>(connection.Session.PrepareCommand("SELECT ID, N FROM T ORDER BY ID")), port, 0);
        List<string> java = OracleRunner.Run([], """
            try (java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:recompile_update")) {
                conn.createStatement().execute("CREATE TABLE T(ID INT PRIMARY KEY, N INT)");
                conn.createStatement().execute("INSERT INTO T VALUES (1, 10), (2, 20)");
                try (java.sql.PreparedStatement s = conn.prepareStatement("  UPDATE T SET N = ?1 WHERE ID = ?2  ")) {
                    s.setInt(1, 99); s.setInt(2, 2);
                    conn.createStatement().execute("CREATE INDEX T_N ON T(N)");
                    System.out.println("=U:" + s.executeUpdate());
                    s.setInt(1, 7); s.setInt(2, 1);
                    conn.createStatement().execute("DROP INDEX T_N");
                    System.out.println("=U:" + s.executeUpdate());
                }
                try (java.sql.ResultSet r = conn.createStatement().executeQuery("SELECT ID, N FROM T ORDER BY ID")) {
                    while (r.next()) System.out.println("=0:R:" + r.getString(1) + "|" + r.getString(2));
                    System.out.println("=0:Q");
                }
            }
            """);
        Assert.Equal(java, port);
    }

    private static H2Connection Open()
    {
        H2Connection connection = new("jdbc:h2:mem:recompile_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        return connection;
    }

    private static void Record(CommandContainer command, List<string> lines, int index)
    {
        string prefix = "=" + index.ToString(CultureInfo.InvariantCulture) + ':';
        try
        {
            using ResultInterface rows = command.Query(0);
            while (rows.Next())
            {
                Value?[] row = rows.CurrentRow();
                List<string> cells = [];
                for (int c = 0; c < rows.GetVisibleColumnCount(); c++) cells.Add(row[c]!.GetString() ?? "null");
                lines.Add(prefix + "R:" + string.Join('|', cells));
            }
            lines.Add(prefix + "Q");
        }
        catch (H2SharpException error) { lines.Add(prefix + "E:" + error.H2ErrorCode); }
    }
}

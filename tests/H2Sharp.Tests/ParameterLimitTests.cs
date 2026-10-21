/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The number of parameters a statement may name, checked against the reference running the
 * same text.
 *
 * The reference marks the parameters a statement uses in a java.util.BitSet, which grows as
 * the parameters are marked, and refuses a statement that names more than
 * Constants.MAX_PARAMETER_INDEX of them. The port marks them in a growing list of bits, and
 * a statement with more than eight parameters is read like any other.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using H2Sharp;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Result;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ParameterLimitTests
{
    /// <summary>
    /// A statement that names more than eight parameters, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheManyParametersAreTheSameAsTheReference()
    {
        List<string> port = RunPort();
        List<string> java = RunJava();
        AssertSame(java, port);
    }

    /// <summary>
    /// A statement that names more parameters than the limit allows, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheParameterLimitIsTheSameAsTheReference()
    {
        List<string> port = RunPortWithTooManyParameters();
        List<string> java = RunJavaWithTooManyParameters();
        AssertSame(java, port);
    }

    /// <summary>
    /// Compares the answers of the two sides.
    /// </summary>
    /// <param name="java">the answers of the reference</param>
    /// <param name="port">the answers of the port</param>
    private static void AssertSame(List<string> java, List<string> port)
    {
        Assert.True(
            java.Count == port.Count && java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
    }

    /// <summary>
    /// The text of a statement that names the given number of parameters.
    /// </summary>
    /// <param name="count">the number of parameters</param>
    /// <returns>the text</returns>
    private static string Statement(int count)
    {
        StringBuilder builder = new("SELECT ");
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append('?');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Runs the statement with many parameters here and writes what it answered.
    /// </summary>
    /// <returns>the lines</returns>
    private static List<string> RunPort()
    {
        SessionLocal session = NewSession();
        List<string> lines = [];
        try
        {
            CommandContainer container =
                Assert.IsType<CommandContainer>(session.PrepareCommand(Statement(15)));
            List<Parameter>? parameters = container.GetPreparedCommand().GetParameters();
            Assert.NotNull(parameters);
            Assert.Equal(15, parameters!.Count);
            for (int i = 0; i < parameters.Count; i++)
            {
                parameters[i].SetValue(ValueInteger.Get(i + 1));
            }

            ResultInterface result = container.Query(-1);
            int visible = result.GetVisibleColumnCount();
            while (result.Next())
            {
                Value?[] row = result.CurrentRow();
                List<string> cells = [];
                for (int i = 0; i < visible; i++)
                {
                    Value? value = row[i];
                    cells.Add(value is null or ValueNull ? "-" : value.GetString() ?? "-");
                }

                lines.Add("=" + string.Join(":", cells));
            }
        }
        catch (Exception e)
        {
            lines.Add(e is H2SharpException h2
                ? "=" + h2.H2ErrorCode.ToString(CultureInfo.InvariantCulture)
                : "=" + e.GetType().Name + ":" + e.Message);
        }

        return lines;
    }

    /// <summary>
    /// Runs the statement with too many parameters here and writes what it answered.
    /// </summary>
    /// <returns>the lines</returns>
    private static List<string> RunPortWithTooManyParameters()
    {
        SessionLocal session = NewSession();
        List<string> lines = [];
        try
        {
            _ = session.PrepareCommand(Statement(100_001));
            lines.Add("=prepared");
        }
        catch (Exception e)
        {
            lines.Add(e is H2SharpException h2
                ? "=" + h2.H2ErrorCode.ToString(CultureInfo.InvariantCulture)
                : "=" + e.GetType().Name + ":" + e.Message);
        }

        return lines;
    }

    /// <summary>
    /// Creates a session of its own.
    /// </summary>
    /// <returns>the session</returns>
    private static SessionLocal NewSession()
    {
        return new SessionLocal(
            new Database(new ConnectionInfo("jdbc:h2:mem:parameter_limit")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
    }

    /// <summary>
    /// Runs the statement with many parameters on the reference and writes what it answered.
    /// </summary>
    /// <returns>the lines</returns>
    private static List<string> RunJava()
        => OracleRunner.Run(
            [],
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:parameter_limit");
            StringBuilder sql = new StringBuilder("SELECT ");
            for (int i = 0; i < 15; i++) {
              if (i > 0) {
                sql.append(", ");
              }
              sql.append("?");
            }
            java.sql.PreparedStatement ps = conn.prepareStatement(sql.toString());
            for (int i = 1; i <= 15; i++) {
              ps.setInt(i, i);
            }
            java.sql.ResultSet rs = ps.executeQuery();
            int columns = rs.getMetaData().getColumnCount();
            while (rs.next()) {
              StringBuilder line = new StringBuilder("=");
              for (int c = 1; c <= columns; c++) {
                if (c > 1) {
                  line.append(':');
                }
                String s = rs.getString(c);
                line.append(rs.wasNull() ? "-" : s);
              }
              System.out.println(line);
            }
            conn.close();
            """,
            extraJavaArgs: ["-Duser.language=en"]);

    /// <summary>
    /// Runs the statement with too many parameters on the reference and writes what it answered.
    /// </summary>
    /// <returns>the lines</returns>
    private static List<string> RunJavaWithTooManyParameters()
        => OracleRunner.Run(
            [],
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:parameter_limit");
            StringBuilder sql = new StringBuilder("SELECT ");
            for (int i = 0; i < 100001; i++) {
              if (i > 0) {
                sql.append(", ");
              }
              sql.append("?");
            }
            try {
              conn.prepareStatement(sql.toString());
              System.out.println("=prepared");
            } catch (java.sql.SQLException e) {
              System.out.println("=" + e.getErrorCode());
            }
            conn.close();
            """,
            extraJavaArgs: ["-Duser.language=en"]);
}

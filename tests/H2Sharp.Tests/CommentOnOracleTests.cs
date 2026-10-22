/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * COMMENT ON, checked against the reference running the same text.
 *
 * A comment lives on the object it names — a table, a column, an index, a schema, a
 * sequence or a constraint — and the catalog views read it from there.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using H2Sharp;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class CommentOnOracleTests
{
    /// <summary>
    /// Comments on the objects the port has, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void CommentsOnTablesColumnsAndSchemasAreTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR(20))",
            "CREATE INDEX I ON T(NAME)",
            "CREATE SCHEMA S",
            "CREATE SEQUENCE Q",
            "COMMENT ON TABLE T IS 'table comment'",
            "SELECT REMARKS FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'T'",
            "COMMENT ON COLUMN T.NAME IS 'column comment'",
            "SELECT REMARKS FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'T' AND COLUMN_NAME = 'NAME'",
            "COMMENT ON COLUMN PUBLIC.T.ID IS 'id column'",
            "SELECT REMARKS FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'T' AND COLUMN_NAME = 'ID'",
            "COMMENT ON INDEX I IS 'index comment'",
            "SELECT REMARKS FROM INFORMATION_SCHEMA.INDEXES WHERE INDEX_NAME = 'I'",
            "COMMENT ON SCHEMA S IS 'schema comment'",
            "SELECT REMARKS FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = 'S'",
            "COMMENT ON SEQUENCE Q IS 'sequence comment'",
            "SELECT REMARKS FROM INFORMATION_SCHEMA.SEQUENCES WHERE SEQUENCE_NAME = 'Q'",
            "COMMENT ON TABLE T IS NULL",
            "SELECT REMARKS FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'T'",
            "COMMENT ON TABLE T IS ''",
            "SELECT REMARKS FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'T'",
            "COMMENT ON TABLE T IS CONCAT('a', 'b')",
            "SELECT REMARKS FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'T'",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        Assert.True(
            java.Count == port.Count && java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
    }

    /// <summary>
    /// The answers to objects the port does not have, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void CommentsOnMissingObjectsFailAsTheReferenceFails()
    {
        string[] statements =
        [
            "CREATE TABLE T(ID INT)",
            "COMMENT ON TABLE NOSUCH IS 'x'",
            "COMMENT ON COLUMN NOSUCH.C IS 'x'",
            "COMMENT ON COLUMN T.NOSUCH IS 'x'",
            "COMMENT ON INDEX NOSUCH IS 'x'",
            "COMMENT ON SCHEMA NOSUCH IS 'x'",
            "COMMENT ON SEQUENCE NOSUCH IS 'x'",
            "COMMENT ON CONSTRAINT NOSUCH IS 'x'",
            "COMMENT ON ALIAS NOSUCH IS 'x'",
            "COMMENT ON USER NOSUCH IS 'x'",
            "COMMENT ON ROLE NOSUCH IS 'x'",
            "COMMENT ON TRIGGER NOSUCH IS 'x'",
            "COMMENT ON DOMAIN NOSUCH IS 'x'",
            "COMMENT ON CONSTANT NOSUCH IS 'x'",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        Assert.True(
            java.Count == port.Count && java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
    }

    /// <summary>
    /// Runs the statements here and writes what the question answered: the type of the value and
    /// the error, and not the value itself.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <returns>the lines</returns>
    private static List<string> RunPort(IReadOnlyList<string> statements)
    {
        SessionLocal session = new(
            new Database(new ConnectionInfo("jdbc:h2:mem:comment_on_oracle")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
        List<string> lines = [];
        foreach (string sql in statements)
        {
            try
            {
                CommandContainer container =
                    Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
                if (sql.StartsWith("SELECT", StringComparison.Ordinal))
                {
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
                else
                {
                    _ = container.ExecuteUpdate(false);
                }
            }
            catch (Exception e)
            {
                lines.Add(e is H2SharpException h2
                    ? "=" + h2.H2ErrorCode.ToString(CultureInfo.InvariantCulture)
                    : "=" + e.GetType().Name);
            }
        }

        return lines;
    }

    /// <summary>
    /// Runs the statements on the reference and writes what the question answered.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <returns>the lines</returns>
    private static List<string> RunJava(IReadOnlyList<string> statements)
        => OracleRunner.Run(
            statements,
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:comment_on_oracle");
            java.sql.Statement stat = conn.createStatement();
            for (int i = 0; i < inputs.length; i++) {
              try {
                boolean isQuery = stat.execute(inputs[i]);
                if (isQuery) {
                  java.sql.ResultSet rs = stat.getResultSet();
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
                }
              } catch (java.sql.SQLException e) {
                System.out.println("=" + e.getErrorCode());
              }
            }
            conn.close();
            """,
            extraJavaArgs: ["-Duser.language=en"]);
}

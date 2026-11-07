/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * MERGE USING with the NOT MATCHED branch, checked against the reference running the same text
 * (the matrix of the reference's dml/mergeUsing.sql): rows of the source that miss the target
 * are inserted, the matched ones are updated, both branches in one statement, guarded by their
 * own conditions, and the removal of a WHEN clause that can never fire.
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
public sealed class MergeUsingNotMatchedOracleTests
{
    [JavaOracleFact]
    public void MergeUsingNotMatchedAnswerTheReference()
    {
        string[] statements =
        [
            // The reference runs this first matrix in Oracle mode (SET MODE Oracle in
            // mergeUsing.sql); D-029 puts the mode into the open, so both sides open with
            // MODE=Oracle and the statements themselves are the same.
            "CREATE TABLE PARENT(ID INT PRIMARY KEY, NAME VARCHAR(20))",
            // Every source row misses: the insert branch fills the target.
            "MERGE INTO PARENT AS P USING (SELECT X AS ID, 'Coco'||X AS NAME FROM SYSTEM_RANGE(1,2)) AS S ON (P.ID = S.ID AND 1=1 AND S.ID = P.ID) WHEN MATCHED THEN UPDATE SET P.NAME = S.NAME WHERE 2 = 2 WHEN NOT MATCHED THEN INSERT (ID, NAME) VALUES (S.ID, S.NAME)",
            "SELECT ID, NAME FROM PARENT ORDER BY ID",
            // The same statement over its own result: now every row matches, the update branch
            // fires and the insert does not.
            "MERGE INTO PARENT AS P USING (SELECT X AS ID, 'Coco'||X AS NAME FROM SYSTEM_RANGE(1,2)) AS S ON (P.ID = S.ID AND 1=1 AND S.ID = P.ID) WHEN MATCHED THEN UPDATE SET P.NAME = S.NAME WHERE 2 = 2 WHEN NOT MATCHED THEN INSERT (ID, NAME) VALUES (S.ID, S.NAME)",
            "SELECT ID, NAME FROM PARENT ORDER BY ID",
            "DROP TABLE PARENT",
            // Two schemas, a partial match: rows 1 and 3 update, row 5 inserts.
            "CREATE SCHEMA SOURCESCHEMA",
            "CREATE TABLE SOURCESCHEMA.SOURCE(ID INT PRIMARY KEY, \"VALUE\" INT)",
            "INSERT INTO SOURCESCHEMA.SOURCE VALUES (1, 10), (3, 30), (5, 50)",
            "CREATE SCHEMA DESTSCHEMA",
            "CREATE TABLE DESTSCHEMA.DEST(ID INT PRIMARY KEY, \"VALUE\" INT)",
            "INSERT INTO DESTSCHEMA.DEST VALUES (1, 1), (3, 3)",
            "MERGE INTO DESTSCHEMA.DEST USING SOURCESCHEMA.SOURCE ON (DEST.ID = SOURCE.ID) WHEN MATCHED THEN UPDATE SET DEST.\"VALUE\" = SOURCE.\"VALUE\" WHEN NOT MATCHED THEN INSERT (ID, \"VALUE\") VALUES (SOURCE.ID, SOURCE.\"VALUE\")",
            "SELECT ID, \"VALUE\" FROM DESTSCHEMA.DEST ORDER BY ID",
            // A NOT MATCHED branch with its own condition: only the small misses insert.
            "INSERT INTO SOURCESCHEMA.SOURCE VALUES (7, 70), (9, 90)",
            "MERGE INTO DESTSCHEMA.DEST USING SOURCESCHEMA.SOURCE ON (DEST.ID = SOURCE.ID) WHEN NOT MATCHED AND SOURCE.ID < 8 THEN INSERT (ID, \"VALUE\") VALUES (SOURCE.ID, SOURCE.\"VALUE\")",
            "SELECT ID, \"VALUE\" FROM DESTSCHEMA.DEST ORDER BY ID",
            "DROP SCHEMA SOURCESCHEMA CASCADE",
            "DROP SCHEMA DESTSCHEMA CASCADE",
        ];
        RunBoth(statements, Mode.GetInstance("Oracle"));
    }

    /// <summary>
    /// Runs the statements on both sides and asserts the same answers: the rows, or the code
    /// of the error.
    /// </summary>
    /// <param name="statements">the statements</param>
    private static void RunBoth(IReadOnlyList<string> statements, Mode? mode = null)
    {
        List<string> port = RunPort(statements, mode);
        List<string> java = RunJava(statements, mode);
        Assert.True(
            java.Count == port.Count && java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
    }

    /// <summary>
    /// Runs the statements here and writes what the question answered: the rows, or the code of
    /// the error.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <returns>the lines</returns>
    private static List<string> RunPort(IReadOnlyList<string> statements, Mode? mode)
    {
        Database database = new(ConnectionInfo.Memory("merge_nm_oracle", mode));
        SessionLocal session = new(database, "sa")
        {
            CompareMode = CompareMode.GetInstance(null, 0),
        };

        // The provider must answer the database's mode (SessionCastDataProvider): the tests'
        // own TestCastDataProvider always answers Regular.
        session.CastDataProvider = new SessionCastDataProvider(session, DefaultCastDataProvider.Instance);
        List<string> lines = [];
        foreach (string sql in statements)
        {
            try
            {
                CommandContainer container =
                    Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
                if (sql.StartsWith("SELECT", StringComparison.Ordinal)
                    || sql.StartsWith("EXPLAIN", StringComparison.Ordinal))
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
                            cells.Add(value is null or ValueNull
                                ? "-"
                                // EXPLAIN plans are multi-line: one line per row on both sides.
                                : (value.GetString() ?? "-").Replace("\n", "\\n"));
                        }

                        lines.Add("=" + string.Join(":", cells));
                    }
                }
                else
                {
                    lines.Add("=" + container.ExecuteUpdate(false).GetUpdateCount());
                }
            }
            catch (Exception e)
            {
                lines.Add(e is H2SharpException h2
                    ? "=" + h2.H2ErrorCode.ToString(CultureInfo.InvariantCulture)
                    : "=" + e.GetType().Name + ":" + e.Message);
            }
        }

        session.Close();
        return lines;
    }

    /// <summary>
    /// Runs the statements on the reference and writes what the question answered.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <returns>the lines</returns>
    private static List<string> RunJava(IReadOnlyList<string> statements, Mode? mode)
        => OracleRunner.Run(
            statements,
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:MERGE_NM_URL", "sa", "");
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
                      line.append(rs.wasNull() ? "-" : s.replace("\n", "\\n"));
                    }
                    System.out.println(line);
                  }
                } else {
                  System.out.println("=" + stat.getUpdateCount());
                }
              } catch (java.sql.SQLException e) {
                System.out.println("=" + e.getErrorCode());
              }
            }
            conn.close();
            """.Replace("MERGE_NM_URL", "jdbc:h2:mem:merge_nm_oracle" + (mode is null ? "" : ";MODE=" + mode)),
            extraJavaArgs: ["-Duser.language=en"]);
}

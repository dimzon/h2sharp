/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The reference's dml/with.sql, statement by statement: recursive and plain CTEs, the
 * TABLE form, CTEs over DML-free sources, nested CTEs, a CTE in a subquery and in a view,
 * and the refusals (a duplicate CTE name, a CTE the query never defined). The EXPLAIN forms,
 * the four statements the port answers differently (WithCteBlockedPortTests) and the
 * in-script SET MODE block are left out; the mode is fixed at the open (D-029).
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
public sealed class WithCteOracleTests
{
    [JavaOracleFact]
    public void WithFormsAnswerTheReference()
    {
        string[] statements =
        [
            "create table folder(id int primary key, name varchar(255), parent int);",
            "insert into folder values(1, null, null), (2, 'bin', 1), (3, 'docs', 1), (4, 'html', 3), (5, 'javadoc', 3), (6, 'ext', 1), (7, 'service', 1), (8, 'src', 1), (9, 'docsrc', 8), (10, 'installer', 8), (11, 'main', 8), (12, 'META-INF', 11), (13, 'org', 11), (14, 'h2', 13), (15, 'test', 8), (16, 'tools', 8);",
            "with recursive link(id, name, level) as (select id, name, 0 from folder where parent is null union all select folder.id, ifnull(link.name || '/', '') || folder.name, level + 1 from link inner join folder on link.id = folder.parent) select name from link where name is not null order by cast(id as int);",
            "drop table folder;",
            "select sum(n) from ( with recursive r(n) as ( (select 1) union all (select n+1 from r where n < 3) ) select n from r );",
            "select sum(n) from ( with recursive \"r\"(n) as ( (select 1) union all (select n+1 from \"r\" where n < 3) ) select n from \"r\" );",
            "select sum(n) from (select 0) join ( with recursive r(n) as ( (select 1) union all (select n+1 from r where n < 3) ) select n from r ) on 1=1;",
            "select 0 from ( select 0 where 0 in ( with recursive r(n) as ( (select 1) union all (select n+1 from r where n < 3) ) select n from r ) );",
            "with recursive r0(n,k) as (select -1, 0), r1(n,k) as ((select 1, 0) union all (select n+1,k+1 from r1 where n <= 3)), r2(n,k) as ((select 10,0) union all (select n+1,k+1 from r2 where n <= 13)) select r1.k, r0.n as N0, r1.n AS N1, r2.n AS n2 from r0 inner join r1 ON r1.k= r0.k inner join r2 ON r1.k= r2.k;",
            "CREATE SCHEMA SCH;",
            "WITH RECURSIVE R1 AS ( (SELECT 1) UNION ALL (SELECT (N + 1) FROM R1 WHERE N < 3)) TABLE R1;",
            "WITH R1(N) AS ( (SELECT 1) UNION ALL (SELECT (N + 1) FROM R1 WHERE N < 3)) TABLE R1;",
            "WITH RECURSIVE R1(A) AS (SELECT 1) SELECT A FROM R1 WHERE A IN (WITH RECURSIVE R2(B) AS (SELECT 1) TABLE R2);",
            "CREATE VIEW SCH.R2(N) AS WITH RECURSIVE R1(N) AS ( (SELECT 1) UNION ALL (SELECT (N + 1) FROM R1 WHERE N < 3)) TABLE R1;",
            "SELECT * FROM SCH.R2;",
            "WITH CTE_TEST AS (SELECT 1, 2) SELECT * FROM CTE_TEST;",
            "WITH CTE_TEST AS (SELECT 1, 2) (SELECT * FROM CTE_TEST);",
            "WITH CTE_TEST AS (SELECT 1, 2) ((SELECT * FROM CTE_TEST));",
            "CREATE TABLE TEST(A INT, B INT) AS SELECT 1, 2;",
            "WITH CTE_TEST AS (TABLE TEST) ((SELECT * FROM CTE_TEST));",
            "WITH CTE_TEST AS (TABLE TEST) ((TABLE CTE_TEST));",
            "WITH CTE_TEST AS (VALUES (1, 2)) ((SELECT * FROM CTE_TEST));",
            "WITH CTE_TEST AS (TABLE TEST) ((SELECT A, B FROM CTE_TEST2));",
            "WITH CTE_TEST AS (TABLE TEST) ((SELECT A, B, C FROM CTE_TEST));",
            "DROP TABLE TEST;",
            "WITH RECURSIVE V(V1, V2) AS ( SELECT 0 V1, 1 V2 UNION ALL SELECT V1 + 1, V2 + 1 FROM V WHERE V2 < 4 ) SELECT V1, V2, COUNT(*) FROM V LEFT JOIN (SELECT T1 / T2 R FROM (VALUES (10, 0)) T(T1, T2) WHERE T2*T2*T2*T2*T2*T2 <> 0) X ON X.R > V.V1 AND X.R < V.V2 GROUP BY V1, V2;",
            "CREATE TABLE TEST(\"VALUE\" INT NOT NULL PRIMARY KEY);",
            "DROP TABLE TEST;",
            "CREATE TABLE T(C INT);",
            "INSERT INTO T WITH W(C) AS (VALUES 1) SELECT C FROM W;",
            "TABLE W;",
            "TABLE T;",
            "WITH T(X) AS (SELECT 1) (SELECT 2 Y) UNION (SELECT 3 Z) UNION (SELECT * FROM T);",
            "WITH T1(F1, F2) AS (SELECT 1, 2) SELECT A1.F1, A1.F2 FROM (SELECT * FROM T1) A1;",
            "CREATE VIEW V AS WITH A AS (SELECT) TABLE A;",
            "DROP VIEW V;",
            "WITH Q1(X) AS ( WITH Q1(Y) AS (VALUES 1) SELECT Q1.Y FROM Q1 ) SELECT Q1.X FROM Q1;",
            "WITH Q1(X) AS (VALUES 1), Q1(X) AS (VALUES 2) TABLE Q1;"
        ];
        RunBoth(statements);
    }

    /// <summary>
    /// Runs the statements on both sides and asserts the same answers: the rows, or the code of
    /// the error.
    /// </summary>
    /// <param name="statements">the statements</param>
    private static void RunBoth(IReadOnlyList<string> statements)
    {
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
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
    private static List<string> RunPort(IReadOnlyList<string> statements)
    {
        SessionLocal session = new(
            new Database(ConnectionInfo.Memory("with_cte_oracle")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
        List<string> lines = [];
        for (int si = 0; si < statements.Count; si++)
        {
            string sql = statements[si];
            try
            {
                CommandContainer container =
                    Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
                if (sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                    || sql.StartsWith("EXPLAIN", StringComparison.OrdinalIgnoreCase)
                    || sql.StartsWith("TABLE", StringComparison.OrdinalIgnoreCase)
                    || sql.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
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

                        lines.Add("=" + si + ":" + string.Join(":", cells));
                    }
                }
                else
                {
                    lines.Add("=" + si + ":" + container.ExecuteUpdate(false).GetUpdateCount());
                }
            }
            catch (Exception e)
            {
                lines.Add(e is H2SharpException h2
                    ? "=" + si + ":" + h2.H2ErrorCode.ToString(CultureInfo.InvariantCulture)
                    : "=" + si + ":" + e.GetType().Name + ":" + e.Message);
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
    private static List<string> RunJava(IReadOnlyList<string> statements)
        => OracleRunner.Run(
            statements,
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:with_cte_oracle", "sa", "");
            java.sql.Statement stat = conn.createStatement();
            for (int i = 0; i < inputs.length; i++) {
              try {
                boolean isQuery = stat.execute(inputs[i]);
                if (isQuery) {
                  java.sql.ResultSet rs = stat.getResultSet();
                  int columns = rs.getMetaData().getColumnCount();
                  while (rs.next()) {
                    StringBuilder line = new StringBuilder("=" + i + ":");
                    for (int c = 1; c <= columns; c++) {
                      if (c > 1) {
                        line.append(':');
                      }
                      String s = rs.getString(c);
                      line.append(rs.wasNull() ? "-" : s);
                    }
                    System.out.println(line);
                  }
                } else {
                  System.out.println("=" + i + ":" + stat.getUpdateCount());
                }
              } catch (java.sql.SQLException e) {
                System.out.println("=" + i + ":" + e.getErrorCode());
              }
            }
            conn.close();
            """,
            extraJavaArgs: ["-Duser.language=en"]);
}

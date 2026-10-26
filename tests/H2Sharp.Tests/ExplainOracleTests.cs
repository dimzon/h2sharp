/* Copyright 2026 H2Sharp contributors. MPL 2.0 or EPL 1.0.
 * EXPLAIN and EXPLAIN ANALYZE, compared with H2 2.5 on the JVM. */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ExplainOracleTests
{
    [JavaOracleFact]
    public void ExplainSelectAndAnalyze()
    {
        Check([
            "EXPLAIN SELECT 1",
            "EXPLAIN SELECT * FROM DUAL",
            "EXPLAIN PLAN FOR SELECT 1",
            "EXPLAIN SELECT * FROM SYSTEM_RANGE(1, 3) WHERE X > 1",
            "CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR)",
            "INSERT INTO T VALUES(1, 'a'), (2, 'b')",
            "EXPLAIN SELECT * FROM T",
            "EXPLAIN SELECT * FROM T WHERE ID IS NULL",
            "EXPLAIN SELECT * FROM T WHERE ID = 1",
            "EXPLAIN ANALYZE SELECT * FROM T WHERE ID = 1",
            "EXPLAIN SELECT * FROM T ORDER BY ID",
        ]);
    }

    [JavaOracleFact]
    public void ExplainUpdateAndDelete()
    {
        Check([
            "CREATE TABLE U(A INT, B INT DEFAULT 5)",
            "INSERT INTO U(A) VALUES(1), (2)",
            "EXPLAIN UPDATE U SET B = DEFAULT WHERE A = 2",
            "EXPLAIN DELETE FROM U WHERE A = 1",
            "EXPLAIN UPDATE U SET (A, B) = (2, 3) WHERE A = 1",
            "UPDATE U SET B = DEFAULT WHERE A = 2",
            "SELECT A, B FROM U ORDER BY A",
        ]);
    }

    private static void Check(string[] statements)
    {
        List<string> port = [];
        using H2Connection connection = new("jdbc:h2:mem:explain_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        for (int i = 0; i < statements.Length; i++)
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = statements[i];
            try
            {
                if (statements[i].StartsWith("EXPLAIN", StringComparison.OrdinalIgnoreCase)
                    || statements[i].StartsWith("SELECT", StringComparison.Ordinal))
                {
                    using DbDataReader reader = command.ExecuteReader();
                    port.Add($"={i}:C:{reader.FieldCount}");
                    while (reader.Read())
                    {
                        List<string> cells = [];
                        for (int c = 0; c < reader.FieldCount; c++)
                            cells.Add(reader.IsDBNull(c) ? "NULL"
                                : Convert.ToString(reader.GetValue(c), CultureInfo.InvariantCulture)!
                                    .Replace("\n", "\\n", StringComparison.Ordinal));
                        port.Add($"={i}:R:" + string.Join('|', cells));
                    }
                }
                else port.Add($"={i}:U:{command.ExecuteNonQuery()}");
            }
            catch (H2SharpException e) { port.Add($"={i}:E:{e.H2ErrorCode}"); }
        }

        List<string> java = OracleRunner.Run(statements, """
            try(java.sql.Connection c=java.sql.DriverManager.getConnection("jdbc:h2:mem:explain")) {
              java.sql.Statement s=c.createStatement();
              for(int i=0;i<inputs.length;i++) {
                try {
                  if(!s.execute(inputs[i])) { System.out.println("="+i+":U:"+s.getUpdateCount()); continue; }
                  java.sql.ResultSet r=s.getResultSet();
                  java.sql.ResultSetMetaData m=r.getMetaData();
                  System.out.println("="+i+":C:"+m.getColumnCount());
                  while(r.next()) {
                    StringBuilder b=new StringBuilder("="+i+":R:");
                    for(int j=1;j<=m.getColumnCount();j++) {
                      if(j>1) b.append('|');
                      Object o=r.getObject(j);
                      b.append(o==null?"NULL":o.toString().replace("\n", "\\n"));
                    }
                    System.out.println(b);
                  }
                } catch(java.sql.SQLException e) {
                  System.out.println("="+i+":E:"+e.getErrorCode());
                }
              }
            }
            """);
        Assert.Equal(java, port);
    }
}

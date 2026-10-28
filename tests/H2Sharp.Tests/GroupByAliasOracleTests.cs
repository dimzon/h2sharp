/* Copyright 2026 H2Sharp contributors. MPL 2.0 or EPL 1.0.
 * GROUP BY name resolution: a qualified column is not the select-list alias. */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class GroupByAliasOracleTests
{
    [JavaOracleFact]
    public void GroupByQualifiedColumnAndAliasAreDifferent()
    {
        Check([
            "CREATE TABLE T(ID INT)",
            "INSERT INTO T VALUES(1), (2), (1)",
            "SELECT 0 AS X FROM SYSTEM_RANGE(1, 2) D GROUP BY D.X",
            "SELECT 0 AS X FROM SYSTEM_RANGE(1, 2) D GROUP BY X",
            "SELECT ID AS X, COUNT(*) FROM T GROUP BY T.ID ORDER BY T.ID",
            "SELECT ID AS X, COUNT(*) FROM T GROUP BY X ORDER BY X",
            "SELECT D.X AS Y FROM SYSTEM_RANGE(1, 2) D GROUP BY Y ORDER BY Y",
            "SELECT D.X AS Y FROM SYSTEM_RANGE(1, 2) D GROUP BY D.X ORDER BY Y",
        ]);
    }

    [JavaOracleFact]
    public void HavingUsesSelectListAliases()
    {
        Check([
            "SELECT X / 3 AS A, COUNT(*) C FROM SYSTEM_RANGE(1, 10) GROUP BY A HAVING C > 2",
            "CREATE TABLE T(ID INT)",
            "INSERT INTO T VALUES(1), (2)",
            "SELECT ID + 1 AS X, COUNT(*) FROM T GROUP BY X HAVING X > 2",
            "SELECT ID + 1 AS X, COUNT(*) FROM T GROUP BY 1",
            "SELECT ID AS X, COUNT(*) FROM T GROUP BY ID HAVING X > 1",
        ]);
    }

    private static void Check(string[] statements)
    {
        List<string> port = [];
        using H2Connection connection = new("Data Source=group_alias_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        for (int i = 0; i < statements.Length; i++)
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = statements[i];
            try
            {
                if (statements[i].StartsWith("SELECT", StringComparison.Ordinal))
                {
                    using DbDataReader reader = command.ExecuteReader();
                    port.Add($"={i}:C:{reader.FieldCount}");
                    while (reader.Read())
                    {
                        List<string> cells = [];
                        for (int c = 0; c < reader.FieldCount; c++)
                            cells.Add(reader.IsDBNull(c) ? "NULL"
                                : Convert.ToString(reader.GetValue(c), CultureInfo.InvariantCulture)!);
                        port.Add($"={i}:R:" + string.Join('|', cells));
                    }
                }
                else port.Add($"={i}:U:{command.ExecuteNonQuery()}");
            }
            catch (H2SharpException e) { port.Add($"={i}:E:{e.H2ErrorCode}"); }
        }

        List<string> java = OracleRunner.Run(statements, """
            try(java.sql.Connection c=java.sql.DriverManager.getConnection("jdbc:h2:mem:group_alias")) {
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
                      b.append(o==null?"NULL":o.toString());
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

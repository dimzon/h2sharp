/* Copyright 2026 H2Sharp contributors. MPL 2.0 or EPL 1.0.
 * GROUP BY integer position is mode-dependent in H2 2.5. A mode is fixed when the
 * database is opened, so each case opens its own database with that mode. */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class GroupByColumnIndexOracleTests
{
    [JavaOracleFact]
    public void GroupByPositionFollowsTheModeAndErrorRulesOfTheReference()
    {
        Check("MySQL", [
            "CREATE TABLE MYTAB(X INT, Y INT, Z INT) AS VALUES (1,123,2), (1,456,2), (3,789,4)",
            "SELECT SUM(Y) AS S, X + Z FROM MYTAB GROUP BY 2",
            "SELECT SUM(Y) AS S, X + Z FROM MYTAB GROUP BY 3",
        ]);
        Check("MariaDB", [
            "CREATE TABLE MYTAB(X INT, Y INT, Z INT) AS VALUES (1,123,2), (1,456,2), (3,789,4)",
            "SELECT SUM(Y) AS S, X + Z FROM MYTAB GROUP BY 2",
        ]);
        Check("PostgreSQL", [
            "CREATE TABLE MYTAB(X INT, Y INT, Z INT) AS VALUES (1,123,2), (1,456,2), (3,789,4)",
            "SELECT SUM(Y) AS S, X + Z FROM MYTAB GROUP BY 2",
        ]);
        Check("Oracle", [
            "CREATE TABLE MYTAB(X INT, Y INT, Z INT) AS VALUES (1,123,2), (1,456,2), (3,789,4)",
            "SELECT SUM(Y) AS S, X FROM MYTAB GROUP BY 2",
        ]);
        Check("Regular", [
            "CREATE TABLE MYTAB(X INT, Y INT, Z INT) AS VALUES (1,123,2), (1,456,2), (3,789,4)",
            "SELECT SUM(Y) AS S, X + Z FROM MYTAB GROUP BY 2",
            "SELECT 7 FROM MYTAB GROUP BY 1",
            "SELECT SUM(Y) AS S, X + Z FROM MYTAB GROUP BY 1",
            "SELECT SUM(Y) AS S, X + Z FROM MYTAB GROUP BY 0",
        ]);
    }

    private static void Check(string mode, string[] statements)
    {
        List<string> port = [];
        using H2Connection connection = new(
            "Data Source=group_by_position_" + Guid.NewGuid().ToString("N")
            + ";Storage=Memory;Mode=" + mode);
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
            try(java.sql.Connection c=java.sql.DriverManager.getConnection("jdbc:h2:mem:group_by_position;MODE=" + System.getProperty("h2sharp.mode"))) {
              java.sql.Statement s=c.createStatement();
              for(int i=0;i<inputs.length;i++) {
                try {
                  if(!s.execute(inputs[i])) { System.out.println("="+i+":U:"+s.getUpdateCount()); continue; }
                  java.sql.ResultSet r=s.getResultSet(); int count=r.getMetaData().getColumnCount();
                  System.out.println("="+i+":C:"+count);
                  while(r.next()) {
                    StringBuilder b=new StringBuilder("="+i+":R:");
                    for(int col=1;col<=count;col++) { if(col>1)b.append('|'); String v=r.getString(col); b.append(v==null?"NULL":v); }
                    System.out.println(b);
                  }
                } catch(java.sql.SQLException e) { System.out.println("="+i+":E:"+e.getErrorCode()); }
              }
            }
            """, extraJavaArgs: ["-Duser.language=en", "-Dh2sharp.mode=" + mode]);
        Assert.True(java.SequenceEqual(port), "java:\n" + string.Join("\n", java) + "\nport:\n" + string.Join("\n", port));
    }
}

/* Copyright 2026 H2Sharp contributors. MPL 2.0 or EPL 1.0.
 * TOP in SELECT/UPDATE/DELETE and explicit DUAL, compared with H2 2.5 on the JVM. */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class TopClauseOracleTests
{
    [JavaOracleFact]
    public void SelectTopInRegularModeAndExplicitDual()
    {
        Check("Regular", [
            "CREATE TABLE T(ID INT)",
            "INSERT INTO T VALUES(1), (2), (3), (4)",
            "SELECT TOP 2 * FROM T ORDER BY ID",
            "SELECT TOP (2) * FROM T ORDER BY ID",
            "SELECT TOP 50 PERCENT * FROM T ORDER BY ID",
            "SELECT TOP 1 WITH TIES * FROM T ORDER BY ID",
            "SELECT 1 FROM DUAL",
            "SELECT 1 FROM SYS.DUAL",
            "SELECT 1 FROM PUBLIC.DUAL",
            "UPDATE TOP (1) T SET ID = 10",
        ]);
    }

    [JavaOracleFact]
    public void UpdateAndDeleteTopInSqlServerAndLegacyModes()
    {
        Check("MSSQLServer", [
            "CREATE TABLE T(ID INT, V INT)",
            "INSERT INTO T VALUES(1, 0), (2, 0), (3, 0), (4, 0)",
            "UPDATE TOP (2) T SET V = 1",
            "SELECT ID, V FROM T ORDER BY ID",
            "DELETE TOP (1) FROM T",
            "SELECT ID, V FROM T ORDER BY ID",
            "UPDATE TOP (1) T SET ID = 20, V = 2",
            "SELECT ID, V FROM T ORDER BY ID",
        ]);

        Check("Legacy", [
            "CREATE TABLE T(ID INT, V INT)",
            "INSERT INTO T VALUES(1, 0), (2, 0), (3, 0), (4, 0)",
            "UPDATE TOP (1) T SET V = 3",
            "DELETE TOP (1) FROM T",
            "SELECT ID, V FROM T ORDER BY ID",
            "SELECT TOP 1 * FROM T ORDER BY ID",
        ]);
    }

    private static void Check(string mode, string[] statements)
    {
        List<string> port = [];
        using H2Connection connection = new(
            "Data Source=top_" + Guid.NewGuid().ToString("N")
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
            try(java.sql.Connection c=java.sql.DriverManager.getConnection("jdbc:h2:mem:top;MODE=" + System.getProperty("h2sharp.mode"))) {
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
            """, extraJavaArgs: ["-Dh2sharp.mode=" + mode]);
        Assert.Equal(java, port);
    }
}

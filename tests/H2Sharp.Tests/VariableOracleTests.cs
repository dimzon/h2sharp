/* Copyright 2026 H2Sharp contributors. MPL 2.0 or EPL 1.0.
 * Session variables @name and SET(), compared with H2 2.5. */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class VariableOracleTests
{
    [JavaOracleFact]
    public void VariableAssignmentAndSetFunction()
    {
        Check([
            "SELECT @N := CASE WHEN X = 1 THEN 1 ELSE @N * X END F FROM SYSTEM_RANGE(1, 4)",
            "SELECT X, SET(@T, COALESCE(@T, 0) + X) FROM SYSTEM_RANGE(1, 3)",
            "SELECT @T",
        ]);
    }

    private static void Check(string[] statements)
    {
        List<string> port = [];
        using H2Connection connection = new("Data Source=variable_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        for (int i = 0; i < statements.Length; i++)
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = statements[i];
            try
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
            catch (H2SharpException e) { port.Add($"={i}:E:{e.H2ErrorCode}"); }
        }

        List<string> java = OracleRunner.Run(statements, """
            try(java.sql.Connection c=java.sql.DriverManager.getConnection("jdbc:h2:mem:variable")) {
              java.sql.Statement s=c.createStatement();
              for(int i=0;i<inputs.length;i++) {
                try {
                  java.sql.ResultSet r=s.executeQuery(inputs[i]);
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

/* Copyright 2026 H2Sharp contributors. MPL 2.0 or EPL 1.0.
 * SCRIPT NODATA/NOPASSWORDS/NOSETTINGS/NOVERSION, compared with H2 2.5. */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ScriptOracleTests
{
    [JavaOracleFact]
    public void ScriptSchemaAndData()
    {
        Check([
            "CREATE DOMAIN INT AS VARCHAR",
            "CREATE MEMORY TABLE TEST(ID INT)",
            "SCRIPT NODATA NOPASSWORDS NOSETTINGS NOVERSION",
            "CREATE MEMORY TABLE TEST2(ID INT, D DOUBLE, F FLOAT)",
            "INSERT INTO TEST2 VALUES(0, POWER(0, -1), POWER(0, -1)), (1, -POWER(0, -1), -POWER(0, -1)), (2, SQRT(-1), SQRT(-1))",
            "SCRIPT NOPASSWORDS NOSETTINGS NOVERSION",
        ]);
    }

    private static void Check(string[] statements)
    {
        List<string> port = [];
        using H2Connection portConnection = new("Data Source=script_port_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        portConnection.Open();
        for (int i = 0; i < statements.Length; i++)
        {
            using DbCommand command = portConnection.CreateCommand();
            command.CommandText = statements[i];
            try
            {
                if (statements[i].StartsWith("SCRIPT", StringComparison.Ordinal))
                {
                    using DbDataReader reader = command.ExecuteReader();
                    port.Add($"={i}:C:{reader.FieldCount}");
                    while (reader.Read())
                    {
                        port.Add($"={i}:R:" + Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)!
                            .Replace("\r", "", StringComparison.Ordinal)
                            .Replace("\n", "<NL>", StringComparison.Ordinal));
                    }
                }
                else port.Add($"={i}:U:{command.ExecuteNonQuery()}");
            }
            catch (H2SharpException e) { port.Add($"={i}:E:{e.H2ErrorCode}"); }
        }

        List<string> java = OracleRunner.Run(statements, """
            try(java.sql.Connection c=java.sql.DriverManager.getConnection("jdbc:h2:mem:script")) {
              java.sql.Statement s=c.createStatement();
              for(int i=0;i<inputs.length;i++) {
                try {
                  if(!s.execute(inputs[i])) { System.out.println("="+i+":U:"+s.getUpdateCount()); continue; }
                  java.sql.ResultSet r=s.getResultSet();
                  java.sql.ResultSetMetaData m=r.getMetaData();
                  System.out.println("="+i+":C:"+m.getColumnCount());
                  while(r.next()) {
                    String line = r.getString(1);
                    if (line.startsWith("CREATE USER") || line.startsWith("CREATE ROLE")
                        || line.startsWith("GRANT") || line.startsWith("REVOKE")) continue;
                    System.out.println("="+i+":R:"+line.replace("\r", "").replace("\n", "<NL>"));
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

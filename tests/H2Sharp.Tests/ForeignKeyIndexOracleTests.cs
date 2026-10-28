/* Copyright 2026 H2Sharp contributors. MPL 2.0 or EPL 1.0.
 * REFERENCES ... INDEX is a hint for the referenced index, not a required object. */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ForeignKeyIndexOracleTests
{
    [JavaOracleFact]
    public void MissingReferencedIndexIsIgnoredWhenAUniqueKeyExists()
    {
        Check([
            "CREATE TABLE P(ID INT PRIMARY KEY, V INT)",
            "CREATE TABLE C(ID INT, PID INT)",
            "ALTER TABLE C ADD CONSTRAINT FK FOREIGN KEY(PID) REFERENCES P(ID) INDEX NO_SUCH_INDEX",
            "INSERT INTO P VALUES(1, 10)",
            "INSERT INTO C VALUES(1, 1)",
            "INSERT INTO C VALUES(2, 2)",
            "CREATE INDEX IDX_P_ID ON P(ID)",
            "ALTER TABLE C ADD CONSTRAINT FK2 FOREIGN KEY(PID) REFERENCES P(ID) INDEX IDX_P_ID",
            "INSERT INTO C VALUES(3, 1)",
            "SELECT ID, PID FROM C ORDER BY ID",
        ]);
    }

    private static void Check(string[] statements)
    {
        List<string> port = [];
        using H2Connection connection = new("Data Source=fk_index_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
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
            try(java.sql.Connection c=java.sql.DriverManager.getConnection("jdbc:h2:mem:fk_index")) {
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

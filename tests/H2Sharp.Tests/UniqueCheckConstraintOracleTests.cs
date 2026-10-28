/* Copyright 2026 H2Sharp contributors. MPL 2.0 or EPL 1.0.
 * UNIQUE and CHECK constraints, compared with H2 2.5 on the JVM. */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class UniqueCheckConstraintOracleTests
{
    [JavaOracleFact]
    public void ColumnAndTableUniqueAndCheckConstraints()
    {
        Check([
            "CREATE TABLE T(ID INT UNIQUE, NAME VARCHAR(10) CHECK (LENGTH(NAME) > 1), V INT, CONSTRAINT U_V UNIQUE(V), CONSTRAINT C_V CHECK (V >= 0))",
            "INSERT INTO T VALUES(1, 'ab', 1)",
            "INSERT INTO T VALUES(2, 'cd', 2)",
            "INSERT INTO T VALUES(1, 'ef', 3)",
            "INSERT INTO T VALUES(3, 'a', 4)",
            "INSERT INTO T VALUES(4, 'gh', -1)",
            "INSERT INTO T VALUES(5, 'ij', 2)",
            "UPDATE T SET V = 1 WHERE ID = 2",
            "UPDATE T SET NAME = 'x' WHERE ID = 1",
            "SELECT ID, NAME, V FROM T ORDER BY ID",
        ], allowErrors: true);
    }

    [JavaOracleFact]
    public void AlterTableAddUniqueAndCheck()
    {
        Check([
            "CREATE TABLE T(ID INT, V INT)",
            "INSERT INTO T VALUES(1, 1), (2, 2)",
            "ALTER TABLE T ADD CONSTRAINT U_ID UNIQUE(ID)",
            "ALTER TABLE T ADD CONSTRAINT C_V CHECK (V > 0)",
            "INSERT INTO T VALUES(1, 3)",
            "INSERT INTO T VALUES(3, 0)",
            "ALTER TABLE T ADD CONSTRAINT C_V2 CHECK (V < 0)",
            "ALTER TABLE T ADD CONSTRAINT C_V3 CHECK (V < 0) NOCHECK",
            "SELECT ID, V FROM T ORDER BY ID",
        ], allowErrors: true);
    }

    [JavaOracleFact]
    public void UniqueNullsTreatment()
    {
        Check([
            "CREATE TABLE T(A INT, B INT, CONSTRAINT U UNIQUE NULLS DISTINCT(A), CONSTRAINT U2 UNIQUE NULLS NOT DISTINCT(B))",
            "INSERT INTO T VALUES(NULL, NULL)",
            "INSERT INTO T VALUES(NULL, NULL)",
            "INSERT INTO T VALUES(1, NULL)",
            "SELECT A, B FROM T ORDER BY A, B",
        ], allowErrors: true);
    }

    private static void Check(string[] statements, bool allowErrors = false)
    {
        List<string> port = [];
        using H2Connection connection = new("Data Source=unique_check_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
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
            try(java.sql.Connection c=java.sql.DriverManager.getConnection("jdbc:h2:mem:unique_check")) {
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

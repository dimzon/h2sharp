/* Copyright 2026 H2Sharp contributors. MPL 2.0 or EPL 1.0.
 * DROP SCHEMA and unary-minus column labels, compared with H2 2.5. */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class SchemaDropAndUnaryAliasOracleTests
{
    [JavaOracleFact]
    public void DropSchemaCascadeAndRestrict()
    {
        Check([
            "CREATE SCHEMA S",
            "CREATE TABLE S.T(ID INT)",
            "INSERT INTO S.T VALUES(1)",
            "DROP SCHEMA S RESTRICT",
            "DROP SCHEMA S CASCADE",
            "SELECT * FROM S.T",
            "DROP SCHEMA IF EXISTS S",
            "DROP SCHEMA S",
        ]);
    }

    [JavaOracleFact]
    public void UnaryMinusKeepsQualifiedColumnLabel()
    {
        CheckNames([
            "CREATE TABLE TEST(A INT)",
            "INSERT INTO TEST VALUES(1), (2)",
            "SELECT -TEST.A FROM TEST ORDER BY TEST.A",
        ]);
    }

    private static void Check(string[] statements)
    {
        List<string> port = [];
        using H2Connection connection = new("jdbc:h2:mem:schema_drop_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        for (int i = 0; i < statements.Length; i++)
        {
            WritePort(connection, statements, i, port, includeNames: false);
        }

        List<string> java = OracleRunner.Run(statements, """
            try(java.sql.Connection c=java.sql.DriverManager.getConnection("jdbc:h2:mem:schema_drop")) {
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

    private static void CheckNames(string[] statements)
    {
        List<string> port = [];
        using H2Connection connection = new("jdbc:h2:mem:unary_label_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        for (int i = 0; i < statements.Length; i++)
        {
            WritePort(connection, statements, i, port, includeNames: true);
        }

        List<string> java = OracleRunner.Run(statements, """
            try(java.sql.Connection c=java.sql.DriverManager.getConnection("jdbc:h2:mem:unary_label")) {
              java.sql.Statement s=c.createStatement();
              for(int i=0;i<inputs.length;i++) {
                try {
                  if(!s.execute(inputs[i])) { System.out.println("="+i+":U:"+s.getUpdateCount()); continue; }
                  java.sql.ResultSet r=s.getResultSet();
                  java.sql.ResultSetMetaData m=r.getMetaData();
                  System.out.println("="+i+":C:"+m.getColumnCount());
                  System.out.println("="+i+":N:"+m.getColumnName(1));
                  while(r.next()) {
                    System.out.println("="+i+":R:"+r.getObject(1));
                  }
                } catch(java.sql.SQLException e) {
                  System.out.println("="+i+":E:"+e.getErrorCode());
                }
              }
            }
            """);
        Assert.Equal(java, port);
    }

    private static void WritePort(H2Connection connection, string[] statements, int i,
        List<string> output, bool includeNames)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = statements[i];
        try
        {
            if (statements[i].StartsWith("SELECT", StringComparison.Ordinal))
            {
                using DbDataReader reader = command.ExecuteReader();
                output.Add($"={i}:C:{reader.FieldCount}");
                if (includeNames) output.Add($"={i}:N:{reader.GetName(0)}");
                while (reader.Read())
                {
                    List<string> cells = [];
                    for (int c = 0; c < reader.FieldCount; c++)
                        cells.Add(reader.IsDBNull(c) ? "NULL"
                            : Convert.ToString(reader.GetValue(c), CultureInfo.InvariantCulture)!);
                    output.Add($"={i}:R:" + string.Join('|', cells));
                }
            }
            else output.Add($"={i}:U:{command.ExecuteNonQuery()}");
        }
        catch (H2SharpException e) { output.Add($"={i}:E:{e.H2ErrorCode}"); }
    }
}

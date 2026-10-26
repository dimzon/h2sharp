/* Copyright 2026 H2Sharp contributors. MPL 2.0 or EPL 1.0.
 * IDENTITY shorthand, domain-name conflicts and multiple star items. */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ShorthandAndStarOracleTests
{
    [JavaOracleFact]
    public void IdentityDataTypeShorthand()
    {
        Check([
            "CREATE TABLE T(ID IDENTITY, NAME VARCHAR)",
            "INSERT INTO T(NAME) VALUES('a')",
            "INSERT INTO T(NAME) VALUES('b')",
            "SELECT ID, NAME FROM T ORDER BY ID",
            "CREATE TABLE U(ID IDENTITY(10, 5), V INT)",
            "INSERT INTO U(V) VALUES(1), (2)",
            "SELECT ID, V FROM U ORDER BY ID",
            "CREATE TABLE W(ID INT, V INT)",
            "ALTER TABLE W ALTER COLUMN ID IDENTITY",
            "INSERT INTO W(V) VALUES(7)",
            "SELECT ID, V FROM W",
        ]);
    }

    [JavaOracleFact]
    public void AutoIncrementAndRenameTable()
    {
        Check([
            "CREATE TABLE P(ID BIGINT AUTO_INCREMENT, V INT)",
            "INSERT INTO P(V) VALUES(1), (2)",
            "SELECT ID, V FROM P ORDER BY ID",
            "CREATE TABLE Q(ID INT AUTO_INCREMENT PRIMARY KEY, V INT)",
            "INSERT INTO Q(V) VALUES(3)",
            "SELECT ID, V FROM Q",
            "ALTER TABLE P RENAME TO R",
            "SELECT ID, V FROM R ORDER BY ID",
            "SELECT * FROM P",
            "ALTER TABLE INFORMATION_SCHEMA.INFORMATION_SCHEMA_CATALOG_NAME RENAME TO INFORMATION_SCHEMA.CAT",
        ]);
    }

    [JavaOracleFact]
    public void SerialAndBigSerialShorthand()
    {
        Check([
            "CREATE TABLE S(ID SERIAL, V INT)",
            "INSERT INTO S(V) VALUES(1), (2)",
            "SELECT ID, V FROM S ORDER BY ID",
            "CREATE TABLE B(ID BIGSERIAL NOT NULL PRIMARY KEY, V INT)",
            "INSERT INTO B(V) VALUES(3), (4)",
            "SELECT ID, V FROM B ORDER BY ID",
        ]);
    }

    [JavaOracleFact]
    public void DomainNameConflictsWithBuiltInType()
    {
        Check([
            "CREATE DOMAIN INTEGER AS VARCHAR",
            "CREATE DOMAIN INT AS VARCHAR",
            "CREATE DOMAIN IF NOT EXISTS INT AS VARCHAR",
            "DROP DOMAIN INT",
        ]);
    }

    [JavaOracleFact]
    public void MultipleQualifiedStars()
    {
        Check([
            "CREATE TABLE T1(K INT, A INT)",
            "CREATE TABLE T2(K INT, B INT)",
            "INSERT INTO T1 VALUES(1, 10), (2, 20)",
            "INSERT INTO T2 VALUES(1, 30), (2, 40)",
            "SELECT T1.*, T2.* FROM T1 JOIN T2 ON T1.K = T2.K ORDER BY T1.K",
            "SELECT * FROM T1 ORDER BY K",
        ]);
    }

    private static void Check(string[] statements)
    {
        List<string> port = [];
        using H2Connection connection = new("jdbc:h2:mem:shorthand_" + Guid.NewGuid().ToString("N"));
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
            try(java.sql.Connection c=java.sql.DriverManager.getConnection("jdbc:h2:mem:shorthand")) {
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

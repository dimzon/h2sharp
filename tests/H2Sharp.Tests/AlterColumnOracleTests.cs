/* Copyright 2026 H2Sharp contributors. MPL 2.0 or EPL 1.0.
 * ALTER COLUMN lifecycle and errors, compared statement-by-statement with H2 2.5.259. */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class AlterColumnOracleTests
{
    [JavaOracleFact]
    public void DefaultsNullsOnUpdateAndSelectivityMatchTheReference()
    {
        Check([
            "CREATE TABLE T(ID INT, V VARCHAR(20) DEFAULT 'old')",
            "INSERT INTO T(ID) VALUES(1)",
            "ALTER TABLE T ALTER COLUMN V SET DEFAULT 'new'",
            "INSERT INTO T(ID) VALUES(2)",
            "ALTER TABLE T ALTER COLUMN V DROP EXPRESSION",
            "SELECT COLUMN_DEFAULT FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='T' AND COLUMN_NAME='V'",
            "ALTER TABLE T ALTER COLUMN V SET DEFAULT ON NULL",
            "INSERT INTO T VALUES(3,NULL)",
            "ALTER TABLE T ALTER COLUMN V DROP DEFAULT ON NULL",
            "INSERT INTO T VALUES(4,NULL)",
            "ALTER TABLE T ALTER COLUMN V SET ON UPDATE 'changed'",
            "UPDATE T SET ID=10 WHERE ID=1",
            "ALTER TABLE T ALTER COLUMN V DROP ON UPDATE",
            "UPDATE T SET ID=20 WHERE ID=2",
            "ALTER TABLE T ALTER COLUMN V DROP DEFAULT",
            "INSERT INTO T(ID) VALUES(5)",
            "ALTER TABLE T ALTER COLUMN V SELECTIVITY 150",
            "SELECT SELECTIVITY FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='T' AND COLUMN_NAME='V'",
            "ALTER TABLE T ALTER COLUMN V SELECTIVITY -1",
            "SELECT SELECTIVITY FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='T' AND COLUMN_NAME='V'",
            "SELECT ID,V FROM T ORDER BY ID",
        ]);
    }

    [JavaOracleFact]
    public void VisibilityAffectsWildcardsAndImplicitInsertColumnsOnly()
    {
        Check([
            "CREATE TABLE T(ID INT, V VARCHAR(20) DEFAULT 'hidden')",
            "INSERT INTO T VALUES(1,'a')",
            "ALTER TABLE T ALTER COLUMN V SET INVISIBLE",
            "SELECT V FROM T",
            "SELECT * FROM T",
            "SELECT T.* FROM T",
            "INSERT INTO T VALUES(2)",
            "SELECT ID,V FROM T ORDER BY ID",
            "ALTER TABLE T ALTER COLUMN V SET VISIBLE",
            "SELECT * FROM T ORDER BY ID",
        ]);
    }

    [JavaOracleFact]
    public void RenameUpdatesIndexAndForeignKeyMetadataAndExecution()
    {
        Check([
            "CREATE TABLE P(ID INT PRIMARY KEY, V VARCHAR(20))",
            "CREATE INDEX IX ON P(V)",
            "CREATE TABLE C(ID INT, PID INT REFERENCES P(ID) ON UPDATE CASCADE)",
            "INSERT INTO P VALUES(1,'a')",
            "INSERT INTO C VALUES(10,1)",
            "ALTER TABLE P ALTER COLUMN ID RENAME TO KEY_ID",
            "ALTER TABLE P ALTER COLUMN V RENAME TO TEXT_VALUE",
            "UPDATE P SET KEY_ID=2 WHERE KEY_ID=1",
            "SELECT ID,PID FROM C ORDER BY ID",
            "SELECT KEY_ID,TEXT_VALUE FROM P WHERE TEXT_VALUE='a'",
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.INDEX_COLUMNS WHERE TABLE_NAME='P' ORDER BY COLUMN_NAME",
        ]);
    }

    [JavaOracleFact]
    public void TypeChangesCopyRowsAndRebuildIndexesWithUsing()
    {
        Check([
            "CREATE TABLE T(ID INT PRIMARY KEY, V VARCHAR(10) DEFAULT 'd', N INT)",
            "CREATE INDEX IX ON T(V)",
            "INSERT INTO T VALUES(1,'a',2),(2,'b',3)",
            "ALTER TABLE T ALTER COLUMN ID SET DATA TYPE VARCHAR(20) USING CAST(ID+10 AS VARCHAR(20))",
            "SELECT ID,V,N FROM T ORDER BY ID",
            "ALTER TABLE T ALTER COLUMN V SET DATA TYPE VARCHAR(30) USING UPPER(V)",
            "SELECT ID,V FROM T WHERE V='A'",
            "INSERT INTO T(ID,N) VALUES('13',4)",
            "ALTER TABLE T ALTER COLUMN N BIGINT",
            "ALTER TABLE T ALTER COLUMN N SET DATA TYPE VARCHAR(20)",
            "SELECT ID,V,N FROM T ORDER BY ID",
            "SELECT CONSTRAINT_NAME,CONSTRAINT_TYPE FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS WHERE TABLE_NAME='T' ORDER BY CONSTRAINT_NAME",
            "SELECT INDEX_NAME,COLUMN_NAME FROM INFORMATION_SCHEMA.INDEX_COLUMNS WHERE TABLE_NAME='T' AND INDEX_NAME='IX'",
        ]);
    }

    [JavaOracleFact]
    public void MissingNamesAndNullabilityErrorsMatchTheReference()
    {
        Check([
            "ALTER TABLE IF EXISTS MISSING ALTER COLUMN V SET NOT NULL",
            "ALTER TABLE IF EXISTS MISSING ALTER COLUMN V SET DATA TYPE INT USING 1",
            "CREATE TABLE T(ID INT PRIMARY KEY, V INT)",
            "ALTER TABLE T ALTER COLUMN IF EXISTS MISSING RENAME TO NEW_NAME",
            "ALTER TABLE T ALTER COLUMN IF EXISTS MISSING SET DEFAULT 1",
            "ALTER TABLE T ALTER COLUMN MISSING SET DEFAULT 1",
            "INSERT INTO T VALUES(1,NULL)",
            "ALTER TABLE T ALTER COLUMN V SET NOT NULL",
            "SELECT IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='T' AND COLUMN_NAME='V'",
            "UPDATE T SET V=2",
            "ALTER TABLE T ALTER COLUMN V SET NOT NULL",
            "INSERT INTO T VALUES(2,NULL)",
            "ALTER TABLE T ALTER COLUMN ID DROP NOT NULL",
            "ALTER TABLE T ALTER COLUMN V RENAME TO ID",
            "ALTER TABLE T ALTER COLUMN V SET NULL",
            "INSERT INTO T VALUES(2,NULL)",
            "SELECT ID,V FROM T ORDER BY ID",
        ], allowErrors: true);
    }

    [JavaOracleFact]
    public void GeneratedAndIdentityColumnsKeepTheirReferenceGuards()
    {
        Check([
            "CREATE TABLE T(ID INT GENERATED ALWAYS AS IDENTITY, V INT, G INT GENERATED ALWAYS AS(V+1))",
            "INSERT INTO T(V) VALUES(1)",
            "ALTER TABLE T ALTER COLUMN G SET DEFAULT 7",
            "ALTER TABLE T ALTER COLUMN G DROP DEFAULT",
            "ALTER TABLE T ALTER COLUMN ID SET DEFAULT 99",
            "ALTER TABLE T ALTER COLUMN G SET ON UPDATE 8",
            "INSERT INTO T(V) VALUES(2)",
            "SELECT ID,V,G FROM T ORDER BY ID",
            "ALTER TABLE T ALTER COLUMN G DROP EXPRESSION",
            "UPDATE T SET V=5",
            "SELECT ID,V,G FROM T ORDER BY ID",
            "ALTER TABLE T ALTER COLUMN ID SET GENERATED BY DEFAULT",
            "ALTER TABLE T ALTER COLUMN ID RESTART WITH 20",
            "INSERT INTO T(V) VALUES(3)",
            "ALTER TABLE T ALTER COLUMN ID DROP IDENTITY",
            "INSERT INTO T(ID,V,G) VALUES(30,4,9)",
            "SELECT ID,V,G FROM T ORDER BY ID",
        ]);
    }

    [JavaOracleFact]
    public void IdentityTypeChangesKeepTheSequenceOptionsAndNextValue()
    {
        Check([
            "CREATE TABLE T(ID INT GENERATED BY DEFAULT AS IDENTITY(START WITH 10 INCREMENT BY 3), V VARCHAR(20))",
            "INSERT INTO T(V) VALUES('a')",
            "ALTER TABLE T ALTER COLUMN ID SET DATA TYPE BIGINT",
            "INSERT INTO T(V) VALUES('b')",
            "SELECT ID,V FROM T ORDER BY ID",
            "SELECT DATA_TYPE,IDENTITY_START,IDENTITY_INCREMENT FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='T' AND COLUMN_NAME='ID'",
            "ALTER TABLE T ALTER COLUMN ID SET GENERATED ALWAYS",
            "INSERT INTO T(V) VALUES('c')",
            "SELECT ID,V FROM T ORDER BY ID",
        ]);
    }

    [JavaOracleFact]
    public void DomainsAndFailedConversionsKeepRowsAndCleanUpCopies()
    {
        Check([
            "CREATE DOMAIN D AS INT CHECK(VALUE>0)",
            "CREATE TABLE T(ID INT PRIMARY KEY, V VARCHAR(20))",
            "INSERT INTO T VALUES(1,'2'),(2,'bad')",
            "ALTER TABLE T ALTER COLUMN V SET DATA TYPE INT",
            "SELECT ID,V FROM T ORDER BY ID",
            "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA='PUBLIC' ORDER BY TABLE_NAME",
            "UPDATE T SET V='3' WHERE ID=2",
            "ALTER TABLE T ALTER COLUMN V SET DATA TYPE PUBLIC.D USING CAST(V AS INT)",
            "INSERT INTO T VALUES(3,-1)",
            "SELECT ID,V FROM T ORDER BY ID",
            "ALTER TABLE T ALTER COLUMN V CLOB",
            "SELECT ID,V FROM T ORDER BY ID",
        ], allowErrors: true);
    }

    [JavaOracleFact]
    public void TypeChangesPreserveIncomingForeignKeysAndRejectSelfReferencingDefaults()
    {
        Check([
            "CREATE TABLE P(ID INT PRIMARY KEY, V INT)",
            "CREATE TABLE C(PID INT REFERENCES P(ID) ON UPDATE CASCADE)",
            "INSERT INTO P VALUES(1,2)",
            "INSERT INTO C VALUES(1)",
            "ALTER TABLE P ALTER COLUMN V SET DEFAULT (SELECT MAX(V) FROM P)",
            "ALTER TABLE P ALTER COLUMN V SET ON UPDATE (SELECT MAX(V) FROM P)",
            "ALTER TABLE P ALTER COLUMN ID SET DATA TYPE BIGINT",
            "UPDATE P SET ID=2 WHERE ID=1",
            "SELECT PID FROM C",
            "INSERT INTO C VALUES(99)",
            "SELECT ID,V FROM P",
        ], allowErrors: true);
    }

    [JavaOracleFact]
    public void ColumnDefinitionAndMetadataSqlMatchTheReference()
    {
        Check([
            "CREATE TABLE T(ID INT, V VARCHAR(10) DEFAULT 'd')",
            "INSERT INTO T VALUES(1,'a')",
            "ALTER TABLE T ALTER COLUMN V VARCHAR(20) DEFAULT 'next' ON UPDATE 'changed' NOT NULL COMMENT 'note'",
            "SELECT REPLACE(DB_OBJECT_SQL('TABLE','PUBLIC','T'),CHAR(10),'|')",
            "INSERT INTO T(ID) VALUES(2)",
            "UPDATE T SET ID=3 WHERE ID=1",
            "ALTER TABLE T ALTER COLUMN V SET DATA TYPE VARCHAR(30)",
            "SELECT REPLACE(DB_OBJECT_SQL('TABLE','PUBLIC','T'),CHAR(10),'|')",
            "SELECT ID,V FROM T ORDER BY ID",
        ]);
    }

    [JavaOracleFact]
    public void AlteredMetadataAndRowsSurviveReopenAndAreReadableByJava()
    {
        string path = Path.Combine(Path.GetTempPath(), "h2sharp_alter_column_" + Guid.NewGuid().ToString("N")).Replace('\\', '/');
        try
        {
            using (H2Connection connection = new("jdbc:h2:file:" + path))
            {
                connection.Open();
                foreach (string sql in new[] {
                    "CREATE TABLE T(ID INT PRIMARY KEY, V VARCHAR(10))",
                    // Three rows exceed the block size (4 / 2), but the final four-row
                    // result fits in memory; external query results are a separate port gap.
                    "SET MAX_MEMORY_ROWS 4",
                    "CREATE INDEX IX ON T(V)", "INSERT INTO T VALUES(1,'a'),(2,'z'),(3,'b')",
                    "ALTER TABLE T ALTER COLUMN V RENAME TO TEXT_VALUE",
                    "ALTER TABLE T ALTER COLUMN TEXT_VALUE SET DEFAULT 'd'",
                    "ALTER TABLE T ALTER COLUMN ID SET DATA TYPE BIGINT USING ID+10",
                    "ALTER TABLE T ALTER COLUMN TEXT_VALUE SET INVISIBLE",
                })
                {
                    using DbCommand command = connection.CreateCommand();
                    command.CommandText = sql;
                    command.ExecuteNonQuery();
                }
            }
            List<string> port;
            using (H2Connection reopened = new("jdbc:h2:file:" + path))
            {
                reopened.Open();
                port = RunPort(reopened, [
                    "INSERT INTO T(ID) VALUES(14)",
                    "SELECT ID,TEXT_VALUE FROM T ORDER BY ID",
                    "SELECT * FROM T ORDER BY ID",
                ]);
            }
            Assert.DoesNotContain(port, line => line.Contains(":E:", StringComparison.Ordinal));
            List<string> java = OracleRunner.Run([path], """
                try (java.sql.Connection c=java.sql.DriverManager.getConnection("jdbc:h2:file:"+inputs[0])) {
                  java.sql.Statement s=c.createStatement();
                  java.sql.ResultSet r=s.executeQuery("SELECT ID,TEXT_VALUE FROM T ORDER BY ID");
                  while(r.next()) System.out.println("="+r.getString(1)+":"+r.getString(2));
                  r=s.executeQuery("SELECT * FROM T ORDER BY ID");
                  System.out.println("=columns:"+r.getMetaData().getColumnCount());
                }
                """, extraJavaArgs: ["-Duser.language=en"]);
            Assert.Equal(["=11:a", "=12:z", "=13:b", "=14:d", "=columns:1"], java);
            Assert.Contains("=1:R:11|a", port);
            Assert.Contains("=1:R:12|z", port);
            Assert.Contains("=1:R:13|b", port);
            Assert.Contains("=1:R:14|d", port);
        }
        finally
        {
            foreach (string suffix in new[] { ".mv.db", ".trace.db", ".lock.db" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    private static void Check(string[] statements, bool allowErrors = false)
    {
        using H2Connection connection = new("jdbc:h2:mem:alter_column_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        List<string> port = RunPort(connection, statements);
        List<string> java = OracleRunner.Run(statements, """
            try (java.sql.Connection c=java.sql.DriverManager.getConnection("jdbc:h2:mem:alter_column")) {
              java.sql.Statement s=c.createStatement();
              for(int i=0;i<inputs.length;i++) {
                try {
                  if(s.execute(inputs[i])) {
                    java.sql.ResultSet r=s.getResultSet(); int count=r.getMetaData().getColumnCount();
                    System.out.println("="+i+":C:"+count);
                    while(r.next()) {
                      StringBuilder b=new StringBuilder("="+i+":R:");
                      for(int col=1;col<=count;col++) { if(col>1)b.append('|'); String v=r.getString(col); b.append(v==null?"-":v); }
                      System.out.println(b);
                    }
                  } else System.out.println("="+i+":U:"+s.getUpdateCount());
                } catch(java.sql.SQLException e) { System.out.println("="+i+":E:"+e.getErrorCode()); }
              }
            }
            """, extraJavaArgs: ["-Duser.language=en"]);
        Assert.True(java.SequenceEqual(port), "SQL:\n" + string.Join("\n", statements)
            + "\njava:\n" + string.Join("\n", java) + "\nport:\n" + string.Join("\n", port));
        if (!allowErrors) Assert.DoesNotContain(port, line => line.Contains(":E:", StringComparison.Ordinal));
    }

    private static List<string> RunPort(H2Connection connection, IReadOnlyList<string> statements)
    {
        List<string> lines = [];
        for (int i = 0; i < statements.Count; i++)
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = statements[i];
            try
            {
                if (statements[i].StartsWith("SELECT", StringComparison.Ordinal))
                {
                    using DbDataReader reader = command.ExecuteReader();
                    lines.Add($"={i}:C:{reader.FieldCount}");
                    while (reader.Read())
                    {
                        List<string> values = [];
                        for (int c = 0; c < reader.FieldCount; c++)
                            values.Add(reader.IsDBNull(c) ? "-" : Convert.ToString(reader.GetValue(c), CultureInfo.InvariantCulture)!);
                        lines.Add($"={i}:R:" + string.Join('|', values));
                    }
                }
                else lines.Add($"={i}:U:{command.ExecuteNonQuery()}");
            }
            catch (H2SharpException e) { lines.Add($"={i}:E:{e.H2ErrorCode}"); }
        }
        return lines;
    }
}

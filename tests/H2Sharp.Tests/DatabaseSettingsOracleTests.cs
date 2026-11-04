/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Settings that change how the database compares and orders: the default NULL placement,
 * case-insensitive identifiers, and the refusal a drop meets when a view depends on the
 * table.
 */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class DatabaseSettingsOracleTests
{
    [JavaOracleFact]
    public void DefaultNullOrderingMatchesJdbc()
    {
        Compare(string.Empty, [
            "CREATE TABLE T(ID INT)",
            "INSERT INTO T VALUES (1), (NULL), (3), (NULL)",
            "SELECT ID FROM T ORDER BY ID",
            "SET DEFAULT_NULL_ORDERING HIGH",
            "SELECT ID FROM T ORDER BY ID",
            "SELECT ID FROM T ORDER BY ID DESC",
            "SET DEFAULT_NULL_ORDERING FIRST",
            "SELECT ID FROM T ORDER BY ID",
            "SELECT ID FROM T ORDER BY ID DESC",
            "SET DEFAULT_NULL_ORDERING LAST",
            "SELECT ID FROM T ORDER BY ID",
            "SET DEFAULT_NULL_ORDERING LOW",
            "SELECT ID FROM T ORDER BY ID",
            "SET DEFAULT_NULL_ORDERING OTHER",
        ]);
    }

    [JavaOracleFact]
    public void CaseInsensitiveIdentifiersMatchJdbc()
    {
        Compare("CASE_INSENSITIVE_IDENTIFIERS=TRUE", [
            "CREATE TABLE Foo(Bar INT, \"Baz\" INT)",
            "INSERT INTO foo(bar, \"baz\") VALUES (1, 2)",
            "SELECT Bar, BAR, bar FROM FOO",
            "SELECT BAZ FROM Foo",
            "SELECT TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS "
                + "WHERE TABLE_NAME = 'FOO' ORDER BY COLUMN_NAME",
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'foo'",
            "CREATE TABLE foo(Bar INT)",
            "CREATE INDEX MyIndex ON Foo(Bar)",
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.INDEXES WHERE INDEX_NAME = 'MYINDEX'",
        ]);
    }

    [JavaOracleFact]
    public void DroppingADependentTableMatchesJdbc()
    {
        Compare(string.Empty, [
            "CREATE TABLE T(ID INT PRIMARY KEY)",
            "CREATE VIEW V AS SELECT * FROM T",
            "DROP TABLE T",
            "DROP TABLE IF EXISTS T",
            "DROP VIEW V",
            "DROP TABLE T",
        ]);
    }

    /// <summary>
    /// Runs the statements over a fresh database on both sides, with the same URL suffix.
    /// </summary>
    internal static void Compare(string urlSuffix, string[] statements)
    {
        string databaseName = "db_settings_" + Guid.NewGuid().ToString("N");
        string portUrl = "Data Source=" + databaseName + ";Storage=Memory";
        string javaUrl = "jdbc:h2:mem:db_settings";
        if (urlSuffix.Length > 0)
        {
            portUrl += ";" + urlSuffix;
            javaUrl += ";" + urlSuffix;
        }

        List<string> port = [];
        using (H2Connection connection = new(portUrl))
        {
            connection.Open();
            for (int i = 0; i < statements.Length; i++)
            {
                string prefix = "=" + i + ':';
                using DbCommand command = connection.CreateCommand();
                command.CommandText = statements[i];
                try
                {
                    bool query = statements[i].StartsWith("SELECT", StringComparison.Ordinal)
                        || statements[i].StartsWith('(');
                    if (!query)
                    {
                        port.Add(prefix + "U:" + command.ExecuteNonQuery());
                        continue;
                    }

                    using DbDataReader rows = command.ExecuteReader();
                    while (rows.Read())
                    {
                        List<string> cells = [];
                        for (int c = 0; c < rows.FieldCount; c++)
                        {
                            cells.Add(rows.IsDBNull(c) ? "NULL" : rows.GetString(c));
                        }

                        port.Add(prefix + "R:" + string.Join('|', cells));
                    }

                    port.Add(prefix + "Q");
                }
                catch (H2SharpException error)
                {
                    port.Add(prefix + "E:" + error.H2ErrorCode.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        string catalog = databaseName.ToUpperInvariant();
        for (int p = 0; p < port.Count; p++)
        {
            port[p] = port[p].Replace(catalog, "DB_SETTINGS", StringComparison.Ordinal);
        }

        string javaSource = """
            try (java.sql.Connection conn = java.sql.DriverManager.getConnection("JAVA_URL")) {
              java.sql.Statement s = conn.createStatement();
              for (int i = 0; i < inputs.length; i++) {
                String prefix = "=" + i + ":";
                try {
                  if (!s.execute(inputs[i])) { System.out.println(prefix + "U:" + s.getUpdateCount()); continue; }
                  try (java.sql.ResultSet r = s.getResultSet()) {
                    while (r.next()) {
                      StringBuilder line = new StringBuilder(prefix + "R:");
                      for (int c = 1; c <= r.getMetaData().getColumnCount(); c++) {
                        if (c > 1) line.append('|');
                        String value = r.getString(c);
                        line.append(r.wasNull() ? "NULL" : value);
                      }
                      System.out.println(line);
                    }
                    System.out.println(prefix + "Q");
                  }
                } catch (java.sql.SQLException error) {
                  System.out.println(prefix + "E:" + error.getErrorCode());
                }
              }
            }
            """.Replace("JAVA_URL", javaUrl, StringComparison.Ordinal);
        List<string> java = OracleRunner.Run(statements, javaSource);

        Assert.True(java.Count == port.Count,
            "lines: jvm=" + java.Count + " port=" + port.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == port[i], "line " + i + ":\n  jvm:  " + java[i] + "\n  port: " + port[i]);
        }
    }
}

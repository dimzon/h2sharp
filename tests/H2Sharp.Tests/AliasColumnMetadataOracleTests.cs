/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for the column metadata behind an alias.
 *
 * A mode decides whether an alias renames the column or only labels it: in the default mode
 * H2 reports the base column name and its table and the alias as the label, while DB2,
 * Derby, SQL Server, Oracle and PostgreSQL report the alias as the name and no table. The
 * command builder depends on that difference, so both sides are asked the same three
 * questions through the same query in every mode, and the answers are compared line by line.
 */
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class AliasColumnMetadataOracleTests
{
    /// <summary>
    /// Every mode the port knows, asked in the spelling the connection string takes.
    /// </summary>
    private static readonly string[] Modes =
    [
        "REGULAR", "STRICT", "LEGACY", "DB2", "Derby", "MariaDB", "MSSQLServer",
        "HSQLDB", "MySQL", "Oracle", "PostgreSQL",
    ];

    [JavaOracleFact]
    public void LabelNameAndTableMatchTheJvmInEveryMode()
    {
        List<string> java = OracleRunner.Run(
            Modes,
            """
            for (String mode : inputs) {
              try (java.sql.Connection conn = java.sql.DriverManager.getConnection(
                  "jdbc:h2:mem:alias_meta_" + mode.replace(' ', '_') + ";MODE=" + mode)) {
                java.sql.Statement s = conn.createStatement();
                s.execute("CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR(20))");
                try (java.sql.ResultSet r = s.executeQuery(
                    "SELECT ID AS K, NAME AS N, ID + 1 AS NEXT FROM T")) {
                  java.sql.ResultSetMetaData m = r.getMetaData();
                  for (int i = 1; i <= m.getColumnCount(); i++) {
                    System.out.println("=" + mode + "|" + m.getColumnLabel(i) + "|"
                        + m.getColumnName(i) + "|" + m.getTableName(i) + "|" + m.getSchemaName(i));
                  }
                }
                s.close();
              }
            }
            """);

        List<string> port = [];
        foreach (string mode in Modes)
        {
            using H2Connection connection = new(
                $"Data Source=alias_meta_{Guid.NewGuid():N};Storage=Memory;Mode={mode}");
            connection.Open();

            using DbCommand create = connection.CreateCommand();
            create.CommandText = "CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR(20))";
            create.ExecuteNonQuery();

            using DbCommand select = connection.CreateCommand();
            select.CommandText = "SELECT ID AS K, NAME AS N, ID + 1 AS NEXT FROM T";
            using DbDataReader reader = select.ExecuteReader(CommandBehavior.SchemaOnly);
            DataTable schema = reader.GetSchemaTable()!;
            for (int i = 0; i < reader.FieldCount; i++)
            {
                DataRow row = schema.Rows[i];
                // The alias is the label; the base column name is only reported by the modes
                // that keep base metadata. Where it is absent the alias is the name as well,
                // which is what JDBC reports in those modes.
                string label = reader.GetName(i);
                string name = row["BaseColumnName"] is DBNull ? label : (string)row["BaseColumnName"];
                string table = row["BaseTableName"] is DBNull ? string.Empty : (string)row["BaseTableName"];
                string schemaName =
                    row["BaseSchemaName"] is DBNull ? string.Empty : (string)row["BaseSchemaName"];
                port.Add("=" + mode + "|" + label + "|" + name + "|" + table + "|" + schemaName);
            }
        }

        Assert.Equal(java.Count, port.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == port[i], $"line {i}:\n  java: {java[i]}\n  port: {port[i]}");
        }
    }
}

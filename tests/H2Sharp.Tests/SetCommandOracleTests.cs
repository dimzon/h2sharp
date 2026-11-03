/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * SET: the options that only change a session, the ones that change the database, the values
 * they refuse, and what INFORMATION_SCHEMA.SETTINGS says afterwards. Every statement and every
 * refusal is compared with the reference running the same text.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using H2Sharp;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class SetCommandOracleTests
{
    [JavaOracleFact]
    public void TheSettingsAndTheirRefusalsMatchTheReference()
    {
        string[] statements =
        [
            // Refused values, one per option that validates its argument.
            "SET ALLOW_LITERALS 3",
            "SET CACHE_SIZE -1",
            "SET DB_CLOSE_DELAY -2",
            "SET DEFAULT_LOCK_TIMEOUT -1",
            "SET LOCK_TIMEOUT -1",
            "SET MAX_LENGTH_INPLACE_LOB -1",
            "SET MAX_LOG_SIZE -1",
            "SET MAX_MEMORY_ROWS -1",
            "SET MAX_MEMORY_UNDO -1",
            "SET MAX_OPERATION_MEMORY -1",
            "SET QUERY_TIMEOUT -1",
            "SET EXCLUSIVE 5",
            "SET MODE no_such_mode",
            "SET LAZY_QUERY_EXECUTION 2",
            "SET RETENTION_TIME -1",
            "SET BUILTIN_ALIAS_OVERRIDE 2",
            "SET IGNORE_CATALOGS 2",
            "SET REDO_LOG_BINARY TRUE",
            "SET SCHEMA no_such_schema",
            "SET REFERENTIAL_INTEGRITY 2",
            "SET QUERY_STATISTICS 2",
            "SET QUERY_STATISTICS_MAX_ENTRIES 0",
            "SET CATALOG PUBLIC",
            "SET TRACE_MAX_FILE_SIZE -1",
            "SET THROTTLE -1",
            "SET WRITE_DELAY -1",
            "SET DEFAULT_NULL_ORDERING BAD",
            // Accepted values, database wide.
            "SET ALLOW_LITERALS ALL",
            "SET CACHE_SIZE 4096",
            "SET DEFAULT_LOCK_TIMEOUT 2000",
            "SET MAX_LENGTH_INPLACE_LOB 32",
            "SET MAX_LOG_SIZE 0",
            "SET MAX_MEMORY_ROWS 10000",
            "SET MAX_MEMORY_UNDO 1000",
            "SET MAX_OPERATION_MEMORY 100000",
            "SET DEFAULT_TABLE_TYPE 0",
            "SET TRACE_LEVEL_FILE 0",
            "SET TRACE_LEVEL_SYSTEM_OUT 0",
            "SET TRACE_MAX_FILE_SIZE 0",
            "SET THROTTLE 0",
            "SET NON_KEYWORDS VALUE",
            "SET CREATE_BUILD 1",
            "SET BUILTIN_ALIAS_OVERRIDE 1",
            "SET DEFAULT_NULL_ORDERING HIGH",
            "SET DEFAULT_NULL_ORDERING LOW",
            "SET DATABASE_EVENT_LISTENER 'x.y.Z'",
            "SET IGNORECASE FALSE",
            "SET LOCK_MODE 3",
            "SET OPTIMIZE_REUSE_RESULTS TRUE",
            "SET QUERY_STATISTICS FALSE",
            "SET QUERY_STATISTICS_MAX_ENTRIES 100",
            "SET REFERENTIAL_INTEGRITY TRUE",
            "SET WRITE_DELAY 500",
            "SET RETENTION_TIME 1000",
            "SET LAZY_QUERY_EXECUTION 0",
            "SET TRUNCATE_LARGE_LENGTH FALSE",
            "SET DEFAULT_NULL_ORDERING LOW",
            "SET MODE REGULAR",
            "SET DB_CLOSE_DELAY -1",
            // Cluster and collation accept a value that only the start-up path uses, and a
            // collation set twice is a no-op.
            "SET CLUSTER 'TRUE'",
            "SET COLLATION OFF",
            "SET COLLATION OFF",
            "SET COLLATION ENGLISH STRENGTH IDENTICAL",
            "SET COLLATION ENGLISH STRENGTH PRIMARY",
            "SET COLLATION ENGLISH STRENGTH SECONDARY",
            "SET COLLATION ENGLISH STRENGTH TERTIARY",
            "SET JAVA_OBJECT_SERIALIZER 'x.y.Z'",
            // Session scoped.
            "SET LOCK_TIMEOUT 500",
            "SET QUERY_TIMEOUT 1000",
            "SET SCHEMA PUBLIC",
            "SET SCHEMA_SEARCH_PATH PUBLIC",
            "SET TIME ZONE 'UTC'",
            "SET @X 42",
            "SELECT @X",
            // What the settings block says afterwards.
            // DbSettings (ANALYZE_*, AUTO_COMPACT_FILL_RATE, ...) are URL settings and are not
            // ported; only the rows the options under test write are compared.
            "SELECT SETTING_NAME, SETTING_VALUE FROM INFORMATION_SCHEMA.SETTINGS"
                + " WHERE SETTING_NAME IN ('ALLOW_LITERALS', 'CACHE_SIZE', 'CLUSTER', 'COLLATION',"
                + " 'DEFAULT_LOCK_TIMEOUT', 'DEFAULT_NULL_ORDERING', 'DEFAULT_TABLE_TYPE',"
                + " 'IGNORECASE', 'LAZY_QUERY_EXECUTION', 'LOCK_MODE', 'MAX_LENGTH_INPLACE_LOB',"
                + " 'MAX_LOG_SIZE', 'MAX_MEMORY_ROWS', 'MAX_MEMORY_UNDO', 'MAX_OPERATION_MEMORY',"
                + " 'OPTIMIZE_REUSE_RESULTS', 'QUERY_STATISTICS', 'QUERY_STATISTICS_MAX_ENTRIES',"
                + " 'REFERENTIAL_INTEGRITY', 'RETENTION_TIME', 'TRUNCATE_LARGE_LENGTH',"
                + " 'WRITE_DELAY') ORDER BY SETTING_NAME",
            // A mode change to a different mode is D-029: the port keeps the mode fixed
            // after the database is opened and answers 50100, so only the no-op form and an
            // unknown mode are comparable here.
            // Refusals that need a table in the database.
            "CREATE TABLE T(ID INT PRIMARY KEY, V VARCHAR)",
            "INSERT INTO T VALUES (1, 'a')",
            "SET COLLATION ENGLISH STRENGTH PRIMARY",
            "SET JAVA_OBJECT_SERIALIZER 'a.b.C'",
            // An option that is accepted and ignored outside a cluster.
            "SET CLUSTER 'disabled'",
            "SET CLUSTER ''",
            // Exclusive modes on this connection: on, admin-only on, off, and off again.
            "SET EXCLUSIVE 1",
            "SET EXCLUSIVE 2",
            "SET EXCLUSIVE 0",
            "SET EXCLUSIVE 0",
            // Restricting literals last: numbers stay allowed, strings do not, and then
            // neither does a number.
            "SET ALLOW_LITERALS NUMBERS",
            "SELECT 'x'",
            "SELECT 1",
            "SET ALLOW_LITERALS NONE",
            "SELECT 1",
            "SET ALLOW_LITERALS ALL",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        Assert.Equal(java, port);
    }

    private static List<string> RunPort(IReadOnlyList<string> statements)
    {
        SessionLocal session = new(
            new Database(ConnectionInfo.Memory("set_command_oracle")), "sa")
        {
            CompareMode = CompareMode.GetInstance(null, 0),
        };
        List<string> lines = [];
        for (int statementIndex = 0; statementIndex < statements.Count; statementIndex++)
        {
            string sql = statements[statementIndex];
            try
            {
                CommandContainer container =
                    Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
                if (sql.StartsWith("SELECT", StringComparison.Ordinal))
                {
                    ResultInterface result = container.Query(-1);
                    int visible = result.GetVisibleColumnCount();
                    while (result.Next())
                    {
                        Value?[] row = result.CurrentRow();
                        List<string> cells = [];
                        for (int i = 0; i < visible; i++)
                        {
                            Value? value = row[i];
                            cells.Add(value is null or ValueNull ? "-" : value.GetString() ?? "-");
                        }

                        lines.Add(statementIndex + "=" + string.Join(":", cells));
                    }
                }
                else
                {
                    lines.Add(statementIndex + "=" + container.ExecuteUpdate(false).GetUpdateCount());
                }
            }
            catch (Exception e)
            {
                lines.Add(statementIndex + "=" + (e is H2SharpException h2
                    ? h2.H2ErrorCode.ToString(CultureInfo.InvariantCulture)
                    : e.GetType().Name));
            }
        }

        return lines;
    }

    private static List<string> RunJava(IReadOnlyList<string> statements)
        => OracleRunner.Run(
            statements,
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:set_command_oracle");
            java.sql.Statement stat = conn.createStatement();
            for (int i = 0; i < inputs.length; i++) {
              try {
                boolean isQuery = stat.execute(inputs[i]);
                if (isQuery) {
                  java.sql.ResultSet rs = stat.getResultSet();
                  int columns = rs.getMetaData().getColumnCount();
                  while (rs.next()) {
                    StringBuilder line = new StringBuilder().append(i).append("=");
                    for (int c = 1; c <= columns; c++) {
                      if (c > 1) {
                        line.append(':');
                      }
                      String s = rs.getString(c);
                      line.append(rs.wasNull() ? "-" : s);
                    }
                    System.out.println(line);
                  }
                } else {
                  System.out.println(i + "=" + stat.getUpdateCount());
                }
              } catch (java.sql.SQLException e) {
                System.out.println(i + "=" + e.getErrorCode());
              }
            }
            conn.close();
            """,
            extraJavaArgs: ["-Duser.language=en", "-Duser.timezone=UTC"]);
}

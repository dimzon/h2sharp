/* Copyright 2026 H2Sharp contributors. Licensed under MPL 2.0 or EPL 1.0.
 * Probe: verify suspected engine defects against pinned JDBC before fixing. */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ConformanceProbeTests
{
    [JavaOracleFact]
    public void ProbeAmbiguousConformanceCases()
    {
        string[] statements =
        [
            "SELECT EXTRACT(SECOND FROM INTERVAL '5.5' SECOND)",
            "SELECT EXTRACT(SECOND FROM INTERVAL '1 02:03:04.5' DAY TO SECOND(2))",
            "SELECT EXTRACT(DECADE FROM DATE '2024-05-06')",
            "SELECT EXTRACT(CENTURY FROM DATE '2024-05-06')",
            "SELECT EXTRACT(MILLENNIUM FROM DATE '2024-05-06')",
            "SELECT ISO_DAY_OF_WEEK(DATE '2024-05-06')",
            "SELECT RAWTOHEX(X'4142'), RAWTOHEX('AB')",
            "SELECT CASE WHEN ID > 2 THEN 'big' WHEN ID > 1 THEN 'medium' ELSE 'small' END FROM (VALUES (2)) T(ID)",
            "SELECT CASE WHEN ID > 2 THEN 'big' WHEN ID > 1 THEN 'medium' ELSE 'small' END FROM (VALUES (3)) T(ID)",
            "CREATE TABLE T(ID INT, UPDATED INT DEFAULT 0 ON UPDATE 42)",
            "INSERT INTO T(ID) VALUES (1)",
            "UPDATE T SET ID = 2 WHERE ID = 1",
            "SELECT UPDATED FROM T",
            "CREATE TABLE D(ID INT, NAME VARCHAR(50) DEFAULT 'x' DEFAULT ON NULL)",
            "INSERT INTO D(ID, NAME) VALUES (1, NULL)",
            "SELECT NAME FROM D",
        ];
        using H2Connection connection = new("jdbc:h2:mem:conformance_probe_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        List<string> port = [];
        for (int i = 0; i < statements.Length; i++)
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = statements[i];
            try
            {
                if (!statements[i].StartsWith("SELECT", StringComparison.Ordinal))
                { port.Add("=" + i + ":U:" + command.ExecuteNonQuery()); continue; }
                using DbDataReader reader = command.ExecuteReader();
                port.Add("=" + i + ":C:" + reader.FieldCount);
                while (reader.Read())
                {
                    List<string> cells = [];
                    for (int c = 0; c < reader.FieldCount; c++) cells.Add(reader.IsDBNull(c) ? "NULL" : reader.GetString(c));
                    port.Add("=" + i + ":R:" + string.Join('|', cells));
                }
            }
            catch (H2SharpException e) { port.Add("=" + i + ":E:" + e.H2ErrorCode); }
        }
        List<string> java = OracleRunner.Run(statements,
            """
            try (java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:conformance_probe")) {
                for (int i = 0; i < inputs.length; i++) {
                    try (java.sql.Statement s = conn.createStatement()) {
                        if (!s.execute(inputs[i])) { System.out.println("=" + i + ":U:" + s.getUpdateCount()); continue; }
                        try (java.sql.ResultSet r = s.getResultSet()) {
                            System.out.println("=" + i + ":C:" + r.getMetaData().getColumnCount());
                            while (r.next()) {
                                StringBuilder line = new StringBuilder("=" + i + ":R:");
                                for (int c = 1; c <= r.getMetaData().getColumnCount(); c++) {
                                    if (c > 1) line.append('|');
                                    String v = r.getString(c); line.append(r.wasNull() ? "NULL" : v);
                                }
                                System.out.println(line);
                            }
                        }
                    } catch (java.sql.SQLException e) { System.out.println("=" + i + ":E:" + e.getErrorCode()); }
                }
            }
            """, extraJavaArgs: ["-Duser.language=en", "-Duser.country=US", "-Duser.timezone=UTC"]);
        Assert.True(java.SequenceEqual(port), "JVM:\n" + string.Join('\n', java) + "\nPort:\n" + string.Join('\n', port));
    }
}

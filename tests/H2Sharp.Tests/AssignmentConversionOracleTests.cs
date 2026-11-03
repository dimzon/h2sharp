/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Assignment conversions: the value a store or an update has to fit into a typed column.
 * The CAST matrix covers the explicit road; this one covers the road INSERT and UPDATE take,
 * which the reference routes through Value.convertTo and which answers some pairs differently
 * from CAST.
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
public sealed class AssignmentConversionOracleTests
{
    private static readonly string[] Setup =
    [
        "CREATE TABLE CV(ID INT GENERATED ALWAYS AS IDENTITY, "
            + "TI TINYINT, SI SMALLINT, I INTEGER, BI BIGINT, N NUMERIC(5, 2), N38 NUMERIC(38, 10), "
            + "R REAL, D DOUBLE, DF DECFLOAT, B BOOLEAN, C5 CHAR(5), VC VARCHAR(5), "
            + "VCI VARCHAR_IGNORECASE(5), V1 BINARY(2), VB VARBINARY(2), BL BLOB, CL CLOB, "
            + "DT DATE, TM TIME(3), TS TIMESTAMP(3), TZ TIMESTAMP(3) WITH TIME ZONE, "
            + "U UUID, IYM INTERVAL YEAR TO MONTH, IDT INTERVAL DAY TO SECOND, "
            + "AR INTEGER ARRAY, J JSON)",
    ];

    private static readonly string[] Assignments =
    [
        "INSERT INTO CV(TI) VALUES (12)",
        "INSERT INTO CV(TI) VALUES ('12')",
        "INSERT INTO CV(TI) VALUES (' 12 ')",
        "INSERT INTO CV(TI) VALUES (300)",
        "INSERT INTO CV(TI) VALUES (12.9)",
        "INSERT INTO CV(TI) VALUES (TRUE)",
        "INSERT INTO CV(SI) VALUES (70000)",
        "INSERT INTO CV(I) VALUES (5000000000)",
        "INSERT INTO CV(BI) VALUES ('9223372036854775807')",
        "INSERT INTO CV(N) VALUES (123.456)",
        "INSERT INTO CV(N) VALUES ('1E3')",
        "INSERT INTO CV(N38) VALUES (1.5)",
        "INSERT INTO CV(R) VALUES (1E40)",
        "INSERT INTO CV(D) VALUES ('1.5')",
        "INSERT INTO CV(DF) VALUES (12.9)",
        "INSERT INTO CV(DF) VALUES ('1E+300')",
        "INSERT INTO CV(DF) VALUES (1.7976931348623157E308)",
        "INSERT INTO CV(B) VALUES (1)",
        "INSERT INTO CV(B) VALUES (0)",
        "INSERT INTO CV(B) VALUES ('true')",
        "INSERT INTO CV(B) VALUES ('T')",
        "INSERT INTO CV(B) VALUES (2)",
        "INSERT INTO CV(C5) VALUES (12345)",
        "INSERT INTO CV(C5) VALUES ('abcdefgh')",
        "INSERT INTO CV(C5) VALUES ('ab')",
        "INSERT INTO CV(VC) VALUES (CAST('ab' AS CHAR(3)))",
        "INSERT INTO CV(VCI) VALUES ('AbC')",
        "INSERT INTO CV(V1) VALUES ('abc')",
        "INSERT INTO CV(V1) VALUES (X'0102')",
        "INSERT INTO CV(V1) VALUES (X'010203')",
        "INSERT INTO CV(VB) VALUES (X'0102')",
        "INSERT INTO CV(BL) VALUES ('abc')",
        "INSERT INTO CV(CL) VALUES (X'414243')",
        "INSERT INTO CV(DT) VALUES ('2024-03-05')",
        "INSERT INTO CV(DT) VALUES ('2024-03-05 12:34:56')",
        "INSERT INTO CV(DT) VALUES (TIMESTAMP '2024-03-05 23:59:59')",
        "INSERT INTO CV(TM) VALUES ('12:34:56')",
        "INSERT INTO CV(TM) VALUES ('12:34:56.789')",
        "INSERT INTO CV(TS) VALUES ('2024-03-05 12:34:56')",
        "INSERT INTO CV(TS) VALUES (DATE '2024-03-05')",
        "INSERT INTO CV(TZ) VALUES ('2024-03-05 12:34:56+02:00')",
        "INSERT INTO CV(TZ) VALUES (TIMESTAMP '2024-03-05 12:34:56')",
        "INSERT INTO CV(U) VALUES ('123e4567-e89b-12d3-a456-426614174000')",
        "INSERT INTO CV(U) VALUES (X'123e4567e89b12d3a456426614174000')",
        "INSERT INTO CV(IYM) VALUES ('1-2')",
        "INSERT INTO CV(IYM) VALUES (INTERVAL '14' MONTH)",
        "INSERT INTO CV(IYM) VALUES ('1 02:03:04')",
        "INSERT INTO CV(IDT) VALUES ('1 02:03:04')",
        "INSERT INTO CV(IDT) VALUES (INTERVAL '26' HOUR)",
        "INSERT INTO CV(IDT) VALUES ('1-2')",
        "INSERT INTO CV(AR) VALUES (ARRAY[1, 2])",
        "INSERT INTO CV(AR) VALUES (ARRAY['1', '2'])",
        "INSERT INTO CV(J) VALUES ('{\"a\":1}')",
        "INSERT INTO CV(J) VALUES (1)",
        "INSERT INTO CV(J) VALUES (ARRAY[1, 2])",
    ];

    private static readonly string[] Checks =
    [
        "UPDATE CV SET TI = '12' WHERE ID = 1",
        "UPDATE CV SET TI = 300 WHERE ID = 1",
        "UPDATE CV SET C5 = 777 WHERE ID = 1",
        "UPDATE CV SET B = 'false' WHERE ID = 1",
        "UPDATE CV SET TS = DATE '2024-01-02' WHERE ID = 1",
    ];

    [JavaOracleFact]
    public void AssignmentConversionsMatchJdbc()
    {
        List<string> statements = [.. Setup, .. Assignments, .. Checks, "SELECT * FROM CV ORDER BY ID"];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);

        Assert.True(java.Count == port.Count,
            "lines: java=" + java.Count.ToString(CultureInfo.InvariantCulture)
            + " port=" + port.Count.ToString(CultureInfo.InvariantCulture));
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == port[i],
                $"line {i}:\n  java: {java[i]}\n  port: {port[i]}");
        }
    }

    private static List<string> RunPort(IReadOnlyList<string> statements)
    {
        using H2Connection connection = new(
            "Data Source=assignment_conv_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        List<string> lines = [];
        for (int i = 0; i < statements.Count; i++)
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = statements[i];
            try
            {
                if (!statements[i].StartsWith("SELECT", StringComparison.Ordinal))
                {
                    lines.Add("=" + i + ":U:" + command.ExecuteNonQuery());
                    continue;
                }

                using DbDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    List<string> cells = [];
                    for (int c = 0; c < reader.FieldCount; c++)
                    {
                        cells.Add(reader.IsDBNull(c) ? "NULL" : reader.GetString(c));
                    }

                    lines.Add("=" + i + ":R:" + string.Join('|', cells));
                }

                lines.Add("=" + i + ":Q");
            }
            catch (H2SharpException error)
            {
                lines.Add("=" + i + ":E:" + error.H2ErrorCode.ToString(CultureInfo.InvariantCulture));
            }
        }

        return lines;
    }

    private static List<string> RunJava(IReadOnlyList<string> statements)
    {
        List<string> raw = OracleRunner.Run(statements, """
            try (java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:assignment_conv")) {
              java.sql.Statement s = conn.createStatement();
              for (int i = 0; i < inputs.length; i++) {
                try {
                  if (!s.execute(inputs[i])) { System.out.println("=" + i + ":U:" + s.getUpdateCount()); continue; }
                  try (java.sql.ResultSet r = s.getResultSet()) {
                    while (r.next()) {
                      StringBuilder line = new StringBuilder("=" + i + ":R:");
                      for (int c = 1; c <= r.getMetaData().getColumnCount(); c++) {
                        if (c > 1) line.append('|');
                        String value = r.getString(c);
                        line.append(r.wasNull() ? "NULL" : value);
                      }
                      System.out.println(line);
                    }
                    System.out.println("=" + i + ":Q");
                  }
                } catch (java.sql.SQLException error) {
                  System.out.println("=" + i + ":E:" + error.getErrorCode());
                }
              }
            }
            """);
        return raw;
    }
}

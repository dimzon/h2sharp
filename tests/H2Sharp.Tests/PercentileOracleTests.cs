/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * MEDIAN, PERCENTILE_CONT and PERCENTILE_DISC, checked against the reference running the
 * same text.
 *
 * Every aggregate below is asked twice over: once over a column with an index (the quick
 * path that reads the answer off the index ends) and once in a shape that cannot use one
 * (the array path that sorts the values). The two must agree, and both must agree with the
 * reference.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using H2Sharp;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class PercentileOracleTests
{
    /// <summary>
    /// The statements, the same on both sides. The indexed column X is NOT NULL, Y is nullable
    /// with NULLs in the middle, D is a double, TS a timestamp, DT a date, S a varchar.
    /// </summary>
    [JavaOracleFact]
    public void ThePercentilesAnswerTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE T(ID INT PRIMARY KEY, X INT NOT NULL, Y INT, D DOUBLE, TS TIMESTAMP, DT DATE, "
                + "S VARCHAR, BI BIGINT, TM TIME, TMTZ TIME WITH TIME ZONE, IV INTERVAL DAY TO SECOND)",
            "CREATE INDEX IDX_X ON T(X)",
            "CREATE INDEX IDX_Y ON T(Y)",
            "CREATE INDEX IDX_YD ON T(Y DESC)",
            "CREATE INDEX IDX_D ON T(D)",
            "CREATE INDEX IDX_TS ON T(TS)",
            "CREATE INDEX IDX_DT ON T(DT)",
            "CREATE INDEX IDX_S ON T(S)",
            "CREATE INDEX IDX_BI ON T(BI)",
            "CREATE INDEX IDX_TM ON T(TM)",
            "CREATE INDEX IDX_TMTZ ON T(TMTZ)",
            "CREATE INDEX IDX_IV ON T(IV)",
            "INSERT INTO T VALUES (1, 1, NULL, 1.5, TIMESTAMP '2024-01-01 00:00:00', DATE '2024-01-01', 'a', "
                + "100, TIME '01:00:00', TIME WITH TIME ZONE '01:00:00+02:00', INTERVAL '1 02:00:00' DAY TO SECOND), "
                + "(2, 2, 2, 2.5, TIMESTAMP '2024-01-02 12:00:00', DATE '2024-01-02', 'b', "
                + "200, TIME '06:00:00', TIME WITH TIME ZONE '06:00:00-02:00', INTERVAL '2 04:00:00' DAY TO SECOND), "
                + "(3, 3, NULL, 3.5, TIMESTAMP '2024-01-03 06:00:00', DATE '2024-01-03', 'c', "
                + "300, TIME '12:00:00', TIME WITH TIME ZONE '12:00:00+00:00', INTERVAL '3 06:00:00' DAY TO SECOND), "
                + "(4, 4, 4, 4.5, TIMESTAMP '2024-01-04 18:00:00', DATE '2024-01-04', 'd', "
                + "400, TIME '18:00:00', TIME WITH TIME ZONE '18:00:00+05:30', INTERVAL '4 08:00:00' DAY TO SECOND), "
                + "(5, 5, 6, 5.5, TIMESTAMP '2024-01-05 00:00:00', DATE '2024-01-05', 'e', "
                + "500, TIME '23:00:00', TIME WITH TIME ZONE '23:00:00-05:00', INTERVAL '5 10:00:00' DAY TO SECOND)",
            // Median over indexed columns: the quick path.
            "SELECT MEDIAN(X) FROM T",
            "SELECT MEDIAN(Y) FROM T",
            "SELECT MEDIAN(D) FROM T",
            "SELECT MEDIAN(TS) FROM T",
            "SELECT MEDIAN(DT) FROM T",
            "SELECT MEDIAN(S) FROM T",
            "SELECT MEDIAN(BI) FROM T",
            "SELECT MEDIAN(TM) FROM T",
            "SELECT MEDIAN(TMTZ) FROM T",
            "SELECT MEDIAN(IV) FROM T",
            // Percentiles over indexed columns: 0.3 keeps a fractional row so the
            // interpolation runs, 0.25 lands on a whole row, 0.4 with a disc interpolates
            // by rounding.
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY X) FROM T",
            "SELECT PERCENTILE_CONT(0.3) WITHIN GROUP (ORDER BY X) FROM T",
            "SELECT PERCENTILE_CONT(0.25) WITHIN GROUP (ORDER BY X) FROM T",
            "SELECT PERCENTILE_DISC(0.4) WITHIN GROUP (ORDER BY X) FROM T",
            "SELECT PERCENTILE_DISC(0.3) WITHIN GROUP (ORDER BY X) FROM T",
            "SELECT PERCENTILE_CONT(0.3) WITHIN GROUP (ORDER BY Y) FROM T",
            "SELECT PERCENTILE_CONT(0.3) WITHIN GROUP (ORDER BY D) FROM T",
            "SELECT PERCENTILE_CONT(0.3) WITHIN GROUP (ORDER BY TS) FROM T",
            "SELECT PERCENTILE_CONT(0.3) WITHIN GROUP (ORDER BY DT) FROM T",
            "SELECT PERCENTILE_CONT(0.3) WITHIN GROUP (ORDER BY BI) FROM T",
            "SELECT PERCENTILE_CONT(0.3) WITHIN GROUP (ORDER BY TM) FROM T",
            "SELECT PERCENTILE_CONT(0.3) WITHIN GROUP (ORDER BY TMTZ) FROM T",
            "SELECT PERCENTILE_CONT(0.3) WITHIN GROUP (ORDER BY IV) FROM T",
            // A descending order flips the row the answer comes from, and with a fractional
            // row it swaps the interpolated pair as well.
            "SELECT PERCENTILE_CONT(0.25) WITHIN GROUP (ORDER BY X DESC) FROM T",
            "SELECT PERCENTILE_DISC(0.75) WITHIN GROUP (ORDER BY X DESC) FROM T",
            "SELECT PERCENTILE_CONT(0.3) WITHIN GROUP (ORDER BY X DESC) FROM T",
            // An empty indexed table answers NULL through the quick path.
            "CREATE TABLE E(X INT NOT NULL)",
            "CREATE INDEX IDX_EX ON E(X)",
            "SELECT MEDIAN(X) FROM E",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY X) FROM E",
            // A column that holds nothing but NULLs: the skip loop empties the count.
            "CREATE TABLE N(Y INT)",
            "CREATE INDEX IDX_NY ON N(Y)",
            "INSERT INTO N VALUES (NULL)",
            "SELECT MEDIAN(Y) FROM N",
            // NULLs only at the end of the chosen order: a second cursor counts them there.
            "CREATE TABLE DY(Y INT)",
            "CREATE INDEX IDX_DY ON DY(Y DESC)",
            "INSERT INTO DY VALUES (1), (NULL), (3)",
            "SELECT MEDIAN(Y) FROM DY",
            // A NULL fraction answers NULL, an out-of-range one is refused.
            "SELECT PERCENTILE_CONT(NULL) WITHIN GROUP (ORDER BY X) FROM T",
            "SELECT PERCENTILE_CONT(1.5) WITHIN GROUP (ORDER BY X) FROM T",
            "SELECT PERCENTILE_CONT(-0.5) WITHIN GROUP (ORDER BY X) FROM T",
            // The array path, no index to read from: the answers must not move.
            "SELECT MEDIAN(T.ID + 0) FROM T",
            "SELECT PERCENTILE_CONT(0.3) WITHIN GROUP (ORDER BY T.ID + 0) FROM T",
            "SELECT PERCENTILE_CONT(0.3) WITHIN GROUP (ORDER BY CAST(T.TS AS TIMESTAMP) + 0) FROM T",
            // An ungrouped aggregate over the dual table: the index path has no rows to read.
            "SELECT MEDIAN(X) FROM (SELECT 1 AS X WHERE 1 = 0)",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        Assert.True(
            java.Count == port.Count && java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
    }

    /// <summary>
    /// The types the first test does not interpolate — REAL, TIMESTAMP WITH TIME ZONE,
    /// BOOLEAN, the interval qualifiers — and the window aggregate form, the same on both
    /// sides.
    /// </summary>
    [JavaOracleFact]
    public void TheRemainingTypesAndWindowsAnswerTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE T2(ID INT, R REAL, TSTZ TIMESTAMP WITH TIME ZONE, B BOOLEAN, S VARCHAR, "
                + "IY INTERVAL YEAR, IM INTERVAL MONTH, IYM INTERVAL YEAR TO MONTH, "
                + "ID2 INTERVAL DAY, IH INTERVAL HOUR, IMIN INTERVAL MINUTE, ISEC INTERVAL SECOND, "
                + "IDH INTERVAL DAY TO HOUR, IDM INTERVAL DAY TO MINUTE, IHM INTERVAL HOUR TO MINUTE, "
                + "IHS INTERVAL HOUR TO SECOND, IMS INTERVAL MINUTE TO SECOND)",
            "CREATE INDEX IDX2_R ON T2(R)",
            "CREATE INDEX IDX2_TSTZ ON T2(TSTZ)",
            "CREATE INDEX IDX2_B ON T2(B)",
            "CREATE INDEX IDX2_S ON T2(S)",
            "INSERT INTO T2 VALUES (1, 1.5, TIMESTAMP WITH TIME ZONE '2024-01-01 01:00:00+01:00', TRUE, 'a', "
                + "INTERVAL '1' YEAR, INTERVAL '1' MONTH, INTERVAL '1-1' YEAR TO MONTH, "
                + "INTERVAL '1' DAY, INTERVAL '1' HOUR, INTERVAL '1' MINUTE, INTERVAL '1' SECOND, "
                + "INTERVAL '1 01' DAY TO HOUR, INTERVAL '1 01:01' DAY TO MINUTE, "
                + "INTERVAL '01:01' HOUR TO MINUTE, INTERVAL '01:01:01' HOUR TO SECOND, "
                + "INTERVAL '01:01' MINUTE TO SECOND)",
            "INSERT INTO T2 VALUES (3, 3.5, TIMESTAMP WITH TIME ZONE '2024-01-03 03:00:00+03:00', FALSE, 'c', "
                + "INTERVAL '3' YEAR, INTERVAL '3' MONTH, INTERVAL '3-3' YEAR TO MONTH, "
                + "INTERVAL '3' DAY, INTERVAL '3' HOUR, INTERVAL '3' MINUTE, INTERVAL '3' SECOND, "
                + "INTERVAL '3 03' DAY TO HOUR, INTERVAL '3 03:03' DAY TO MINUTE, "
                + "INTERVAL '03:03' HOUR TO MINUTE, INTERVAL '03:03:03' HOUR TO SECOND, "
                + "INTERVAL '03:03' MINUTE TO SECOND)",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY R) FROM T2",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY TSTZ) FROM T2",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY B) FROM T2",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY S) FROM T2",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY IY) FROM T2",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY IM) FROM T2",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY IYM) FROM T2",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY ID2) FROM T2",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY IH) FROM T2",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY IMIN) FROM T2",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY ISEC) FROM T2",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY IDH) FROM T2",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY IDM) FROM T2",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY IHM) FROM T2",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY IHS) FROM T2",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY IMS) FROM T2",
            // The window form: one answer per partition.
            "CREATE TABLE TW(G INT, V INT)",
            "INSERT INTO TW VALUES (1, 1), (1, 2), (1, 3), (2, 10), (2, 20)",
            "SELECT G, PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY V) OVER (PARTITION BY G) FROM TW ORDER BY G, V",
            "SELECT G, MEDIAN(V) OVER (PARTITION BY G) FROM TW ORDER BY G, V",
            "SELECT G, PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY V) OVER (PARTITION BY G) FROM TW ORDER BY G, V",
            // NULLs last by the default ordering: the second cursor counts them at the end.
            "SET DEFAULT_NULL_ORDERING LAST",
            "CREATE TABLE DZ(Y INT)",
            "CREATE INDEX IDZ ON DZ(Y DESC)",
            "INSERT INTO DZ VALUES (1), (2), (3), (4), (NULL)",
            "SELECT MEDIAN(Y) FROM DZ",
            "SELECT PERCENTILE_CONT(0.25) WITHIN GROUP (ORDER BY Y) FROM DZ",
            "SELECT PERCENTILE_CONT(0.75) WITHIN GROUP (ORDER BY Y) FROM DZ",
            // All NULLs with the NULLs last: the second cursor counts the count out.
            "CREATE TABLE DN(Y INT)",
            "CREATE INDEX IDN ON DN(Y)",
            "INSERT INTO DN VALUES (NULL)",
            "SELECT MEDIAN(Y) FROM DN",
            // The array path's rounding and reverse rules.
            "SELECT PERCENTILE_DISC(0.9) WITHIN GROUP (ORDER BY ID + 0) FROM T2",
            "SELECT PERCENTILE_DISC(0.1) WITHIN GROUP (ORDER BY ID + 0) FROM T2",
            "SELECT PERCENTILE_CONT(0.3) WITHIN GROUP (ORDER BY ID + 0 DESC) FROM T2",
            "SELECT PERCENTILE_CONT(0.75) WITHIN GROUP (ORDER BY ID + 0 DESC) FROM T2",
            // Two equal values: the interpolated pair is the same value, returned as it is.
            "CREATE TABLE TE(V INT)",
            "INSERT INTO TE VALUES (5), (5)",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY V + 0) FROM TE",
            // A TIME WITH TIME ZONE crossing midnight between two half-hour offsets.
            "CREATE TABLE TT(A TIME WITH TIME ZONE, B TIME WITH TIME ZONE)",
            "INSERT INTO TT VALUES (TIME WITH TIME ZONE '23:59:00+00:01', TIME WITH TIME ZONE '00:01:00+01:00'), "
                + "(TIME WITH TIME ZONE '00:00:30+01:00', TIME WITH TIME ZONE '23:58:30+00:00')",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY A) FROM TT",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY B) FROM TT",
            // A fraction whose offset does not fall on a whole second.
            "SELECT PERCENTILE_CONT(0.987654321) WITHIN GROUP (ORDER BY A) FROM TT",
            "SELECT PERCENTILE_CONT(0.987654321) WITHIN GROUP (ORDER BY B) FROM TT",
            "SELECT PERCENTILE_CONT(0.987654321) WITHIN GROUP (ORDER BY TSTZ) FROM T2",
            "SELECT PERCENTILE_CONT(0.012345679) WITHIN GROUP (ORDER BY TSTZ) FROM T2",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        Assert.True(
            java.Count == port.Count && java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
    }

    /// <summary>
    /// Runs the statements here and writes what the question answered: the rows, or the code of
    /// the error.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <returns>the lines</returns>
    private static List<string> RunPort(IReadOnlyList<string> statements)
    {
        SessionLocal session = new(
            new Database(ConnectionInfo.Memory("percentile_oracle")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
        List<string> lines = [];
        foreach (string sql in statements)
        {
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
                            cells.Add(value is null or ValueNull
                                ? "-"
                                : Regex.Replace(value.GetString() ?? "-", "\\s+", " "));
                        }

                        lines.Add("=" + string.Join(":", cells));
                    }
                }
                else
                {
                    _ = container.ExecuteUpdate(false);
                }
            }
            catch (Exception e)
            {
                lines.Add(e is H2SharpException h2
                    ? "=" + h2.H2ErrorCode.ToString(CultureInfo.InvariantCulture)
                    : "=" + e.GetType().Name + ":" + e.Message);
            }
        }

        return lines;
    }

    /// <summary>
    /// Runs the statements on the reference and writes what the question answered.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <returns>the lines</returns>
    private static List<string> RunJava(IReadOnlyList<string> statements)
        => OracleRunner.Run(
            statements,
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:percentile_oracle");
            java.sql.Statement stat = conn.createStatement();
            for (int i = 0; i < inputs.length; i++) {
              try {
                boolean isQuery = stat.execute(inputs[i]);
                if (isQuery) {
                  java.sql.ResultSet rs = stat.getResultSet();
                  int columns = rs.getMetaData().getColumnCount();
                  while (rs.next()) {
                    StringBuilder line = new StringBuilder("=");
                    for (int c = 1; c <= columns; c++) {
                      if (c > 1) {
                        line.append(':');
                      }
                      String s = rs.getString(c);
                      line.append(rs.wasNull() ? "-" : s.replaceAll("\\\\s+", " "));
                    }
                    System.out.println(line);
                  }
                }
              } catch (java.sql.SQLException e) {
                System.out.println("=" + e.getErrorCode());
              }
            }
            conn.close();
            """,
            extraJavaArgs: ["-Duser.language=en"]);
}

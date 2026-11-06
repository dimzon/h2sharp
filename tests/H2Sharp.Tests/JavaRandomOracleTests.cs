/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * JavaRandom against the JDK: the same 48-bit generator with the same seed must answer the
 * same numbers. The surface the port exposes through System.Random maps onto nextInt(bound),
 * the ranged nextInt, nextDouble, nextFloat and nextBytes; doubles and floats are compared as
 * raw bits, and RAND(seed) is compared through SQL as the engine itself asks for its numbers.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using H2Sharp.JavaLegacy;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class JavaRandomOracleTests
{
    [JavaOracleFact]
    public void JavaRandomAgreesWithTheJdk()
    {
        List<string> java = OracleRunner.Run([], Body(), null, null, null, null).ToList();
        List<string> csharp = [.. Lines()];
        Assert.True(
            java.Count == csharp.Count && java.SequenceEqual(csharp),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", csharp));
    }

    private static string Body() =>
        """
        {
        java.util.Random r = new java.util.Random(12345L);
        for (int i = 0; i < 8; i++) System.out.println("i|" + r.nextInt(1000));
        java.util.Random p = new java.util.Random(-7L);
        for (int i = 0; i < 4; i++) System.out.println("p|" + p.nextInt(256));
        java.util.Random g = new java.util.Random(42L);
        for (int i = 0; i < 4; i++) System.out.println("g|" + (g.nextInt(200) - 100));
        java.util.Random d = new java.util.Random(99L);
        for (int i = 0; i < 3; i++) System.out.println("d|" + Double.doubleToLongBits(d.nextDouble()));
        for (int i = 0; i < 3; i++) System.out.println("f|" + Float.floatToIntBits(d.nextFloat()));
        java.util.Random b = new java.util.Random(1L);
        byte[] bytes = new byte[7];
        b.nextBytes(bytes);
        StringBuilder sb = new StringBuilder("b|");
        for (byte x : bytes) sb.append(x).append(',');
        System.out.println(sb);
        java.util.Random s = new java.util.Random(5L);
        s.nextInt(10);
        s.setSeed(5L);
        System.out.println("s|" + s.nextInt(10));
        }
        """;

    private static IEnumerable<string> Lines()
    {
        JavaRandom r = new(12345L);
        for (int i = 0; i < 8; i++) yield return "i|" + r.Next(1000);
        JavaRandom p = new(-7L);
        for (int i = 0; i < 4; i++) yield return "p|" + p.Next(256);
        JavaRandom g = new(42L);
        for (int i = 0; i < 4; i++) yield return "g|" + g.Next(-100, 100);
        JavaRandom d = new(99L);
        for (int i = 0; i < 3; i++)
        {
            yield return "d|" + BitConverter.DoubleToInt64Bits(d.NextDouble())
                .ToString(CultureInfo.InvariantCulture);
        }

        for (int i = 0; i < 3; i++)
        {
            yield return "f|" + BitConverter.SingleToInt32Bits(d.NextSingle())
                .ToString(CultureInfo.InvariantCulture);
        }

        JavaRandom b = new(1L);
        byte[] bytes = new byte[7];
        b.NextBytes(bytes);
        // Java prints bytes signed; .NET bytes are unsigned, so cast for the comparison.
        yield return "b|" + string.Join(",", Array.ConvertAll(bytes, x => (sbyte)x)) + ",";
        JavaRandom s = new(5L);
        _ = s.Next(10);
        s.SetSeed(5L);
        yield return "s|" + s.Next(10);
    }

    [JavaOracleFact]
    public void RandAnswersTheSameAsTheReference()
    {
        string[] statements =
        [
            // The seed restarts the session's generator, so both calls answer the same.
            "SELECT RAND(1)",
            "SELECT RAND(1)",
            "SELECT RAND(-7)",
            "SELECT RAND(NULL)",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        Assert.True(
            java.Count == port.Count && java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
    }

    /// <summary>
    /// Runs the statements here and writes what the question answered: the rows, or the code of
    /// the error. The connection opts into the Java legacy random, the documented way to get the
    /// JDK sequence for RAND(seed).
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <returns>the lines</returns>
    private static List<string> RunPort(IReadOnlyList<string> statements)
    {
        using H2Sharp.Data.H2Connection connection =
            new("Data Source=rand_oracle_java_" + Guid.NewGuid().ToString("N"));
        connection.UseJavaLegacy();
        connection.Open();
        List<string> lines = [];
        foreach (string sql in statements)
        {
            try
            {
                using H2Sharp.Data.H2Command command = new(sql, connection);
                using System.Data.Common.DbDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    lines.Add("=" + (reader.IsDBNull(0) ? "-" : reader.GetString(0)));
                }
            }
            catch (Exception e)
            {
                lines.Add(e is H2Sharp.H2SharpException h2
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:rand_oracle");
            java.sql.Statement stat = conn.createStatement();
            for (int i = 0; i < inputs.length; i++) {
              java.sql.ResultSet rs = stat.executeQuery(inputs[i]);
              while (rs.next()) {
                String s = rs.getString(1);
                System.out.println("=" + (rs.wasNull() ? "-" : s));
              }
            }
            conn.close();
            """,
            extraJavaArgs: ["-Duser.language=en"]);
}

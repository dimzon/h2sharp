/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What a CLOB and a BLOB answer, checked against the reference running the same text.
 *
 * The value is what has to agree: a clob is the text it was written with and a blob is the
 * bytes it was written with, and the reference keeps them in the storage of the lobs while
 * this port keeps them in memory. Where the bytes live is the port's business; what they
 * say is not.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using H2Sharp;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Result;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class LobOracleTests
{
    /// <summary>
    /// The text of the clob: long enough to be a clob and not a string that fits anywhere.
    /// </summary>
    private const string Note = "a clob that is long enough to be worth its name: "
        + "0123456789abcdefghijklmnopqrstuvwxyz0123456789abcdefghijklmnopqrstuvwxyz"
        + "0123456789abcdefghijklmnopqrstuvwxyz0123456789abcdefghijklmnopqrstuvwxyz";

    /// <summary>
    /// The bytes of the blob: every byte value, so that a byte that is written as a sign
    /// comes back as the same byte.
    /// </summary>
    private static byte[] Picture
    {
        get
        {
            byte[] bytes = new byte[256];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = (byte)i;
            }

            return bytes;
        }
    }

    /// <summary>
    /// The same values, written and read on both sides.
    /// </summary>
    [JavaOracleFact]
    public void AClobAndABlobAnswerTheSameAsTheReference()
    {
        List<string> port = RunPort();
        List<string> java = RunJava();
        Assert.Equal(java.Count, port.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(
                java[i] == port[i],
                $"line {i}\n  java:   {java[i]}\n  csharp: {port[i]}");
        }
    }

    /// <summary>
    /// Writes the values here and reads them back.
    /// </summary>
    /// <returns>the lines</returns>
    private static List<string> RunPort()
    {
        SessionLocal session = new(
            new Database(new ConnectionInfo("jdbc:h2:mem:lob_oracle")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };

        CommandContainer create = Assert.IsType<CommandContainer>(
            session.PrepareCommand("CREATE TABLE T(ID INT PRIMARY KEY, NOTE CLOB, PICTURE BLOB)"));
        _ = create.ExecuteUpdate(false);

        // The values go in as the text and the bytes they are; the engine turns them into
        // the types of the columns.
        CommandContainer insert = Assert.IsType<CommandContainer>(
            session.PrepareCommand("INSERT INTO T VALUES(1, ?, ?)"));
        List<ParameterInterface> parameters = insert.GetParameters();
        parameters[0].SetValue(ValueVarchar.Get(Note), true);
        parameters[1].SetValue(ValueVarbinary.Get(Picture), true);
        _ = insert.ExecuteUpdate(false);

        List<string> lines = [];
        CommandContainer select = Assert.IsType<CommandContainer>(
            session.PrepareCommand("SELECT NOTE, PICTURE FROM T"));
        ResultInterface result = select.Query(-1);
        while (result.Next())
        {
            Value?[] row = result.CurrentRow();
            string note = row[0]!.GetString() ?? string.Empty;
            byte[] bytes = row[1]!.GetBytes() ?? [];
            StringBuilder hex = new();
            foreach (byte b in bytes)
            {
                _ = hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }

            lines.Add("=" + note + ":" + hex + ":" + bytes.Length.ToString(CultureInfo.InvariantCulture));
        }

        return lines;
    }

    /// <summary>
    /// Writes the values on the reference and reads them back.
    /// </summary>
    /// <returns>the lines</returns>
    private static List<string> RunJava()
        => OracleRunner.Run(
            [],
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:lob_oracle");
            java.sql.Statement stat = conn.createStatement();
            stat.execute("CREATE TABLE T(ID INT PRIMARY KEY, NOTE CLOB, PICTURE BLOB)");
            java.sql.PreparedStatement ps = conn.prepareStatement("INSERT INTO T VALUES(1, ?, ?)");
            ps.setString(1, "a clob that is long enough to be worth its name: "
                + "0123456789abcdefghijklmnopqrstuvwxyz0123456789abcdefghijklmnopqrstuvwxyz"
                + "0123456789abcdefghijklmnopqrstuvwxyz0123456789abcdefghijklmnopqrstuvwxyz");
            byte[] picture = new byte[256];
            for (int i = 0; i < picture.length; i++) {
              picture[i] = (byte) i;
            }
            ps.setBytes(2, picture);
            ps.executeUpdate();
            java.sql.ResultSet rs = stat.executeQuery("SELECT NOTE, PICTURE FROM T");
            while (rs.next()) {
              String note = rs.getString(1);
              byte[] bytes = rs.getBytes(2);
              StringBuilder hex = new StringBuilder();
              for (int i = 0; i < bytes.length; i++) {
                hex.append(String.format("%02x", bytes[i]));
              }
              System.out.println("=" + note + ":" + hex + ":" + bytes.length);
            }
            conn.close();
            """,
            extraJavaArgs: ["-Duser.language=en"]);
}

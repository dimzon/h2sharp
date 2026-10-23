/* Copyright 2026 H2Sharp contributors. Licensed under MPL 2.0 or EPL 1.0.
 * TIME and duration parameter mappings against the pinned H2 2.5 JDBC driver. */
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class TimeParameterOracleTests
{
    [JavaOracleFact]
    public void Issue41TimeOnlyAndDeclaredTimeSpanMatchJdbcLocalTime()
    {
        string[] inputs = ["00:00:00", "10:00:00", "12:34:56.1234567", "23:59:59.9999999"];
        using H2Connection connection = Open();
        using DbCommand setup = connection.CreateCommand();
        setup.CommandText = "CREATE TABLE T(ID INT PRIMARY KEY, TM TIME(9))";
        setup.ExecuteNonQuery();
        using DbCommand insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO T VALUES (@id, @time)";
        DbParameter id = insert.CreateParameter();
        id.ParameterName = "id";
        DbParameter time = insert.CreateParameter();
        time.ParameterName = "time";
        insert.Parameters.Add(id); insert.Parameters.Add(time);
        List<string> port = [];
        for (int mode = 0; mode < 2; mode++)
        {
            for (int i = 0; i < inputs.Length; i++)
            {
                TimeOnly value = TimeOnly.Parse(inputs[i], CultureInfo.InvariantCulture);
                int index = mode * inputs.Length + i;
                id.Value = index;
                time.Value = mode == 0 ? value : value.ToTimeSpan();
                if (mode == 0) time.ResetDbType();
                else time.DbType = DbType.Time;
                insert.ExecuteNonQuery();
                using DbCommand read = connection.CreateCommand();
                read.CommandText = "SELECT TM FROM T WHERE ID = " + index;
                using DbDataReader reader = read.ExecuteReader();
                Assert.Equal("TIME", reader.GetDataTypeName(0));
                Assert.Equal(typeof(TimeSpan), reader.GetFieldType(0));
                Assert.True(reader.Read());
                Assert.Equal(value.ToTimeSpan(), reader.GetValue(0));
                port.Add("=" + index + ':' + reader.GetString(0));
            }
        }
        List<string> java = OracleRunner.Run(inputs, """
            try (java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:time_parameters")) {
                conn.createStatement().execute("CREATE TABLE T(ID INT PRIMARY KEY, TM TIME(9))");
                try (java.sql.PreparedStatement insert = conn.prepareStatement("INSERT INTO T VALUES (?, ?)")) {
                    for (int mode = 0; mode < 2; mode++) {
                        for (int i = 0; i < inputs.length; i++) {
                            int index = mode * inputs.length + i;
                            insert.setInt(1, index);
                            insert.setObject(2, java.time.LocalTime.parse(inputs[i]));
                            insert.executeUpdate();
                            try (java.sql.ResultSet r = conn.createStatement().executeQuery("SELECT TM FROM T WHERE ID = " + index)) {
                                r.next(); System.out.println("=" + index + ":" + r.getString(1));
                            }
                        }
                    }
                }
            }
            """);
        Assert.Equal(java, port);
    }

    [JavaOracleFact]
    public void UntypedTimeSpanRemainsAnIntervalAndCannotBeAssignedToTime()
    {
        using H2Connection connection = Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE T(TM TIME)";
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO T VALUES (@value)";
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "value";
        parameter.Value = TimeSpan.FromHours(10);
        command.Parameters.Add(parameter);
        H2SharpException error = Assert.Throws<H2SharpException>(() => command.ExecuteNonQuery());
        List<string> port = ["=E:" + error.H2ErrorCode];
        // CLR TimeSpan uses the port's existing DAY TO SECOND mapping; JDBC Duration
        // defaults to SECOND. Compare the same declared SQL interval type.
        command.CommandText = "SELECT CAST(@value AS INTERVAL DAY TO SECOND)";
        using (DbDataReader reader = command.ExecuteReader())
        {
            Assert.True(reader.Read());
            port.Add("=" + reader.GetDataTypeName(0) + '|' + reader.GetString(0));
        }
        parameter.Value = DBNull.Value;
        parameter.DbType = DbType.Time;
        command.CommandText = "INSERT INTO T VALUES (@value)";
        port.Add("=U:" + command.ExecuteNonQuery());
        List<string> java = OracleRunner.Run([], """
            try (java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:duration_parameters")) {
                conn.createStatement().execute("CREATE TABLE T(TM TIME)");
                try (java.sql.PreparedStatement s = conn.prepareStatement("INSERT INTO T VALUES (?)")) {
                    s.setObject(1, java.time.Duration.ofHours(10));
                    try { s.executeUpdate(); } catch (java.sql.SQLException e) { System.out.println("=E:" + e.getErrorCode()); }
                }
                try (java.sql.PreparedStatement s = conn.prepareStatement("SELECT CAST(? AS INTERVAL DAY TO SECOND)")) {
                    s.setObject(1, java.time.Duration.ofHours(10));
                    try (java.sql.ResultSet r = s.executeQuery()) {
                        r.next(); System.out.println("=" + r.getMetaData().getColumnTypeName(1) + "|" + r.getString(1));
                    }
                }
                try (java.sql.PreparedStatement s = conn.prepareStatement("INSERT INTO T VALUES (?)")) {
                    s.setNull(1, java.sql.Types.TIME); System.out.println("=U:" + s.executeUpdate());
                }
            }
            """);
        Assert.Equal(java, port);
    }

    private static H2Connection Open()
    {
        H2Connection connection = new("jdbc:h2:mem:time_oracle_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        return connection;
    }
}

/* Copyright 2026 H2Sharp contributors. MPL 2.0 or EPL 1.0.
 * Query result reuse across dirty reads, savepoints, commits and snapshot changes. */
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class QueryResultCacheOracleTests
{
    [JavaOracleFact]
    public void DirtyRowsAreNotReusedAfterAnotherTransactionRollsBack()
    {
        List<string> port = [];
        foreach (int reuse in new[] { 0, 1 })
        {
            string connectionString = NewConnectionString();
            using H2Connection main = Open(connectionString);
            SetUp(main, reuse);
            using H2Connection writer = Open(connectionString);
            using H2Connection reader = Open(connectionString);
            using DbTransaction write = writer.BeginTransaction();
            using DbTransaction read = reader.BeginTransaction(IsolationLevel.ReadUncommitted);
            Execute(writer, write, "UPDATE T SET V=1 WHERE ID=1");
            using DbCommand select = Select(reader, read);
            port.Add($"{reuse}:dirty:{select.ExecuteScalar()}");
            write.Rollback();
            port.Add($"{reuse}:rolledBack:{select.ExecuteScalar()}");
            read.Rollback();
        }
        Assert.Equal(OracleRunner.Run(["0", "1"], """
            for (String input : inputs) {
              String url="jdbc:h2:mem:cache_dirty_"+input;
              try (java.sql.Connection main=java.sql.DriverManager.getConnection(url);
                   java.sql.Connection writer=java.sql.DriverManager.getConnection(url);
                   java.sql.Connection reader=java.sql.DriverManager.getConnection(url)) {
                try(java.sql.Statement s=main.createStatement()) {
                  s.execute("CREATE TABLE T(ID INT PRIMARY KEY,V INT)");
                  s.execute("INSERT INTO T VALUES(1,0),(2,10)");
                  s.execute("SET OPTIMIZE_REUSE_RESULTS "+input);
                }
                writer.setAutoCommit(false);
                reader.setTransactionIsolation(java.sql.Connection.TRANSACTION_READ_UNCOMMITTED);
                reader.setAutoCommit(false);
                try(java.sql.Statement update=writer.createStatement();
                    java.sql.PreparedStatement select=reader.prepareStatement("SELECT V FROM T WHERE ID=?")) {
                  select.setInt(1,1);
                  update.executeUpdate("UPDATE T SET V=1 WHERE ID=1");
                  try(java.sql.ResultSet r=select.executeQuery()) {
                    r.next(); System.out.println(input+":dirty:"+r.getInt(1));
                  }
                  writer.rollback();
                  try(java.sql.ResultSet r=select.executeQuery()) {
                    r.next(); System.out.println(input+":rolledBack:"+r.getInt(1));
                  }
                }
                reader.rollback();
              }
            }
            """), port);
        Assert.Contains("1:rolledBack:0", port);
    }

    [JavaOracleFact]
    public void OwnWritesSavepointsParameterRebindingAndCommitsInvalidateResults()
    {
        List<string> port = [];
        foreach (int reuse in new[] { 0, 1 })
        {
            using H2Connection connection = Open(NewConnectionString());
            SetUp(connection, reuse);
            using DbTransaction transaction = connection.BeginTransaction();
            using DbCommand select = Select(connection, transaction);
            port.Add($"{reuse}:initial:{select.ExecuteScalar()}");
            Execute(connection, transaction, "UPDATE T SET V=1 WHERE ID=1");
            port.Add($"{reuse}:ownWrite:{select.ExecuteScalar()}");
            transaction.Save("S");
            Execute(connection, transaction, "UPDATE T SET V=2 WHERE ID=1");
            port.Add($"{reuse}:savepointWrite:{select.ExecuteScalar()}");
            transaction.Rollback("S");
            port.Add($"{reuse}:savepointRollback:{select.ExecuteScalar()}");
            select.Parameters[0].Value = 2;
            port.Add($"{reuse}:parameter2:{select.ExecuteScalar()}");
            select.Parameters[0].Value = 1;
            port.Add($"{reuse}:parameter1:{select.ExecuteScalar()}");
            transaction.Commit();
            select.Transaction = null;
            Execute(connection, null, "UPDATE T SET V=7 WHERE ID=1");
            port.Add($"{reuse}:committed:{select.ExecuteScalar()}");
        }
        Assert.Equal(OracleRunner.Run(["0", "1"], """
            for(String input:inputs) {
              try(java.sql.Connection c=java.sql.DriverManager.getConnection("jdbc:h2:mem:cache_writes_"+input);
                  java.sql.Statement s=c.createStatement()) {
                  s.execute("CREATE TABLE T(ID INT PRIMARY KEY,V INT)");
                  s.execute("INSERT INTO T VALUES(1,0),(2,10)");
                  s.execute("SET OPTIMIZE_REUSE_RESULTS "+input);
                  c.setAutoCommit(false);
                  try(java.sql.PreparedStatement q=c.prepareStatement("SELECT V FROM T WHERE ID=?")) {
                    q.setInt(1,1);
                    try(java.sql.ResultSet r=q.executeQuery()) {r.next();System.out.println(input+":initial:"+r.getInt(1));}
                    s.executeUpdate("UPDATE T SET V=1 WHERE ID=1");
                    try(java.sql.ResultSet r=q.executeQuery()) {r.next();System.out.println(input+":ownWrite:"+r.getInt(1));}
                    java.sql.Savepoint save=c.setSavepoint("S");
                    s.executeUpdate("UPDATE T SET V=2 WHERE ID=1");
                    try(java.sql.ResultSet r=q.executeQuery()) {r.next();System.out.println(input+":savepointWrite:"+r.getInt(1));}
                    c.rollback(save);
                    try(java.sql.ResultSet r=q.executeQuery()) {r.next();System.out.println(input+":savepointRollback:"+r.getInt(1));}
                    q.setInt(1,2);
                    try(java.sql.ResultSet r=q.executeQuery()) {r.next();System.out.println(input+":parameter2:"+r.getInt(1));}
                    q.setInt(1,1);
                    try(java.sql.ResultSet r=q.executeQuery()) {r.next();System.out.println(input+":parameter1:"+r.getInt(1));}
                    c.commit();c.setAutoCommit(true);
                    s.executeUpdate("UPDATE T SET V=7 WHERE ID=1");
                    try(java.sql.ResultSet r=q.executeQuery()) {r.next();System.out.println(input+":committed:"+r.getInt(1));}
                  }
              }
            }
            """), port);
    }

    [JavaOracleFact]
    public void RepeatableReadResultCacheDoesNotSurviveIntoTheNextSnapshot()
    {
        List<string> port = [];
        foreach (int reuse in new[] { 0, 1 })
        {
            string connectionString = NewConnectionString();
            using H2Connection reader = Open(connectionString);
            SetUp(reader, reuse);
            using H2Connection writer = Open(connectionString);
            using DbCommand select = Select(reader, null);
            using (DbTransaction first = reader.BeginTransaction(IsolationLevel.RepeatableRead))
            {
                select.Transaction = first;
                port.Add($"{reuse}:snapshot0:{select.ExecuteScalar()}");
                Execute(writer, null, "UPDATE T SET V=1 WHERE ID=1");
                port.Add($"{reuse}:sameSnapshot:{select.ExecuteScalar()}");
                first.Commit();
            }
            using (DbTransaction second = reader.BeginTransaction(IsolationLevel.RepeatableRead))
            {
                select.Transaction = second;
                port.Add($"{reuse}:nextSnapshot:{select.ExecuteScalar()}");
                second.Commit();
            }
        }
        Assert.Equal(OracleRunner.Run(["0", "1"], """
            for(String input:inputs) {
              String url="jdbc:h2:mem:cache_snapshot_"+input;
              try(java.sql.Connection reader=java.sql.DriverManager.getConnection(url);
                  java.sql.Connection writer=java.sql.DriverManager.getConnection(url);
                  java.sql.Statement setup=reader.createStatement();
                  java.sql.Statement update=writer.createStatement()) {
                setup.execute("CREATE TABLE T(ID INT PRIMARY KEY,V INT)");
                setup.execute("INSERT INTO T VALUES(1,0),(2,10)");
                setup.execute("SET OPTIMIZE_REUSE_RESULTS "+input);
                reader.setTransactionIsolation(java.sql.Connection.TRANSACTION_REPEATABLE_READ);
                reader.setAutoCommit(false);
                try(java.sql.PreparedStatement q=reader.prepareStatement("SELECT V FROM T WHERE ID=?")) {
                  q.setInt(1,1);
                  try(java.sql.ResultSet r=q.executeQuery()) {r.next();System.out.println(input+":snapshot0:"+r.getInt(1));}
                  update.executeUpdate("UPDATE T SET V=1 WHERE ID=1");
                  try(java.sql.ResultSet r=q.executeQuery()) {r.next();System.out.println(input+":sameSnapshot:"+r.getInt(1));}
                  reader.commit();
                  try(java.sql.ResultSet r=q.executeQuery()) {r.next();System.out.println(input+":nextSnapshot:"+r.getInt(1));}
                  reader.commit();
                }
              }
            }
            """), port);
        Assert.Contains("1:sameSnapshot:0", port);
        Assert.Contains("1:nextSnapshot:1", port);
    }

    private static string NewConnectionString() => $"Data Source=cache_oracle_{Guid.NewGuid():N};Storage=Memory";

    private static H2Connection Open(string connectionString)
    {
        H2Connection connection = new(connectionString);
        connection.Open();
        return connection;
    }

    private static void SetUp(DbConnection connection, int reuse)
    {
        Execute(connection, null, "CREATE TABLE T(ID INT PRIMARY KEY,V INT)");
        Execute(connection, null, "INSERT INTO T VALUES(1,0),(2,10)");
        Execute(connection, null, "SET OPTIMIZE_REUSE_RESULTS " + reuse);
    }

    private static void Execute(DbConnection connection, DbTransaction? transaction, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static DbCommand Select(DbConnection connection, DbTransaction? transaction)
    {
        DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT V FROM T WHERE ID=?";
        DbParameter parameter = command.CreateParameter();
        parameter.Value = 1;
        command.Parameters.Add(parameter);
        return command;
    }
}

/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * FOREIGN KEY, seen through ADO.NET.
 *
 * A child row without a parent is refused, and a parent row with children cannot go away
 * unless the action says what happens to them instead.
 */
using System.Data.Common;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2ForeignKeyTests
{
    [Fact]
    public void AChildWithoutAParentIsRefused()
    {
        using H2Connection connection = Open("fk_child");
        Execute(connection, "CREATE TABLE PARENT(ID INT PRIMARY KEY)");
        Execute(connection, "CREATE TABLE CHILD(ID INT PRIMARY KEY, PID INT, FOREIGN KEY (PID) REFERENCES PARENT(ID))");
        Execute(connection, "INSERT INTO PARENT VALUES (1)");
        Execute(connection, "INSERT INTO CHILD VALUES (10, 1)");
        Execute(connection, "INSERT INTO CHILD VALUES (11, NULL)");

        H2SharpException missing = Assert.Throws<H2SharpException>(
            () => Execute(connection, "INSERT INTO CHILD VALUES (12, 2)"));
        Assert.Equal(23506, missing.H2ErrorCode);

        Assert.Equal(["10", "11"], Ids(connection, "SELECT ID FROM CHILD ORDER BY ID"));
    }

    [Fact]
    public void AParentWithChildrenCannotGoAway()
    {
        using H2Connection connection = Open("fk_parent");
        Execute(connection, "CREATE TABLE PARENT(ID INT PRIMARY KEY)");
        Execute(connection, "CREATE TABLE CHILD(ID INT PRIMARY KEY, PID INT, FOREIGN KEY (PID) REFERENCES PARENT(ID))");
        Execute(connection, "INSERT INTO PARENT VALUES (1)");
        Execute(connection, "INSERT INTO CHILD VALUES (10, 1)");

        H2SharpException stranded = Assert.Throws<H2SharpException>(
            () => Execute(connection, "DELETE FROM PARENT WHERE ID = 1"));
        Assert.Equal(23503, stranded.H2ErrorCode);

        Execute(connection, "DELETE FROM CHILD WHERE ID = 10");
        Execute(connection, "DELETE FROM PARENT WHERE ID = 1");
        Assert.Equal([], Ids(connection, "SELECT ID FROM PARENT"));
    }

    [Fact]
    public void ADeleteCascadesWhenToldTo()
    {
        using H2Connection connection = Open("fk_cascade");
        Execute(connection, "CREATE TABLE PARENT(ID INT PRIMARY KEY)");
        Execute(connection, "CREATE TABLE CHILD(ID INT PRIMARY KEY, PID INT, FOREIGN KEY (PID) REFERENCES PARENT(ID) ON DELETE CASCADE)");
        Execute(connection, "INSERT INTO PARENT VALUES (1), (2)");
        Execute(connection, "INSERT INTO CHILD VALUES (10, 1), (11, 1), (12, 2)");
        Execute(connection, "DELETE FROM PARENT WHERE ID = 1");

        Assert.Equal(["12"], Ids(connection, "SELECT ID FROM CHILD ORDER BY ID"));
    }

    [Fact]
    public void AColumnLevelReferenceWorks()
    {
        using H2Connection connection = Open("fk_column");
        Execute(connection, "CREATE TABLE PARENT(ID INT PRIMARY KEY)");
        Execute(connection, "CREATE TABLE CHILD(ID INT PRIMARY KEY, PID INT REFERENCES PARENT(ID))");
        Execute(connection, "INSERT INTO PARENT VALUES (1)");

        H2SharpException missing = Assert.Throws<H2SharpException>(
            () => Execute(connection, "INSERT INTO CHILD VALUES (10, 2)"));
        Assert.Equal(23506, missing.H2ErrorCode);

        Execute(connection, "INSERT INTO CHILD VALUES (10, 1)");
        Assert.Equal(["1"], Ids(connection, "SELECT PID FROM CHILD"));
    }

    [Fact]
    public void TheCatalogNamesTheKey()
    {
        using H2Connection connection = Open("fk_catalog");
        Execute(connection, "CREATE TABLE PARENT(ID INT PRIMARY KEY)");
        Execute(connection, "CREATE TABLE CHILD(ID INT PRIMARY KEY, PID INT, CONSTRAINT FK_C FOREIGN KEY (PID) REFERENCES PARENT(ID) ON DELETE CASCADE)");

        Assert.Equal(
            ["FK_C:NO ACTION:CASCADE"],
            Strings(
                connection,
                "SELECT CONCAT(CONSTRAINT_NAME, ':', UPDATE_RULE, ':', DELETE_RULE)"
                + " FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS"));
        Assert.Equal(
            ["PID"],
            Strings(
                connection,
                "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE WHERE CONSTRAINT_NAME = 'FK_C'"));
    }

    [Fact]
    public void AKeyNeedsAParentWithAKey()
    {
        using H2Connection connection = Open("fk_missing");
        Execute(connection, "CREATE TABLE PARENT(ID INT PRIMARY KEY)");

        H2SharpException noTable = Assert.Throws<H2SharpException>(
            () => Execute(connection, "CREATE TABLE C1(ID INT, PID INT, FOREIGN KEY (PID) REFERENCES NOSUCH(ID))"));
        Assert.Equal(42102, noTable.H2ErrorCode);

        H2SharpException noColumn = Assert.Throws<H2SharpException>(
            () => Execute(connection, "CREATE TABLE C2(ID INT, PID INT, FOREIGN KEY (PID) REFERENCES PARENT(NOSUCH))"));
        Assert.Equal(42122, noColumn.H2ErrorCode);

        Execute(connection, "CREATE TABLE NOPK(ID INT)");
        H2SharpException noKey = Assert.Throws<H2SharpException>(
            () => Execute(connection, "CREATE TABLE C3(ID INT, PID INT, FOREIGN KEY (PID) REFERENCES NOPK(ID))"));
        Assert.Equal(90057, noKey.H2ErrorCode);
    }

    /// <summary>
    /// Opens a connection to a database of its own.
    /// </summary>
    /// <param name="name">the name of the database</param>
    /// <returns>the connection</returns>
    private static H2Connection Open(string name)
    {
        H2Connection connection = new($"Data Source=ado_{name};Storage=Memory");
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Runs a statement that needs no answer.
    /// </summary>
    /// <param name="connection">the connection</param>
    /// <param name="sql">the statement</param>
    private static void Execute(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        _ = command.ExecuteNonQuery();
    }

    /// <summary>
    /// Reads the ids of the rows of a query.
    /// </summary>
    /// <param name="connection">the connection</param>
    /// <param name="sql">the query</param>
    /// <returns>the values</returns>
    private static string[] Ids(H2Connection connection, string sql) => Strings(connection, sql);

    /// <summary>
    /// Reads the first column of the rows of a query.
    /// </summary>
    /// <param name="connection">the connection</param>
    /// <param name="sql">the query</param>
    /// <returns>the values</returns>
    private static string[] Strings(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using DbDataReader reader = command.ExecuteReader();
        System.Collections.Generic.List<string> values = [];
        while (reader.Read())
        {
            object value = reader.GetValue(0);
            values.Add(value is System.DBNull ? "-" : value.ToString() ?? "-");
        }

        return [.. values];
    }
}

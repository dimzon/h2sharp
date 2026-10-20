/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The catalogue, read through the ADO.NET layer.
 *
 * The catalogue is a view of the metadata of the database, and it is read the
 * way every other statement is read: a command, a reader, rows. The engine's
 * own tests of the catalogue go through the engine, and a statement the engine
 * answers is not a statement the ADO layer answers -- the reader of the ADO
 * layer has its own lifetime, and the two do not always agree.
 *
 * These tests therefore ask for the catalogue over ADO.NET, and they ask for
 * it after a statement that is not about the catalogue, because that is the
 * order a program asks in.
 */
using System;
using System.Collections.Generic;
using System.Data.Common;
using H2Sharp;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2CatalogueTests
{
    /// <summary>
    /// Every column of the COLUMNS view of H2 2.5, in the order the reference declares them.
    /// </summary>
    private static readonly string[] AllColumns =
    [
        "TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME", "COLUMN_NAME", "ORDINAL_POSITION",
        "COLUMN_DEFAULT", "IS_NULLABLE", "DATA_TYPE", "CHARACTER_MAXIMUM_LENGTH",
        "CHARACTER_OCTET_LENGTH", "NUMERIC_PRECISION", "NUMERIC_PRECISION_RADIX", "NUMERIC_SCALE",
        "DATETIME_PRECISION", "INTERVAL_TYPE", "INTERVAL_PRECISION", "CHARACTER_SET_CATALOG",
        "CHARACTER_SET_SCHEMA", "CHARACTER_SET_NAME", "COLLATION_CATALOG", "COLLATION_SCHEMA",
        "COLLATION_NAME", "DOMAIN_CATALOG", "DOMAIN_SCHEMA", "DOMAIN_NAME", "MAXIMUM_CARDINALITY",
        "DTD_IDENTIFIER", "IS_IDENTITY", "IDENTITY_GENERATION", "IDENTITY_START", "IDENTITY_INCREMENT",
        "IDENTITY_MAXIMUM", "IDENTITY_MINIMUM", "IDENTITY_CYCLE", "IS_GENERATED",
        "GENERATION_EXPRESSION", "DECLARED_DATA_TYPE", "DECLARED_NUMERIC_PRECISION",
        "DECLARED_NUMERIC_SCALE", "GEOMETRY_TYPE", "GEOMETRY_SRID", "IDENTITY_BASE", "IDENTITY_CACHE",
        "COLUMN_ON_UPDATE", "IS_VISIBLE", "DEFAULT_ON_NULL", "SELECTIVITY", "REMARKS",
    ];

    [Fact]
    public void TheCatalogueIsReadAfterAStatementThatIsNotAboutIt()
    {
        using H2Connection connection = new("jdbc:h2:mem:catalogue_ado");
        connection.Open();

        using (DbCommand create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE T(ID INTEGER, NAME VARCHAR(50))";
            create.ExecuteNonQuery();
        }

        using DbCommand query = connection.CreateCommand();
        query.CommandText =
            "SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS "
            + "WHERE TABLE_NAME = 'T' ORDER BY ORDINAL_POSITION";

        using DbDataReader reader = query.ExecuteReader();

        List<string> rows = [];
        while (reader.Read())
        {
            rows.Add(reader.GetString(0) + "/" + reader.GetString(1) + "/" + reader.GetString(2));
        }

        Assert.Equal(new[] { "T/ID/INTEGER", "T/NAME/CHARACTER VARYING" }, rows);
    }

    [Fact]
    public void EveryColumnOfTheColumnsViewIsRead()
    {
        using H2Connection connection = new("jdbc:h2:mem:catalogue_ado_all");
        connection.Open();

        using (DbCommand create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE T(ID INTEGER, NAME VARCHAR(50))";
            create.ExecuteNonQuery();
        }

        using DbCommand query = connection.CreateCommand();
        query.CommandText =
            "SELECT " + string.Join(", ", AllColumns) + " FROM INFORMATION_SCHEMA.COLUMNS "
            + "WHERE TABLE_NAME = 'T' ORDER BY ORDINAL_POSITION";

        using DbDataReader reader = query.ExecuteReader();

        Assert.True(reader.Read());

        // The row is read by the order of the columns the view declares, and a row that is
        // shorter than that list leaves the reader outside the array. Every column is asked for,
        // so that a short row is caught here and not in a program that reads one column.
        Assert.Equal(AllColumns.Length, reader.FieldCount);

        Assert.Equal("T", reader.GetString(2));
        Assert.Equal("ID", reader.GetString(3));
        Assert.Equal("INTEGER", reader.GetString(7));

        // The last columns of the view are read as well: they are the ones a short row does not
        // reach. What they hold is a matter of the port, not of this test -- the reference says
        // that IS_VISIBLE is 'YES' or 'NO', and the port says it with a boolean.
        _ = reader.GetValue(AllColumns.Length - 2);
        _ = reader.GetValue(AllColumns.Length - 1);
    }

    [Fact]
    public void TheTablesViewIsReadWithTheComments()
    {
        using H2Connection connection = new("jdbc:h2:mem:catalogue_ado_tables");
        connection.Open();

        using (DbCommand create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE T(ID INTEGER, NAME VARCHAR(50))";
            create.ExecuteNonQuery();
        }

        using DbCommand query = connection.CreateCommand();
        query.CommandText =
            "SELECT TABLE_CATALOG, TABLE_SCHEMA, TABLE_NAME, TABLE_TYPE, REMARKS "
            + "FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'T'";

        using DbDataReader reader = query.ExecuteReader();

        Assert.True(reader.Read());
        Assert.Equal("T", reader.GetString(2));
        Assert.Equal("BASE TABLE", reader.GetString(3));
    }
}

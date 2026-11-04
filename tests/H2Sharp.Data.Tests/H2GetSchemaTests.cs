/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The connection's schema discovery: the reference metadata rows in DataTables, the
 * restriction values that select them, and the answers for the cases that have none.
 */
using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2GetSchemaTests
{
    [Fact]
    public void MetaDataCollectionsAdvertiseTheRestrictions()
    {
        using H2Connection connection = NewConnection();
        connection.Open();

        DataTable collections = connection.GetSchema();
        Assert.Equal(DbMetaDataCollectionNames.MetaDataCollections, collections.TableName);
        Assert.Equal(4, Int(Row(collections, "CollectionName", "Tables"), "NumberOfRestrictions"));
        Assert.Equal(4, Int(Row(collections, "CollectionName", "Columns"), "NumberOfRestrictions"));
        Assert.Equal(4, Int(Row(collections, "CollectionName", "Indexes"), "NumberOfRestrictions"));
        Assert.Equal(3, Int(Row(collections, "CollectionName", "ForeignKeys"), "NumberOfRestrictions"));
        Assert.Equal(3, Int(Row(collections, "CollectionName", "Procedures"), "NumberOfRestrictions"));
        Assert.Equal(2, Int(Row(collections, "CollectionName", "Schemas"), "NumberOfRestrictions"));

        DataTable restrictions = connection.GetSchema(DbMetaDataCollectionNames.Restrictions);
        DataRow tableRestrictions = Row(restrictions, "CollectionName", "Tables");
        Assert.Equal("Catalog", tableRestrictions["RestrictionName"]);
        Assert.Equal(1, Int(tableRestrictions, "RestrictionNumber"));
    }

    [Fact]
    public void TablesAndColumnsUseTheReferenceColumns()
    {
        using H2Connection connection = NewConnection();
        connection.Open();
        Setup(connection);

        DataTable tables = connection.GetSchema("Tables", [null, "PUBLIC", null, null]);
        Assert.Equal(
            ["TABLE_CAT", "TABLE_SCHEM", "TABLE_NAME", "TABLE_TYPE", "REMARKS", "TYPE_CAT",
                "TYPE_SCHEM", "TYPE_NAME", "SELF_REFERENCING_COL_NAME", "REF_GENERATION"],
            Names(tables));
        Assert.Equal("BASE TABLE", Row(tables, "TABLE_NAME", "P")["TABLE_TYPE"]);
        Assert.Equal("VIEW", Row(tables, "TABLE_NAME", "V")["TABLE_TYPE"]);
        Assert.Equal("parent", Row(tables, "TABLE_NAME", "P")["REMARKS"]);

        DataTable columns = connection.GetSchema("Columns", [null, "PUBLIC", "P", null]);
        DataRow name = Row(columns, "COLUMN_NAME", "NAME");
        Assert.Equal("CHARACTER VARYING", name["TYPE_NAME"]);
        Assert.Equal("'x'", name["COLUMN_DEF"]);
        Assert.Equal("YES", name["IS_NULLABLE"]);
        DataRow generated = Row(columns, "COLUMN_NAME", "N");
        Assert.Equal("YES", generated["IS_GENERATEDCOLUMN"]);
        Assert.Equal(DBNull.Value, generated["COLUMN_DEF"]);
        Assert.Equal("YES", Row(columns, "COLUMN_NAME", "ID")["IS_AUTOINCREMENT"]);
    }

    [Fact]
    public void RestrictionsFilterTheRows()
    {
        using H2Connection connection = NewConnection();
        connection.Open();
        Setup(connection);

        DataTable tables = connection.GetSchema("Tables", [null, "PUBLIC", "P", null]);
        Assert.Single(tables.Rows);
        Assert.Equal("P", tables.Rows[0]["TABLE_NAME"]);

        DataTable columns = connection.GetSchema("Columns", [null, "PUBLIC", "P", "NAME"]);
        Assert.Single(columns.Rows);
        Assert.Equal("NAME", columns.Rows[0]["COLUMN_NAME"]);

        DataTable schemas = connection.GetSchema("Schemas", [null, "PUBLIC"]);
        Assert.Single(schemas.Rows);
        Assert.Equal("PUBLIC", schemas.Rows[0]["TABLE_SCHEM"]);

        DataTable catalogs = connection.GetSchema("Catalogs");
        Assert.Single(catalogs.Rows);
        Assert.Equal(connection.Database, catalogs.Rows[0]["TABLE_CAT"]);
    }

    [Fact]
    public void IndexesAndForeignKeysAreAnsweredForATable()
    {
        using H2Connection connection = NewConnection();
        connection.Open();
        Setup(connection);

        DataTable indexes = connection.GetSchema("Indexes", [null, "PUBLIC", "C", "IDX_C_PID"]);
        DataRow index = Assert.IsType<DataRow>(Assert.Single(indexes.Rows));
        Assert.Equal("IDX_C_PID", index["INDEX_NAME"]);
        Assert.Equal("PID", index["COLUMN_NAME"]);
        Assert.True((bool)index["NON_UNIQUE"]);

        DataTable keys = connection.GetSchema("ForeignKeys", [null, "PUBLIC", "C"]);
        DataRow key = Assert.IsType<DataRow>(Assert.Single(keys.Rows));
        Assert.Equal("FK_C_P", key["FK_NAME"]);
        Assert.Equal("P", key["PKTABLE_NAME"]);
        Assert.Equal("PID", key["FKCOLUMN_NAME"]);

        Assert.Throws<ArgumentException>(() => connection.GetSchema("Indexes"));
        Assert.Throws<ArgumentException>(() => connection.GetSchema("ForeignKeys"));
    }

    [Fact]
    public void DataTypesAndReservedWordsComeFromTheCatalogue()
    {
        using H2Connection connection = NewConnection();
        connection.Open();

        DataTable types = connection.GetSchema(DbMetaDataCollectionNames.DataTypes);
        Assert.Contains("TYPE_NAME", Names(types));
        Assert.Equal(4, Int(Row(types, "TYPE_NAME", "INTEGER"), "DATA_TYPE"));
        Assert.Equal("INTEGER", Row(types, "TYPE_NAME", "INTEGER")["TYPE_NAME"]);

        DataTable words = connection.GetSchema(DbMetaDataCollectionNames.ReservedWords);
        Assert.Contains("_ROWID_", Values(words, "ReservedWord"));

        DataTable tableTypes = connection.GetSchema("TableTypes");
        Assert.Equal(5, tableTypes.Rows.Count);
        Assert.Contains("BASE TABLE", Values(tableTypes, "TABLE_TYPE"));
    }

    [Fact]
    public void ProceduresAreAdvertisedButEmpty()
    {
        using H2Connection connection = NewConnection();
        connection.Open();

        DataTable procedures = connection.GetSchema("Procedures", [null, "PUBLIC", null]);
        Assert.Equal(
            ["PROCEDURE_CAT", "PROCEDURE_SCHEM", "PROCEDURE_NAME", "RESERVED1", "RESERVED2",
                "RESERVED3", "REMARKS", "PROCEDURE_TYPE", "SPECIFIC_NAME"],
            Names(procedures));
        Assert.Empty(procedures.Rows);
    }

    [Fact]
    public void UnknownCollectionsAndTooManyRestrictionsRefuse()
    {
        using H2Connection connection = NewConnection();
        connection.Open();

        Assert.Throws<ArgumentException>(() => connection.GetSchema("NoSuchCollection"));
        Assert.Throws<ArgumentException>(() => connection.GetSchema("Catalogs", ["one-too-many"]));
    }

    [Fact]
    public void AClosedConnectionRefuses()
    {
        using H2Connection connection = NewConnection();
        Assert.Throws<InvalidOperationException>(() => connection.GetSchema());
        Assert.Throws<InvalidOperationException>(() => connection.GetSchema("Tables"));
    }

    [Fact]
    public async Task AsyncSchemaMatchesTheSynchronousOneAndHonoursCancellation()
    {
        using H2Connection connection = NewConnection();
        connection.Open();
        Setup(connection);

        DataTable sync = connection.GetSchema("Tables", [null, "PUBLIC", null, null]);
        DataTable async = await connection.GetSchemaAsync("Tables", [null, "PUBLIC", null, null]);
        Assert.Equal(sync.Rows.Count, async.Rows.Count);
        Assert.Equal(Names(sync), Names(async));

        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => connection.GetSchemaAsync(cancelled.Token));
    }

    private static void Setup(H2Connection connection)
    {
        Execute(connection, "CREATE TABLE P(ID INT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, NAME VARCHAR(20) DEFAULT 'x', N INT GENERATED ALWAYS AS (ID + 1), AMT NUMERIC(10, 2), PLAIN NUMERIC, F FLOAT(10), R REAL, D DOUBLE)");
        Execute(connection, "CREATE TABLE C(ID INT PRIMARY KEY, PID INT, CONSTRAINT FK_C_P FOREIGN KEY(PID) REFERENCES P(ID))");
        Execute(connection, "CREATE INDEX IDX_C_PID ON C(PID)");
        Execute(connection, "CREATE VIEW V AS SELECT ID FROM P");
        Execute(connection, "COMMENT ON TABLE P IS 'parent'");
    }

    private static void Execute(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static DataRow Row(DataTable table, string column, string value)
    {
        foreach (DataRow row in table.Rows)
        {
            if (string.Equals(Convert.ToString(row[column], System.Globalization.CultureInfo.InvariantCulture),
                    value, StringComparison.Ordinal))
            {
                return row;
            }
        }

        throw new Xunit.Sdk.XunitException($"No row with {column} = {value}");
    }

    private static int Int(DataRow row, string column)
        => Convert.ToInt32(row[column], System.Globalization.CultureInfo.InvariantCulture);

    private static string[] Names(DataTable table)
    {
        string[] names = new string[table.Columns.Count];
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = table.Columns[i].ColumnName;
        }

        return names;
    }

    private static string[] Values(DataTable table, string column)
    {
        string?[] values = new string?[table.Rows.Count];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = Convert.ToString(table.Rows[i][column], System.Globalization.CultureInfo.InvariantCulture);
        }

        return Array.ConvertAll(values, static value => value ?? string.Empty);
    }

    private static H2Connection NewConnection()
        => new($"Data Source=getschema_{Guid.NewGuid():N};Storage=Memory");
}

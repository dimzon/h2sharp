// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// SCRIPT coverage: sequences, indexes, foreign keys, generated identity and
// LOB streaming in a single result script.
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class ScriptCoverageTests
{
    [Fact]
    public void ScriptSimpleDropColumnsStreamsSequencesAndLobs()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE SEQUENCE SEQ START WITH 5");
        _ = connection.Execute(
            "CREATE TABLE ITEM(" +
            "ID BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, " +
            "NAME VARCHAR(50), NOTE CLOB, DATA BLOB)");
        _ = connection.Execute(
            "INSERT INTO ITEM(NAME, NOTE, DATA) VALUES (" +
            "'a', REPEAT('x', 200), CAST(REPEAT('ab', 100) AS BLOB))");
        _ = connection.Execute("CREATE INDEX IDX_ITEM_NAME ON ITEM(NAME)");
        _ = connection.Execute(
            "CREATE TABLE CHILD(" +
            "ID INT PRIMARY KEY, ITEM INT, " +
            "CONSTRAINT FK_ITEM FOREIGN KEY(ITEM) REFERENCES ITEM(ID))");
        _ = connection.Execute("INSERT INTO CHILD VALUES (1, 1)");

        List<string> script = connection.Query<string>(
            "SCRIPT SIMPLE COLUMNS DROP BLOCKSIZE 16").AsList();
        string joined = string.Join("\n", script);

        Assert.Contains("CREATE SEQUENCE \"PUBLIC\".\"SEQ\"", joined);
        Assert.Contains("FOREIGN KEY", joined);
        Assert.Contains("SYSTEM_LOB_STREAM", joined);
        Assert.Contains("SYSTEM_COMBINE_CLOB", joined);
        Assert.Contains("SYSTEM_COMBINE_BLOB", joined);
    }
}

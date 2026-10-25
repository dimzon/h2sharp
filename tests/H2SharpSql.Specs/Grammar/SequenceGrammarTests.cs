// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/grammar.html — sequence and identity options, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Grammar;

public sealed class SequenceGrammarTests
{
    /// <summary>docs/grammar.html:3259 — { NEXT | { CURRENT } } VALUE FOR [schemaName.]sequenceName</summary>
    [Fact]
    public void SequenceValueExpression_NextAndCurrent_AreReadable() // GRM-3259
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE SEQUENCE SEQ_ID");

        Assert.Equal(1, connection.QuerySingle<int>("SELECT NEXT VALUE FOR SEQ_ID"));
        Assert.Equal(1, connection.QuerySingle<int>("SELECT CURRENT VALUE FOR SEQ_ID"));
    }

    /// <summary>docs/grammar.html:3277 — START WITH long</summary>
    [Fact]
    public void SequenceOption_StartWith_SetsTheFirstValue() // GRM-3277
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE SEQUENCE SEQ_ID START WITH 50");

        Assert.Equal(50, connection.QuerySingle<int>("SELECT NEXT VALUE FOR SEQ_ID"));
    }

    /// <summary>docs/grammar.html:3278 — | { RESTART WITH long }</summary>
    [Fact]
    public void SequenceOption_RestartWith_SetsTheCurrentValue() // GRM-3278
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE SEQUENCE SEQ_ID START WITH 100 RESTART WITH 10");

        Assert.Equal(10, connection.QuerySingle<int>("SELECT NEXT VALUE FOR SEQ_ID"));
    }

    /// <summary>docs/grammar.html:3279 — | basicSequenceOption</summary>
    [Fact]
    public void SequenceOption_BasicOption_IsApplied() // GRM-3279
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE SEQUENCE SEQ_ID INCREMENT BY 10");

        Assert.Equal(1, connection.QuerySingle<int>("SELECT NEXT VALUE FOR SEQ_ID"));
        Assert.Equal(11, connection.QuerySingle<int>("SELECT NEXT VALUE FOR SEQ_ID"));
    }

    /// <summary>docs/grammar.html:3299 — { START WITH long }</summary>
    [Fact]
    public void AlterSequenceOption_StartWith_IsAccepted() // GRM-3299
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE SEQUENCE SEQ_ID");

        int affected = connection.Execute("ALTER SEQUENCE SEQ_ID START WITH 100");

        Assert.Equal(0, affected);
    }

    /// <summary>docs/grammar.html:3300 — | RESTART [ WITH long ]</summary>
    [Fact]
    public void AlterSequenceOption_RestartWith_SetsTheCurrentValue() // GRM-3300
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE SEQUENCE SEQ_ID");
        _ = connection.QuerySingle<int>("SELECT NEXT VALUE FOR SEQ_ID");

        _ = connection.Execute("ALTER SEQUENCE SEQ_ID RESTART WITH 100");

        Assert.Equal(100, connection.QuerySingle<int>("SELECT NEXT VALUE FOR SEQ_ID"));
    }

    /// <summary>docs/grammar.html:3301 — | basicSequenceOption</summary>
    [Fact]
    public void AlterSequenceOption_BasicOption_IsApplied() // GRM-3301
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE SEQUENCE SEQ_ID");

        _ = connection.Execute("ALTER SEQUENCE SEQ_ID INCREMENT BY 5");

        Assert.Equal(1, connection.QuerySingle<int>("SELECT NEXT VALUE FOR SEQ_ID"));
        Assert.Equal(6, connection.QuerySingle<int>("SELECT NEXT VALUE FOR SEQ_ID"));
    }

    /// <summary>docs/grammar.html:3321 — { START WITH long }</summary>
    [Fact]
    public void AlterIdentityColumnOption_StartWith_IsAccepted() // GRM-3321
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenIdentityTable(connection);

        int affected = connection.Execute("ALTER TABLE TEST ALTER COLUMN ID START WITH 100");

        Assert.Equal(0, affected);
    }

    /// <summary>docs/grammar.html:3322 — | RESTART [ WITH long ]</summary>
    [Fact]
    public void AlterIdentityColumnOption_RestartWith_SetsTheNextValue() // GRM-3322
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenIdentityTable(connection);

        _ = connection.Execute("ALTER TABLE TEST ALTER COLUMN ID RESTART WITH 100");
        _ = connection.Execute("INSERT INTO TEST(NAME) VALUES ('Ada')");

        Assert.Equal(100, connection.QuerySingle<long>("SELECT ID FROM TEST WHERE NAME = 'Ada'"));
    }

    /// <summary>docs/grammar.html:3323 — | SET basicSequenceOption</summary>
    [Fact]
    public void AlterIdentityColumnOption_SetBasicOption_IsApplied() // GRM-3323
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenIdentityTable(connection);

        _ = connection.Execute("ALTER TABLE TEST ALTER COLUMN ID SET INCREMENT BY 5");
        _ = connection.Execute("INSERT INTO TEST(NAME) VALUES ('Ada')");
        _ = connection.Execute("INSERT INTO TEST(NAME) VALUES ('Alan')");

        Assert.Equal(6, connection.QuerySingle<long>("SELECT ID FROM TEST WHERE NAME = 'Alan'"));
    }

    /// <summary>docs/grammar.html:3343 — INCREMENT BY long</summary>
    [Fact]
    public void BasicSequenceOption_IncrementBy_IsApplied() // GRM-3343
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE SEQUENCE SEQ_ID INCREMENT BY 10");

        Assert.Equal(1, connection.QuerySingle<int>("SELECT NEXT VALUE FOR SEQ_ID"));
        Assert.Equal(11, connection.QuerySingle<int>("SELECT NEXT VALUE FOR SEQ_ID"));
    }

    /// <summary>docs/grammar.html:3344 — | MINVALUE long | NO MINVALUE | { NOMINVALUE }</summary>
    [Fact]
    public void BasicSequenceOption_MinValue_IsAccepted() // GRM-3344
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("CREATE SEQUENCE SEQ_ID MINVALUE 5 START WITH 5");
        _ = connection.Execute("CREATE SEQUENCE SEQ_ID2 NOMINVALUE");

        Assert.Equal(5, connection.QuerySingle<int>("SELECT NEXT VALUE FOR SEQ_ID"));
        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SEQUENCES WHERE SEQUENCE_NAME = 'SEQ_ID2'"));
    }

    /// <summary>docs/grammar.html:3345 — | MAXVALUE long | NO MAXVALUE | { NOMAXVALUE }</summary>
    [Fact]
    public void BasicSequenceOption_MaxValue_IsAccepted() // GRM-3345
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("CREATE SEQUENCE SEQ_ID MAXVALUE 100 START WITH 99");
        _ = connection.Execute("CREATE SEQUENCE SEQ_ID2 NOMAXVALUE");

        Assert.Equal(99, connection.QuerySingle<int>("SELECT NEXT VALUE FOR SEQ_ID"));
        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SEQUENCES WHERE SEQUENCE_NAME = 'SEQ_ID2'"));
    }

    /// <summary>docs/grammar.html:3346 — | CYCLE | NO CYCLE | { EXHAUSTED } | { NOCYCLE }</summary>
    [Fact]
    public void BasicSequenceOption_Cycle_WrapsAround() // GRM-3346
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE SEQUENCE SEQ_ID MAXVALUE 2 START WITH 1 CYCLE");
        _ = connection.Execute("CREATE SEQUENCE SEQ_ID2 NOCYCLE");

        Assert.Equal(1, connection.QuerySingle<int>("SELECT NEXT VALUE FOR SEQ_ID"));
        Assert.Equal(2, connection.QuerySingle<int>("SELECT NEXT VALUE FOR SEQ_ID"));
        Assert.Equal(1, connection.QuerySingle<int>("SELECT NEXT VALUE FOR SEQ_ID"));
    }

    /// <summary>docs/grammar.html:3347 — | { CACHE long } | { NO CACHE } | { NOCACHE }</summary>
    [Fact]
    public void BasicSequenceOption_Cache_IsAccepted() // GRM-3347
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE SEQUENCE SEQ_ID CACHE 10");
        _ = connection.Execute("CREATE SEQUENCE SEQ_ID2 NOCACHE");

        Assert.Equal(1, connection.QuerySingle<int>("SELECT NEXT VALUE FOR SEQ_ID"));
        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SEQUENCES WHERE SEQUENCE_NAME = 'SEQ_ID2'"));
    }

    private static void GivenIdentityTable(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID BIGINT GENERATED BY DEFAULT AS IDENTITY, NAME VARCHAR(50))");
    }
}

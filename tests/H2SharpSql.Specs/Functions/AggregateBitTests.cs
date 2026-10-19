// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/functions-aggregate.html — bit aggregates, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Functions;

public sealed class AggregateBitTests
{
    private static void GivenBitValues(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT)");
        _ = connection.Execute("INSERT INTO TEST(ID) VALUES (3), (5)");
    }

    /// <summary>docs/functions-aggregate.html:627 — ANY_VALUE( [ DISTINCT|ALL ] value )</summary>
    [Fact]
    public void AnyValue_ReturnsOneOfTheValues() // AGG-0627
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenBitValues(connection);

        Assert.True(connection.QuerySingle<bool>("SELECT ANY_VALUE(ID) BETWEEN 3 AND 5 FROM TEST"));
        Assert.True(connection.QuerySingle<bool>("SELECT ANY_VALUE(DISTINCT ID) BETWEEN 3 AND 5 FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:628 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void AnyValue_WithFilterAndOver() // AGG-0628
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenBitValues(connection);

        Assert.Equal(3, connection.QuerySingle<int>("SELECT ANY_VALUE(ID) FILTER (WHERE ID = 3) FROM TEST"));
        Assert.True(connection.QuerySingle<bool>(
            "SELECT DISTINCT ANY_VALUE(ID) OVER () BETWEEN 3 AND 5 FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:647 — {{BIT_AND_AGG}|{BIT_AND}}(expression)</summary>
    [Fact]
    public void BitAndAgg_AndsTheValues() // AGG-0647
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenBitValues(connection);

        Assert.Equal(1, connection.QuerySingle<int>("SELECT BIT_AND_AGG(ID) FROM TEST"));
        Assert.Equal(1, connection.QuerySingle<int>("SELECT BIT_AND(ID) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:648 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void BitAndAgg_WithFilterAndOver() // AGG-0648
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenBitValues(connection);

        Assert.Equal(3, connection.QuerySingle<int>("SELECT BIT_AND_AGG(ID) FILTER (WHERE ID = 3) FROM TEST"));
        Assert.Equal(1, connection.QuerySingle<int>("SELECT DISTINCT BIT_AND_AGG(ID) OVER () FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:667 — {{BIT_OR_AGG}|{BIT_OR}}(expression)</summary>
    [Fact]
    public void BitOrAgg_OrsTheValues() // AGG-0667
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenBitValues(connection);

        Assert.Equal(7, connection.QuerySingle<int>("SELECT BIT_OR_AGG(ID) FROM TEST"));
        Assert.Equal(7, connection.QuerySingle<int>("SELECT BIT_OR(ID) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:668 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void BitOrAgg_WithFilterAndOver() // AGG-0668
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenBitValues(connection);

        Assert.Equal(3, connection.QuerySingle<int>("SELECT BIT_OR_AGG(ID) FILTER (WHERE ID = 3) FROM TEST"));
        Assert.Equal(7, connection.QuerySingle<int>("SELECT DISTINCT BIT_OR_AGG(ID) OVER () FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:687 — BIT_XOR_AGG( [ DISTINCT|ALL ] expression)</summary>
    [Fact]
    public void BitXorAgg_XorsTheValues() // AGG-0687
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenBitValues(connection);

        Assert.Equal(6, connection.QuerySingle<int>("SELECT BIT_XOR_AGG(ID) FROM TEST"));
        Assert.Equal(6, connection.QuerySingle<int>("SELECT BIT_XOR_AGG(DISTINCT ID) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:688 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void BitXorAgg_WithFilterAndOver() // AGG-0688
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenBitValues(connection);

        Assert.Equal(3, connection.QuerySingle<int>("SELECT BIT_XOR_AGG(ID) FILTER (WHERE ID = 3) FROM TEST"));
        Assert.Equal(6, connection.QuerySingle<int>("SELECT DISTINCT BIT_XOR_AGG(ID) OVER () FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:707 — BIT_NAND_AGG(expression)</summary>
    [Fact]
    public void BitNandAgg_NegatesTheAnd() // AGG-0707
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenBitValues(connection);

        Assert.Equal(-2, connection.QuerySingle<int>("SELECT BIT_NAND_AGG(ID) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:708 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void BitNandAgg_WithFilterAndOver() // AGG-0708
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenBitValues(connection);

        Assert.Equal(-4, connection.QuerySingle<int>("SELECT BIT_NAND_AGG(ID) FILTER (WHERE ID = 3) FROM TEST"));
        Assert.Equal(-2, connection.QuerySingle<int>("SELECT DISTINCT BIT_NAND_AGG(ID) OVER () FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:727 — BIT_NOR_AGG(expression)</summary>
    [Fact]
    public void BitNorAgg_NegatesTheOr() // AGG-0727
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenBitValues(connection);

        Assert.Equal(-8, connection.QuerySingle<int>("SELECT BIT_NOR_AGG(ID) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:728 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void BitNorAgg_WithFilterAndOver() // AGG-0728
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenBitValues(connection);

        Assert.Equal(-4, connection.QuerySingle<int>("SELECT BIT_NOR_AGG(ID) FILTER (WHERE ID = 3) FROM TEST"));
        Assert.Equal(-8, connection.QuerySingle<int>("SELECT DISTINCT BIT_NOR_AGG(ID) OVER () FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:747 — BIT_XNOR_AGG( [ DISTINCT|ALL ] expression)</summary>
    [Fact]
    public void BitXnorAgg_NegatesTheXor() // AGG-0747
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenBitValues(connection);

        Assert.Equal(-7, connection.QuerySingle<int>("SELECT BIT_XNOR_AGG(ID) FROM TEST"));
        Assert.Equal(-7, connection.QuerySingle<int>("SELECT BIT_XNOR_AGG(DISTINCT ID) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:748 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void BitXnorAgg_WithFilterAndOver() // AGG-0748
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenBitValues(connection);

        Assert.Equal(-4, connection.QuerySingle<int>("SELECT BIT_XNOR_AGG(ID) FILTER (WHERE ID = 3) FROM TEST"));
        Assert.Equal(-7, connection.QuerySingle<int>("SELECT DISTINCT BIT_XNOR_AGG(ID) OVER () FROM TEST"));
    }
}

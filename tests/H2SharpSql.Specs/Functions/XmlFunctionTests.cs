// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/functions.html — XML functions, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Functions;

public sealed class XmlFunctionTests
{
    /// <summary>docs/functions.html:2563 — XMLATTR(nameString, valueString)</summary>
    [Fact]
    public void XmlAttr_BuildsAnAttribute() // FUN-2563
    {
        using IDbConnection connection = TestDatabase.Open();

        XmlFunctionAssert.Attribute(connection.QuerySingle<string>("SELECT XMLATTR('a', '1')"), "a", "1");
    }

    /// <summary>docs/functions.html:2581 — XMLNODE(elementString [, attributesString [, contentString [, indentBoolean]]])</summary>
    [Fact]
    public void XmlNode_BuildsAnElement() // FUN-2581
    {
        using IDbConnection connection = TestDatabase.Open();

        XmlFunctionAssert.Element(connection.QuerySingle<string>("SELECT XMLNODE('a')"), "a", "");
        XmlFunctionAssert.Element(connection.QuerySingle<string>("SELECT XMLNODE('a', NULL, 'x')"), "a", "x");
    }

    /// <summary>docs/functions.html:2599 — XMLCOMMENT(commentString)</summary>
    [Fact]
    public void XmlComment_BuildsAComment() // FUN-2599
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("<!--x-->", connection.QuerySingle<string>("SELECT XMLCOMMENT('x')"));
    }

    /// <summary>docs/functions.html:2617 — XMLCDATA(valueString)</summary>
    [Fact]
    public void XmlCdata_BuildsACdataSection() // FUN-2617
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("<![CDATA[x]]>", connection.QuerySingle<string>("SELECT XMLCDATA('x')"));
    }

    /// <summary>docs/functions.html:2635 — XMLSTARTDOC()</summary>
    [Fact]
    public void XmlStartDoc_ReturnsTheDeclaration() // FUN-2635
    {
        using IDbConnection connection = TestDatabase.Open();

        XmlFunctionAssert.Declaration(connection.QuerySingle<string>("SELECT XMLSTARTDOC()"));
    }

    /// <summary>docs/functions.html:2653 — XMLTEXT(valueString [, escapeNewlineBoolean])</summary>
    [Fact]
    public void XmlText_ReturnsTheText() // FUN-2653
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("x", connection.QuerySingle<string>("SELECT XMLTEXT('x')"));
        XmlFunctionAssert.EscapedText(connection.QuerySingle<string>("SELECT XMLTEXT('x' || CHAR(10) || 'y', TRUE)"), "x\ny");
    }
}

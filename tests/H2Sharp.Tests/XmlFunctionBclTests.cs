/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * SQL XML contracts: reference SQL layer plus approved D-012..D-018 BCL behavior.
 */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml;
using Dapper;
using H2Sharp.Api;
using H2Sharp.Data;
using H2Sharp.Message;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class XmlFunctionBclTests
{
    private static H2Connection Open()
    {
        H2Connection connection = new("Data Source=" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        return connection;
    }

    private static XmlDocument Parse(string fragment)
    {
        XmlDocument doc = new() { PreserveWhitespace = true, XmlResolver = null };
        using XmlReader reader = XmlReader.Create(new System.IO.StringReader(fragment), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = false });
        doc.Load(reader);
        return doc;
    }

    [Theory]
    [InlineData("SELECT XMLATTR('a','1')", " a=\"1\"")]
    [InlineData("SELECT XMLNODE('a')", "<a />")]
    [InlineData("SELECT XMLNODE('a',NULL,'x')", "<a>x</a>")]
    [InlineData("SELECT XMLCOMMENT('x')", "<!--x-->")]
    [InlineData("SELECT XMLCDATA('x')", "<![CDATA[x]]>")]
    [InlineData("SELECT XMLSTARTDOC()", "<?xml version=\"1.0\"?>")]
    [InlineData("SELECT XMLTEXT('x')", "x")]
    [InlineData("SELECT XMLTEXT('x' || CHAR(10) || 'y',TRUE)", "x&#xA;y")]
    public void ScalarFunctionsExposeVarcharThroughAdoAndDapper(string sql, string expected)
    {
        using H2Connection connection = Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using (DbDataReader reader = command.ExecuteReader())
        {
            Assert.Equal(typeof(string), reader.GetFieldType(0));
            Assert.True(reader.Read());
            Assert.Equal(expected, Assert.IsType<string>(reader.GetValue(0)));
            Assert.Equal(expected, reader.GetFieldValue<string>(0));
            Assert.False(reader.Read());
        }
        Assert.Equal(expected, connection.QuerySingle<string>(sql));
    }

    [Theory]
    [InlineData("SELECT XMLATTR(NULL,'1')")]
    [InlineData("SELECT XMLATTR('a',NULL)")]
    [InlineData("SELECT XMLNODE(NULL)")]
    [InlineData("SELECT XMLNODE('a',NULL,'x',NULL)")]
    [InlineData("SELECT XMLCDATA(NULL)")]
    [InlineData("SELECT XMLCOMMENT(NULL)")]
    [InlineData("SELECT XMLTEXT(NULL)")]
    [InlineData("SELECT XMLTEXT('x',NULL)")]
    public void NullPropagationRetainsVarcharMetadata(string sql)
    {
        using H2Connection connection = Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using DbDataReader reader = command.ExecuteReader();
        Assert.Equal(typeof(string), reader.GetFieldType(0));
        Assert.True(reader.Read());
        Assert.True(reader.IsDBNull(0));
        Assert.Same(DBNull.Value, reader.GetValue(0));
        Assert.False(reader.Read());
    }

    [Theory]
    [InlineData("SELECT XMLATTR()")]
    [InlineData("SELECT XMLATTR('a')")]
    [InlineData("SELECT XMLNODE()")]
    [InlineData("SELECT XMLNODE('a',NULL,NULL,FALSE,1)")]
    [InlineData("SELECT XMLCDATA()")]
    [InlineData("SELECT XMLCOMMENT('x','y')")]
    [InlineData("SELECT XMLSTARTDOC('x')")]
    [InlineData("SELECT XMLTEXT('x',TRUE,FALSE)")]
    public void ArityMatchesTheReference(string sql)
    {
        using H2Connection connection = Open();
        H2SharpException error = Assert.Throws<H2SharpException>(() => connection.QuerySingle<string>(sql));
        Assert.Equal(ErrorCode.InvalidParameterCount2, error.H2ErrorCode);
    }

    [Theory]
    [InlineData("SELECT XMLNODE('a b')")]
    [InlineData("SELECT XMLNODE('p:a:b')")]
    [InlineData("SELECT XMLNODE('a',' x=''1'' x=''2''')")]
    [InlineData("SELECT XMLNODE('a',NULL,'<b>')")]
    [InlineData("SELECT XMLNODE('a',NULL,'x & y')")]
    [InlineData("SELECT XMLNODE('a',NULL,'&missing;')")]
    [InlineData("SELECT XMLTEXT(CHAR(1))")]
    [InlineData("SELECT XMLCDATA(CHAR(1))")]
    [InlineData("SELECT XMLNODE('a',' xmlns:p=''urn:x'' xmlns:q=''urn:x'' p:v=''1'' q:v=''2''')")]
    public void MalformedXmlRefusesThroughTheSqlErrorSurface(string sql)
    {
        using H2Connection connection = Open();
        H2SharpException error = Assert.Throws<H2SharpException>(() => connection.QuerySingle<string>(sql));
        Assert.Equal(ErrorCode.InvalidValue2, error.H2ErrorCode);
        Assert.Contains("argument", error.Message);
    }

    [Fact]
    public void IndentPreserveCommentsAndCdataUseApprovedBclContracts()
    {
        using H2Connection connection = Open();
        Assert.Equal("<a><b /><c /></a>", connection.QuerySingle<string>("SELECT XMLNODE('a',NULL,'<b/><c/>')"));
        Assert.Equal("<a>\n  <b />\n  <c />\n</a>", connection.QuerySingle<string>("SELECT XMLNODE('a',NULL,'<b/><c/>',TRUE)"));
        string preserved = connection.QuerySingle<string>("SELECT XMLNODE('a',NULL,'<b xml:space=''preserve''><c/></b><d/>',TRUE)");
        Assert.DoesNotContain('\n', preserved);
        Assert.Equal("<a><b xml:space=\"preserve\"><c /></b><d /></a>", preserved);
        Assert.Equal("<!--x- -y-->", connection.QuerySingle<string>("SELECT XMLCOMMENT('x--y')"));
        string cdata = connection.QuerySingle<string>("SELECT XMLCDATA(' x]]>y]]>z ')");
        XmlElement root = Parse("<probe>" + cdata + "</probe>").DocumentElement!;
        Assert.Equal(3, root.ChildNodes.Count);
        Assert.All(root.ChildNodes.Cast<XmlNode>(), node => Assert.IsType<XmlCDataSection>(node));
        Assert.Equal(" x]]>y]]>z ", root.InnerText);
        string eol = connection.QuerySingle<string>("SELECT XMLCDATA('x' || CHAR(13) || CHAR(10) || 'y')");
        Assert.Equal("x\ny", Parse("<probe>" + eol + "</probe>").DocumentElement!.InnerText);
    }

    [Fact]
    public void DeferredNamespacesAndAttributeWhitespaceSurviveComposition()
    {
        using H2Connection connection = Open();
        string sql = "SELECT XMLNODE('r',XMLATTR('xmlns:p','urn:x'),XMLNODE('p:a',XMLATTR('p:v','x' || CHAR(9) || CHAR(13) || CHAR(10) || 'y')),FALSE)";
        XmlElement root = Parse(connection.QuerySingle<string>(sql)).DocumentElement!;
        XmlElement child = Assert.IsType<XmlElement>(root.FirstChild);
        Assert.Equal("a", child.LocalName);
        Assert.Equal("urn:x", child.NamespaceURI);
        Assert.Equal("x\t\r\ny", child.GetAttribute("v", "urn:x"));
        string declaration = connection.QuerySingle<string>("SELECT XMLSTARTDOC() || XMLNODE('a',NULL,XMLTEXT('ёж🎉'),FALSE)");
        XmlDocument document = Parse(declaration);
        XmlDeclaration header = Assert.IsType<XmlDeclaration>(document.FirstChild);
        Assert.Equal("1.0", header.Version);
        Assert.Equal("", header.Encoding);
        Assert.Equal("ёж🎉", document.DocumentElement!.InnerText);
    }

    [Fact]
    public void NonConstantColumnArgumentsUseTheSameEvaluationPath()
    {
        using H2Connection connection = Open();
        connection.Execute("CREATE TABLE T(N VARCHAR,V VARCHAR,C VARCHAR,I BOOLEAN)");
        connection.Execute("INSERT INTO T VALUES('a','1','<b/>',TRUE)");
        Assert.Equal("<a v=\"1\">\n  <b />\n</a>", connection.QuerySingle<string>("SELECT XMLNODE(N,XMLATTR('v',V),C,I) FROM T"));
        connection.Execute("UPDATE T SET I=NULL");
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT XMLNODE(N,XMLATTR('v',V),C,I) FROM T";
        using DbDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.True(reader.IsDBNull(0));
    }

    [JavaOracleFact]
    public void CommonStringResultsNullsAndArityMatchPinnedJarExactly()
    {
        string[] queries =
        [
            "SELECT XMLATTR('a','1')", "SELECT XMLATTR('n',42)", "SELECT XMLCDATA('x')",
            "SELECT XMLCDATA(42)", "SELECT XMLTEXT('x')", "SELECT XMLTEXT(42)",
            "SELECT XMLTEXT(NULL)", "SELECT XMLATTR(NULL,'1')", "SELECT XMLNODE(NULL)",
            "SELECT XMLNODE('a',NULL,'x',NULL)", "SELECT XMLTEXT('x',NULL)",
            "SELECT XMLATTR()", "SELECT XMLNODE()", "SELECT XMLSTARTDOC('x')", "SELECT XMLTEXT('x',TRUE,FALSE)",
        ];
        List<string> java = OracleRunner.Run(queries, """
            try (java.sql.Connection c = java.sql.DriverManager.getConnection("jdbc:h2:mem:xml_contract")) {
              java.sql.Statement stat = c.createStatement();
              for (String sql : inputs) {
                try (java.sql.ResultSet r = stat.executeQuery(sql)) {
                  r.next(); String value = r.getString(1);
                  System.out.println("=" + (value == null ? "NULL" : org.h2.util.StringUtils.convertBytesToHex(value.getBytes(java.nio.charset.StandardCharsets.UTF_16BE))));
                } catch (java.sql.SQLException e) { System.out.println("=ERR:" + e.getErrorCode()); }
              }
            }
            """);
        Assert.Equal(queries.Length, java.Count);
        using H2Connection connection = Open();
        List<string> port = [];
        foreach (string sql in queries)
        {
            try
            {
                using DbCommand command = connection.CreateCommand();
                command.CommandText = sql;
                object? value = command.ExecuteScalar();
                port.Add(value is null or DBNull ? "=NULL" : "=" + Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes((string)value)).ToLowerInvariant());
            }
            catch (H2SharpException error) { port.Add("=ERR:" + error.H2ErrorCode.ToString(CultureInfo.InvariantCulture)); }
        }
        Assert.Equal(java, port);
    }
}

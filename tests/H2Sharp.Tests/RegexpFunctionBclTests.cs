/* Copyright 2026 H2Sharp contributors. MPL 2.0 or EPL 1.0 (LICENSE-H2.txt).
 * D-001/D-019: pattern and replacement are passed to BCL unchanged.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Dapper;
using H2Sharp.Api;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class RegexpFunctionBclTests
{
    private static H2Connection Open(string mode = "Regular")
    {
        H2Connection connection = new(
            "Data Source=regex_" + Guid.NewGuid().ToString("N")
            + ";Storage=Memory;Mode=" + mode);
        connection.Open();
        return connection;
    }

    [Theory]
    [InlineData("SELECT REGEXP_REPLACE('Hello','l','L')", "HeLLo")]
    [InlineData("SELECT REGEXP_REPLACE('a','A','X','i')", "X")]
    [InlineData("SELECT REGEXP_SUBSTR('Hello','l+')", "ll")]
    [InlineData("SELECT REGEXP_SUBSTR('Hello','l',4)", "l")]
    [InlineData("SELECT REGEXP_SUBSTR('abc123def456','[0-9]+',1,2)", "456")]
    [InlineData("SELECT REGEXP_SUBSTR('abc123','([a-z]+)([0-9]+)',1,1,NULL,2)", "123")]
    [InlineData("SELECT REGEXP_SUBSTR('ab','(?<42>a)(b)',1,1,NULL,42)", "a")]
    [InlineData("SELECT REGEXP_REPLACE('ab','(a)','$1X')", "aXb")]
    [InlineData("SELECT REGEXP_REPLACE('ab','(a)','\\1')", "\\1b")]
    [InlineData("SELECT REGEXP_REPLACE('ab','(a)','$&X')", "aXb")]
    [InlineData("SELECT REGEXP_REPLACE('ab','(a)','$$1')", "$1b")]
    public void NativeBclStringsAndGroupsArePreserved(string sql, string expected)
    {
        using H2Connection connection = Open();
        Assert.Equal(expected, connection.QuerySingle<string>(sql));
    }

    [Theory]
    [InlineData("SELECT REGEXP_LIKE('Hello','ell')", true)]
    [InlineData("SELECT REGEXP_LIKE('abc','ABC','ic')", false)]
    [InlineData("SELECT REGEXP_LIKE('abc','ABC','ci')", true)]
    [InlineData("SELECT REGEXP_LIKE('x' || CHAR(10) || 'y','x.y','n')", true)]
    [InlineData("SELECT REGEXP_LIKE('x' || CHAR(10) || 'y','^y','m')", true)]
    [InlineData("SELECT REGEXP_LIKE('oocc','^(?''open''o)+(?''-open''c)+(?(open)(?!))$')", true)]
    [InlineData("SELECT REGEXP_LIKE('ooc','^(?''open''o)+(?''-open''c)+(?(open)(?!))$')", false)]
    [InlineData("SELECT REGEXP_LIKE('bc','^[a-z-[aeiou]]+$')", true)]
    [InlineData("SELECT REGEXP_LIKE('ba','^[a-z-[aeiou]]+$')", false)]
    public void FlagsAndDotNetSpecificPatternsAreNotRewritten(string sql, bool expected)
    {
        using H2Connection connection = Open();
        Assert.Equal(expected, connection.QuerySingle<bool>(sql));
    }

    [Theory]
    [InlineData("SELECT REGEXP_LIKE(NULL,'x')")]
    [InlineData("SELECT REGEXP_LIKE('x','x',NULL)")]
    [InlineData("SELECT REGEXP_REPLACE('x','x',NULL)")]
    [InlineData("SELECT REGEXP_SUBSTR('x','y')")]
    [InlineData("SELECT REGEXP_SUBSTR('x','x',0)")]
    [InlineData("SELECT REGEXP_SUBSTR('x','x',3)")]
    [InlineData("SELECT REGEXP_SUBSTR('x','x',1,1,NULL,9)")]
    [InlineData("SELECT REGEXP_SUBSTR('x','(a)?x',1,1,NULL,1)")]
    public void NullAndAbsentGroupResultsFollowTheSqlContract(string sql)
    {
        using H2Connection connection = Open();
        Assert.Null(connection.QuerySingle<string?>(sql));
    }

    [Theory]
    [InlineData("SELECT REGEXP_LIKE('x','[')", 22025)]
    [InlineData("SELECT REGEXP_LIKE('x','\\Qx\\E')", 22025)]
    [InlineData("SELECT REGEXP_REPLACE('x','[','y')", 22025)]
    [InlineData("SELECT REGEXP_LIKE('x','x','g')", 90008)]
    [InlineData("SELECT REGEXP_SUBSTR('x','x',1,1,'g')", 90008)]
    [InlineData("SELECT REGEXP_LIKE('x')", 7001)]
    [InlineData("SELECT REGEXP_REPLACE('x','x','y',1,1)", 7001)]
    public void ErrorsUseTheReferenceSqlCodes(string sql, int expected)
    {
        using H2Connection connection = Open();
        H2SharpException error = Assert.Throws<H2SharpException>(() => connection.QuerySingle<object>(sql));
        Assert.Equal(expected, error.H2ErrorCode);
    }

    [Fact]
    public void ModesDoNotTranslateReplacementAndPostgresRetainsGlobalRules()
    {
        foreach (string mode in new[] { "Regular", "MariaDB", "MySQL", "Oracle", "PostgreSQL" })
        {
            using H2Connection connection = Open(mode);
            Assert.Equal("\\1b", connection.QuerySingle<string>(@"SELECT REGEXP_REPLACE('ab','(a)','\1')"));
            Assert.Equal("aXb", connection.QuerySingle<string>("SELECT REGEXP_REPLACE('ab','(a)','$1X')"));
        }
        using H2Connection postgres = Open("PostgreSQL");
        Assert.Equal("Xaa", postgres.QuerySingle<string>("SELECT REGEXP_REPLACE('aaa','a','X')"));
        Assert.Equal("XXX", postgres.QuerySingle<string>("SELECT REGEXP_REPLACE('aaa','a','X','g')"));
    }

    [Fact]
    public void OraclePositionAndOccurrenceUseNativeBclSearch()
    {
        using H2Connection connection = Open("Oracle");
        Assert.Equal("abXbX", connection.QuerySingle<string>("SELECT REGEXP_REPLACE('ababa','a','X',3,0)"));
        Assert.Equal("abXba", connection.QuerySingle<string>("SELECT REGEXP_REPLACE('ababa','a','X',3,1)"));
        Assert.Equal("ababa", connection.QuerySingle<string>("SELECT REGEXP_REPLACE('ababa','^a','X',3,1)"));
        Assert.Equal("ababa", connection.QuerySingle<string>("SELECT REGEXP_REPLACE('ababa','a','X',3,9)"));
        Assert.Equal("ac", connection.QuerySingle<string>("SELECT REGEXP_REPLACE('abc','b',NULL)"));
        Assert.Equal("abc", connection.QuerySingle<string>("SELECT REGEXP_REPLACE('abc',NULL,NULL)"));
    }

    private static List<string> Reference(string[] sql, string mode) => OracleRunner.Run([mode, .. sql], """
        try (java.sql.Connection c = java.sql.DriverManager.getConnection("jdbc:h2:mem:regex_contract;MODE=" + inputs[0])) {
          java.sql.Statement stat = c.createStatement();
          for (int i=1; i<inputs.length; i++) {
            try (java.sql.ResultSet r = stat.executeQuery(inputs[i])) {
              r.next(); String value = r.getString(1);
              System.out.println(value == null ? "=NULL" : hex(value));
            } catch (java.sql.SQLException e) { System.out.println("=ERR:" + e.getErrorCode()); }
          }
        }
        """);

    private static List<string> Port(string[] sql, string mode)
    {
        using H2Connection connection = Open(mode);
        List<string> result = [];
        foreach (string query in sql)
        {
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = query;
                using var reader = command.ExecuteReader();
                Assert.True(reader.Read());
                result.Add(reader.IsDBNull(0) ? "=NULL" : OracleRunner.Hex(reader.GetString(0)));
            }
            catch (H2SharpException e) { result.Add("=ERR:" + e.H2ErrorCode.ToString(CultureInfo.InvariantCulture)); }
        }
        return result;
    }

    [JavaOracleFact]
    public void OriginalSqlAndSharedFlagsNullsGroupsAndModesMatchJar()
    {
        string[] common =
        [
            "SELECT REGEXP_REPLACE('Hello', 'l', 'L')",
            "SELECT REGEXP_LIKE('Hello', '.*ell.*')", "SELECT REGEXP_LIKE('abc', 'ABC', 'i')",
            "SELECT REGEXP_SUBSTR('Hello', 'l+')", "SELECT REGEXP_SUBSTR('Hello', 'l', 4)",
            "SELECT REGEXP_SUBSTR('abc123def456','[0-9]+',1,2)",
            "SELECT REGEXP_SUBSTR('abc123','([a-z]+)([0-9]+)',1,1,NULL,2)",
            "SELECT REGEXP_SUBSTR('x','x',1,1,NULL,9)",
            "SELECT REGEXP_SUBSTR('x','x',0)", "SELECT REGEXP_SUBSTR('x','x',1,0)",
            "SELECT REGEXP_LIKE('abc','ABC','ic')", "SELECT REGEXP_LIKE('abc','ABC','ci')",
            "SELECT REGEXP_LIKE('x' || CHAR(10) || 'y','x.y','n')",
            "SELECT REGEXP_LIKE('x' || CHAR(10) || 'y','^y','m')",
            "SELECT REGEXP_LIKE(NULL,'x')", "SELECT REGEXP_LIKE('x','x',NULL)",
            "SELECT REGEXP_LIKE('x','[')", "SELECT REGEXP_LIKE('x','x','g')",
            "SELECT REGEXP_REPLACE('x','[','y')", "SELECT REGEXP_LIKE('x')",
        ];
        foreach (string mode in new[] { "Regular", "Oracle", "PostgreSQL", "MySQL", "MariaDB" })
        {
            List<string> java = Reference(common, mode);
            Assert.Equal(common.Length, java.Count);
            Assert.Equal(java, Port(common, mode));
        }
        string[] regular = ["SELECT REGEXP_REPLACE('a', 'A', 'X', 'i')"];
        Assert.Equal(Reference(regular, "Regular"), Port(regular, "Regular"));
        string[] postgres = ["SELECT REGEXP_REPLACE('aaa','a','X','g')", "SELECT REGEXP_REPLACE('aaa','a','X')"];
        Assert.Equal(Reference(postgres, "PostgreSQL"), Port(postgres, "PostgreSQL"));
    }

    [JavaOracleFact]
    public void OrdinaryAndEscapedSqlLiteralsCarryTheSameBackslash()
    {
        string[] sql = [@"SELECT '\1'", @"SELECT E'\\1'"];
        string[] expected = [OracleRunner.Hex(@"\1"), OracleRunner.Hex(@"\1")];
        Assert.Equal(expected, Reference(sql, "Regular"));
        Assert.Equal(expected, Port(sql, "Regular"));
    }

    [JavaOracleFact]
    public void PlainReplacementThenBackslashLocalizesSqlDecodingAndReplacementDialect()
    {
        string[] sql = ["SELECT REGEXP_REPLACE('ab','(a)','Z')",
            @"SELECT REGEXP_REPLACE('ab','(a)','\1')", @"SELECT REGEXP_REPLACE('ab','(a)',E'\\1')"];
        foreach (string mode in new[] { "Regular", "Oracle", "PostgreSQL" })
        {
            List<string> java = Reference(sql, mode);
            List<string> port = Port(sql, mode);
            Assert.Equal(3, java.Count);
            Assert.Equal(3, port.Count);
            // First isolate the operation without any backslash in pattern or replacement.
            Assert.Equal(OracleRunner.Hex("Zb"), java[0]);
            Assert.Equal(java[0], port[0]);
            // Ordinary and E-literals decode identically; the remaining difference is D-019.
            string referenceBackslash = mode == "Regular" ? "1b" : "ab";
            Assert.Equal(OracleRunner.Hex(referenceBackslash), java[1]);
            Assert.Equal(java[1], java[2]);
            Assert.Equal(OracleRunner.Hex(@"\1b"), port[1]);
            Assert.Equal(port[1], port[2]);
        }
    }

    [JavaOracleFact]
    public void AcceptedReplacementAndRegionDifferencesArePinnedExplicitly()
    {
        string[] unmatched = ["SELECT REGEXP_SUBSTR('x','(a)?x',1,1,NULL,1)"];
        Assert.Equal(["=ERR:50000"], Reference(unmatched, "Regular"));
        Assert.Equal(["=NULL"], Port(unmatched, "Regular"));
        string[] replacement = ["SELECT REGEXP_REPLACE('ab','(a)','$1')"];
        Assert.Equal([OracleRunner.Hex("$1b")], Reference(replacement, "PostgreSQL"));
        Assert.Equal([OracleRunner.Hex("ab")], Port(replacement, "PostgreSQL"));
        string[] position = ["SELECT REGEXP_REPLACE('ababa','a','X',3,0)", "SELECT REGEXP_REPLACE('ababa','^a','X',3,1)"];
        Assert.Equal([OracleRunner.Hex("XbXbX"), OracleRunner.Hex("abXba")], Reference(position, "Oracle"));
        Assert.Equal([OracleRunner.Hex("abXbX"), OracleRunner.Hex("ababa")], Port(position, "Oracle"));
    }
}

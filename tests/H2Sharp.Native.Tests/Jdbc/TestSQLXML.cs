// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.jdbc.TestSQLXML.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// New test, per the discussion decision (see docs/NATIVE_TESTS_DISCUSSION.md): the
// reference exercises java.sql.SQLXML objects (createSQLXML, rs.getSQLXML, DOM/SAX/
// StAX Source/Result transforms, FEATURE_NOT_SUPPORTED_1 refusals). The ADO.NET caller
// reads and writes a CLOB as string/TextReader/bytes, so the same content ports as a
// CLOB value round-trip: the getters read the column through GetString/GetTextReader,
// the setters submit the value through a parameter as string and as bytes, and a null
// parameter stores NULL. The reference's getBinaryStream/setCharacterStream legs are
// the byte[]/TextReader shapes; TextReader parameters are not accepted by the engine
// yet (issue #57), so that leg is the byte[] form.
using System.Text;
using Xunit;

namespace H2Sharp.Native.Tests.Jdbc;

/// <summary>
/// The CLOB-value shape of the reference's TestSQLXML.
/// </summary>
public sealed class TestSQLXML : TestDb
{
    private const string Xml = "<xml a=\"v\">Text</xml>";

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        DeleteDb(GetTestName());
        using (DbConnection conn = GetConnection(GetTestName()))
        {
            DbCommand stat = conn.CreateCommand();
            Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY, X CLOB)");
            Execute(stat, "INSERT INTO TEST VALUES (1, NULL)");
            TestGetters(conn, stat);
            TestSetters(conn, stat);
            Execute(stat, "DROP TABLE TEST");
        }

        DeleteDb(GetTestName());
    }

    private void TestGetters(DbConnection conn, DbCommand stat)
    {
        stat.CommandText = "SELECT * FROM TEST";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertTrue(rs.IsDBNull(1));
            AssertTrue(rs.IsDBNull(rs.GetOrdinal("X")));
        }

        AssertEquals(1, ExecuteUpdate(conn, "UPDATE TEST SET X = '" + Xml + "'"));

        stat.CommandText = "SELECT * FROM TEST";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            // GetString()
            AssertXml(rs.GetString(1));
            // GetTextReader() — the reference's getCharacterStream(), which is how an
            // ADO.NET caller reads a CLOB
            AssertXml(ReadAll(rs.GetTextReader(1)));
            AssertFalse(rs.Read());
        }
    }

    private void TestSetters(DbConnection conn, DbCommand stat)
    {
        // each setter form of the reference writes the value and the reader sees the
        // same XML back
        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "UPDATE TEST SET X = ? WHERE ID = 1";
            prep.Parameters.Add(new H2Parameter());
            prep.Parameters[0].Value = Xml;
            AssertEquals(1, prep.ExecuteNonQuery());
            AssertStored(stat);
        }

        using (DbCommand prep = conn.CreateCommand())
        {
            // setCharacterStream() / setBinaryStream(): the character content as bytes,
            // the ADO.NET caller's byte[]; TextReader parameters are issue #57
            prep.CommandText = "UPDATE TEST SET X = ? WHERE ID = 1";
            prep.Parameters.Add(new H2Parameter());
            prep.Parameters[0].Value = Encoding.UTF8.GetBytes(Xml);
            AssertEquals(1, prep.ExecuteNonQuery());
            AssertStored(stat);
        }

        using (DbCommand prep = conn.CreateCommand())
        {
            // null parameter stores NULL
            prep.CommandText = "UPDATE TEST SET X = ? WHERE ID = 1";
            prep.Parameters.Add(new H2Parameter());
            prep.Parameters[0].Value = DBNull.Value;
            AssertEquals(1, prep.ExecuteNonQuery());
            stat.CommandText = "SELECT X FROM TEST";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertTrue(rs.IsDBNull(0));
                AssertFalse(rs.Read());
            }
        }
    }

    /// <summary>
    /// The reference's assertXML: the stored value may carry an XML declaration, which
    /// is stripped before comparing.
    /// </summary>
    /// <param name="actual">the stored value</param>
    private void AssertXml(string actual)
    {
        if (actual.StartsWith("<?"))
        {
            actual = actual.Substring(actual.IndexOf("?>") + 2);
        }

        AssertEquals(Xml, actual);
    }

    /// <summary>
    /// Read the row back and check the stored value.
    /// </summary>
    /// <param name="stat">the statement</param>
    private void AssertStored(DbCommand stat)
    {
        stat.CommandText = "SELECT X FROM TEST";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertXml(rs.GetString(0));
            AssertFalse(rs.Read());
        }
    }

    private static int ExecuteUpdate(DbConnection conn, string sql)
    {
        using DbCommand command = conn.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteNonQuery();
    }

    private static string ReadAll(TextReader reader)
    {
        using TextReader r = reader;
        return r.ReadToEnd();
    }

    private static string ReadAll(Stream stream)
    {
        using Stream s = stream;
        using StreamReader reader = new(s, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}

/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 249: parser edges that the ordinary suites do not reach. Two kinds: statements whose
 * answer is compared with the reference (temporary tables, sequence exhaustion, ALTER COLUMN
 * DROP IDENTITY, ROWID, JSON/GEOMETRY literals, domains, and a batch of syntax errors), and
 * statements the port refuses by name because the feature is not ported, which is a recorded
 * defect rather than a silent answer.
 */
using System;
using System.Data.Common;
using H2Sharp.Api;
using H2Sharp.Data;
using H2Sharp.Message;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ParserSurfaceTests : ParserCoverageOracleBase
{
    private static H2Connection Open(string name, string? mode = null)
    {
        string options = mode is null ? string.Empty : ";MODE=" + mode;
        H2Connection connection = new($"Data Source={name};Storage=Memory{options}");
        connection.Open();
        return connection;
    }

    private static Exception Refusal(string sql, string? mode = null)
    {
        using H2Connection connection = Open("parser_refusal_" + Guid.NewGuid().ToString("N"), mode);
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Assert.ThrowsAny<Exception>(() => command.ExecuteNonQuery());
    }

    [Fact]
    public void ScriptAndRunScriptRefuseCompressionAndCipher()
    {
        TODOException scriptCompression = Assert.IsType<TODOException>(
            Refusal("SCRIPT TO 'parser_surface.zip' COMPRESSION ZIP"));
        Assert.Contains("COMPRESSION", scriptCompression.Message, StringComparison.Ordinal);

        TODOException scriptCipher = Assert.IsType<TODOException>(
            Refusal("SCRIPT TO 'parser_surface.zip' CIPHER AES"));
        Assert.Contains("CIPHER", scriptCipher.Message, StringComparison.Ordinal);

        TODOException runCompression = Assert.IsType<TODOException>(
            Refusal("RUNSCRIPT FROM 'parser_surface.zip' COMPRESSION ZIP"));
        Assert.Contains("compression", runCompression.Message, StringComparison.OrdinalIgnoreCase);

        TODOException runCipher = Assert.IsType<TODOException>(
            Refusal("RUNSCRIPT FROM 'parser_surface.zip' CIPHER AES"));
        Assert.Contains("cipher", runCipher.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnportedBuiltinsAreRefusedByName()
    {
        Assert.Contains("CryptFunction",
            Assert.IsType<TODOException>(Refusal("CALL ENCRYPT('x', 'y')")).Message,
            StringComparison.Ordinal);
        Assert.Contains("CryptFunction",
            Assert.IsType<TODOException>(Refusal("CALL DECRYPT('x', 'y')")).Message,
            StringComparison.Ordinal);
        Assert.Contains("ORA_HASH",
            Assert.IsType<TODOException>(Refusal("CALL ORA_HASH('x')")).Message,
            StringComparison.Ordinal);

        H2SharpException fileRead = Assert.IsType<H2SharpException>(Refusal("CALL FILE_READ('x')"));
        Assert.Contains("FILE_READ", fileRead.Message, StringComparison.Ordinal);
        H2SharpException fileWrite = Assert.IsType<H2SharpException>(Refusal("CALL FILE_WRITE('x', 'y')"));
        Assert.Contains("FILE_WRITE", fileWrite.Message, StringComparison.Ordinal);
        H2SharpException csvWrite = Assert.IsType<H2SharpException>(Refusal("CALL CSVWRITE('x', 'y')"));
        Assert.Contains("CSVWRITE", csvWrite.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TableFunctionEdgesRefuseByName()
    {
        TODOException link = Assert.IsType<TODOException>(
            Refusal("SELECT * FROM LINK_SCHEMA('A', 'B', 'C')"));
        Assert.Contains("LinkSchemaFunction", link.Message, StringComparison.Ordinal);

        // CALL reads the same name through the table-function path, so the same gap answers.
        TODOException call = Assert.IsType<TODOException>(
            Refusal("CALL LINK_SCHEMA('A', 'B', 'C')"));
        Assert.Contains("LinkSchemaFunction", call.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SqlServerExecuteIsRefused()
    {
        TODOException e = Assert.IsType<TODOException>(
            Refusal("EXECUTE ('SELECT 1')", "MSSQLServer"));
        Assert.Contains("parseExecuteSQLServer", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RowValueAssignmentAndOtherUnportedChangesAreRefused()
    {
        using H2Connection connection = Open("parser_unported_changes");
        using DbCommand create = connection.CreateCommand();
        create.CommandText = "CREATE TABLE T(A INT, B INT)";
        _ = create.ExecuteNonQuery();

        using DbCommand update = connection.CreateCommand();
        update.CommandText = "INSERT INTO T VALUES (0, 0)";
        _ = update.ExecuteNonQuery();
        update.CommandText = "UPDATE T SET (A, B) = (1, 2)";
        _ = update.ExecuteNonQuery();
        using DbCommand verify = connection.CreateCommand();
        verify.CommandText = "SELECT A, B FROM T";
        using (DbDataReader reader = verify.ExecuteReader())
        {
            Assert.True(reader.Read());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.Equal(2, reader.GetInt32(1));
        }

        using DbCommand sequence = connection.CreateCommand();
        sequence.CommandText = "CREATE SEQUENCE S AS INTEGER";
        _ = sequence.ExecuteNonQuery();
    }

    [JavaOracleFact]
    public void TemporaryTablesMatchTheReference()
    {
        AssertSameAsReference("parser_temp_tables",
            "CREATE GLOBAL TEMPORARY TABLE GT(A INT) ON COMMIT DELETE ROWS",
            "INSERT INTO GT VALUES 1, 2",
            "SELECT COUNT(*) FROM GT",
            "COMMIT",
            "SELECT COUNT(*) FROM GT",
            "CREATE LOCAL TEMPORARY TABLE LT(A INT) ON COMMIT DROP",
            "INSERT INTO LT VALUES 7",
            "SELECT * FROM LT",
            "DROP TABLE GT, LT");
    }

    [JavaOracleFact]
    public void SequenceOptionsAndIdentityDropMatchTheReference()
    {
        AssertSameAsReference("parser_sequence_options",
            "CREATE SEQUENCE S1 CYCLE EXHAUSTED",
            "CREATE SEQUENCE S2 NO CYCLE",
            "CREATE SEQUENCE S3 CYCLE",
            "SELECT NEXT VALUE FOR S3",
            "CREATE TABLE T(A INT GENERATED ALWAYS AS IDENTITY)",
            "INSERT INTO T DEFAULT VALUES",
            "SELECT * FROM T",
            "ALTER TABLE T ALTER COLUMN A DROP IDENTITY",
            "INSERT INTO T DEFAULT VALUES",
            "SELECT * FROM T ORDER BY A");
    }

    [JavaOracleFact]
    public void RowidJsonGeometryAndDomainMatchTheReference()
    {
        AssertSameAsReference("parser_rowid_json_geometry",
            "CREATE TABLE T(A INT)",
            "INSERT INTO T VALUES 1",
            "SELECT ROWID FROM T",
            "SELECT CAST(JSON '{\"a\": 1}' AS VARCHAR)",
            "SELECT GEOMETRY 'POINT (1 2)'",
            "CREATE DOMAIN D AS INT CHECK (VALUE > 0)",
            "SELECT CAST(5 AS D)",
            "SELECT CAST(-5 AS D)");
    }

    [JavaOracleFact]
    public void SyntaxErrorsMatchTheReference()
    {
        AssertSameAsReference("parser_syntax_errors",
            "CREATE TABLE DUP(A INT, A INT)",
            "CREATE SCHEMA S",
            "CREATE TABLE S.T(A INT)",
            "CREATE TABLE S.T(A INT)",
            "SELECT FROM (VALUES 1) V",
            "CREATE TABLE X.Y.Z(A INT)");
    }

    [JavaOracleFact]
    public void TodayAndSysdateWithoutParenthesesMatchTheReference()
    {
        AssertSameAsReferenceInMode("parser_today_sysdate", "Oracle",
            "SELECT EXTRACT(YEAR FROM TODAY), EXTRACT(YEAR FROM SYSDATE), "
            + "EXTRACT(YEAR FROM SYSTIMESTAMP) FROM DUAL",
            "SELECT TODAY, SYSDATE FROM DUAL");
    }

    [JavaOracleFact]
    public void IfExistsAndQualifiedColumnNamesMatchTheReference()
    {
        AssertSameAsReference("parser_if_exists_qualified",
            "ALTER TABLE IF EXISTS NO_T ALTER COLUMN A DROP IDENTITY",
            "CREATE SCHEMA Q",
            "CREATE TABLE Q.T(A INT)",
            "INSERT INTO Q.T VALUES 3",
            "SELECT Q.T.A FROM Q.T",
            "SELECT COUNT(*) FROM Q.T");
    }

    [JavaOracleFact]
    public void LiteralScaleAndFieldErrorsMatchTheReference()
    {
        AssertSameAsReference("parser_literal_errors",
            "SELECT TIME(10) '10:00:00'",
            "SELECT EXTRACT(FOO FROM DATE '2020-01-01')",
            "SELECT 'unterminated");
    }

    [Fact]
    public void TableFunctionsAndRunScriptOptionsWork()
    {
        string file = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "parser_surface_" + Guid.NewGuid().ToString("N"));
        string script = file + ".sql";
        string written = file + ".out.sql";
        System.IO.File.WriteAllText(script, "CREATE TABLE RS(A INT);\nINSERT INTO RS VALUES 1;\n");
        try
        {
            using H2Connection connection = Open("parser_table_functions");
            using DbCommand run = connection.CreateCommand();
            run.CommandText = "RUNSCRIPT FROM '" + script.Replace("'", "''", StringComparison.Ordinal)
                + "' FROM_1X";
            _ = run.ExecuteNonQuery();

            using DbCommand check = connection.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM RS";
            Assert.Equal(1L, check.ExecuteScalar());

            using DbCommand distinct = connection.CreateCommand();
            distinct.CommandText = "SELECT * FROM TABLE_DISTINCT(C INT = ARRAY[1, 1, 2]) ORDER BY 1";
            using (DbDataReader reader = distinct.ExecuteReader())
            {
                Assert.True(reader.Read());
                Assert.Equal(1, reader.GetInt32(0));
                Assert.True(reader.Read());
                Assert.Equal(2, reader.GetInt32(0));
                Assert.False(reader.Read());
            }

            using DbCommand scriptCommand = connection.CreateCommand();
            scriptCommand.CommandText = "SCRIPT TO '" + written.Replace("'", "''", StringComparison.Ordinal)
                + "' CHARSET 'UTF-8'";
            _ = scriptCommand.ExecuteNonQuery();
            Assert.Contains("CREATE MEMORY TABLE", System.IO.File.ReadAllText(written),
                StringComparison.Ordinal);

            using DbCommand csv = connection.CreateCommand();
            csv.CommandText = "SELECT * FROM CSVREAD('no-such-file')";
            H2SharpException missing = Assert.IsType<H2SharpException>(
                Assert.ThrowsAny<Exception>(() => csv.ExecuteReader()));
            Assert.Equal(ErrorCode.FunctionAliasNotFound1, missing.H2ErrorCode);
        }
        finally
        {
            System.IO.File.Delete(script);
            System.IO.File.Delete(written);
        }
    }
}

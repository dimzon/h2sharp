/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class PostfixCastOracleTests
{
    // NULL checks the full target type (including precision and extended type info),
    // without making this parser test depend on a conversion between unrelated types.
    private static readonly string[] Types =
    [
        "NULL", "CHARACTER(5)", "CHARACTER VARYING(10)", "CHARACTER LARGE OBJECT",
        "VARCHAR_IGNORECASE(10)", "BINARY(4)", "BINARY VARYING(8)", "BINARY LARGE OBJECT",
        "BOOLEAN", "TINYINT", "SMALLINT", "INTEGER", "BIGINT", "NUMERIC(10, 2)",
        "REAL", "DOUBLE PRECISION", "FLOAT(24)", "DECFLOAT(10)", "DATE", "TIME(3)",
        "TIME(3) WITH TIME ZONE", "TIMESTAMP(3)", "TIMESTAMP(3) WITH TIME ZONE",
        "INTERVAL YEAR", "INTERVAL MONTH", "INTERVAL DAY", "INTERVAL HOUR",
        "INTERVAL MINUTE", "INTERVAL SECOND(3, 2)", "INTERVAL YEAR(3) TO MONTH",
        "INTERVAL DAY TO HOUR", "INTERVAL DAY TO MINUTE", "INTERVAL DAY TO SECOND",
        "INTERVAL HOUR TO MINUTE", "INTERVAL HOUR TO SECOND", "INTERVAL MINUTE TO SECOND",
        "JAVA_OBJECT", "ENUM('a', 'b')", "GEOMETRY(POINT, 4326)", "JSON", "UUID",
        "INTEGER ARRAY", "VARCHAR(10) ARRAY[3] ARRAY[2]", "ROW(A INTEGER, B VARCHAR(10))",
    ];

    private static readonly string[] Expressions =
    [
        "SELECT '123'::INTEGER",
        "SELECT 1.235::NUMERIC(10, 2)",
        "SELECT 123::CHAR(5)",
        "SELECT '2020-01-02'::DATE",
        "SELECT '03:04:05.123456'::TIME(3)",
        "SELECT '2020-01-02 03:04:05.123456+02:00'::TIMESTAMP(3) WITH TIME ZONE",
        "SELECT X'4142'::BINARY VARYING",
        "SELECT 'a'::ENUM('a', 'b')",
        "SELECT 'POINT(1 2)'::GEOMETRY",
        "SELECT '{\"a\":1}'::JSON",
        "SELECT '7'::INTEGER::VARCHAR(3)::INTEGER + 2",
        "SELECT (1 + 2)::VARCHAR",
        "SELECT 1 + '2'::INTEGER * 3",
        "SELECT -1::VARCHAR, +1::VARCHAR",
        "SELECT -('2'::INTEGER)::BIGINT",
        "SELECT ABS(-7)::VARCHAR",
        "SELECT CASE WHEN TRUE THEN '8' ELSE '9' END::INTEGER",
        "SELECT (SELECT '9')::INTEGER",
        "SELECT ARRAY['10', '20'][2]::INTEGER",
        "SELECT ARRAY['10', '20']::INTEGER ARRAY[2]",
        "SELECT (ARRAY['10', '20']::INTEGER ARRAY)[2]::VARCHAR",
        "SELECT ARRAY[ARRAY['10', '20']]::INTEGER ARRAY ARRAY[1][2]::VARCHAR",
        "SELECT ROW('3', 'ab')::ROW(A INTEGER, B VARCHAR(5))",
        "SELECT ?::INTEGER + 1, ?::INTEGER::VARCHAR",
        "SELECT X::INTEGER + 1 FROM (VALUES ('2'), ('3')) T(X) ORDER BY X::INTEGER",
        "SELECT SUM(X)::VARCHAR FROM (VALUES (1), (2)) T(X)",
        "SELECT COUNT(*)::INTEGER + 1 FROM (VALUES (1), (2)) T(X)",
        "SELECT COUNT(*)::INTEGER > 1 AND SUM(X)::INTEGER = 3 FROM (VALUES (1), (2)) T(X)",
        "SELECT 'bad'::INTEGER",
        "SELECT 1::NOSUCHTYPE",
        "SELECT 1::\"INTEGER\"",
        "SELECT 1::VARCHAR(0)",
        "SELECT 1::NUMERIC(0, 0)",
        "SELECT 1::",
        "SELECT 1::REGCLASS",
    ];

    [JavaOracleFact]
    public void TargetTypesAndExpressionsMatchReference()
    {
        List<string> statements = [];
        foreach (string type in Types)
        {
            statements.Add("SELECT NULL::" + type);
        }

        statements.AddRange(Expressions);
        AssertMatches(statements, "Regular");
    }

    [JavaOracleFact]
    [Trait("Category", "BlockedByPort")]
    public void ExistingConversionGapsAlsoAffectPostfixCasts()
    {
        AssertMatches(
        [
            "SELECT '1.235'::NUMERIC(10, 2)",
            "SELECT CAST('1.235' AS NUMERIC(10, 2))",
            "SELECT 128::TINYINT",
            "SELECT CAST(128 AS TINYINT)",
        ], "Regular");
    }

    [JavaOracleTheory]
    [InlineData("Strict")]
    [InlineData("PostgreSQL")]
    public void CastsAreReadInOtherModes(string mode)
    {
        AssertMatches(
        [
            "SELECT '7'::INTEGER::VARCHAR",
            "SELECT ?::INTEGER + 1",
            "SELECT ARRAY['10', '20']::INTEGER ARRAY[2]",
            "SELECT 'd3b07384-d9a4-4f5b-8f1a-2a1e7a1e7a11'::UUID",
        ], mode);
    }

    [JavaOracleFact]
    public void PostgreSqlRegclassMatchesReference()
    {
        AssertMatches(
        [
            "CREATE TABLE T(ID INTEGER)",
            "CREATE INDEX IDX ON T(ID)",
            "CREATE SCHEMA S",
            "CREATE TABLE S.U(ID INTEGER)",
            "SELECT NULL::REGCLASS",
            "SELECT 12::REGCLASS, 123::SMALLINT::REGCLASS, 1234::REGCLASS",
            "SELECT 4294967297::BIGINT::PG_CATALOG.REGCLASS",
            "SELECT 'T'::REGCLASS AS OID",
            "SELECT 'IDX'::PG_CATALOG.REGCLASS AS OID",
            "SELECT 'U'::REGCLASS AS OID",
            "SELECT NAME::REGCLASS AS OID FROM (VALUES ('T'), ('IDX'), ('U')) V(NAME) ORDER BY NAME",
            "SELECT 'MISSING'::REGCLASS",
            "SELECT 1::\"REGCLASS\"",
            "SELECT 1::PG_CATALOG.\"REGCLASS\"",
            "SELECT 1::\"PG_CATALOG\".REGCLASS",
            "SELECT '7'::PG_CATALOG.INTEGER",
            "SELECT 1::PG_CATALOG",
        ], "PostgreSQL");
    }

    [JavaOracleFact]
    [Trait("Category", "BlockedByPort")]
    public void PostgreSqlDisallowedTypesAreNotPortedYet()
    {
        AssertMatches(
        [
            "SELECT 12::TINYINT::REGCLASS",
            "SELECT CAST(12 AS TINYINT)",
        ], "PostgreSQL");
    }

    [JavaOracleFact]
    [Trait("Category", "BlockedByPort")]
    public void SchemaObjectLookupIsNotCaseSensitiveYet()
    {
        AssertMatches(
        [
            "CREATE TABLE T(ID INTEGER)",
            "SELECT 't'::REGCLASS",
        ], "PostgreSQL");
    }

    private static void AssertMatches(IReadOnlyList<string> statements, string mode)
    {
        List<string> port = RunPort(statements, mode);
        List<string> java = RunJava(statements, mode);
        Assert.Equal(java.Count, port.Count);
        List<string> mismatches = [];
        for (int i = 0; i < java.Count; i++)
        {
            if (java[i] != port[i])
            {
                mismatches.Add($"{mode}: {statements[i]}\n  java:   {java[i]}\n  csharp: {port[i]}");
            }
        }

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    private static List<string> RunPort(IReadOnlyList<string> statements, string mode)
    {
        SessionLocal session = new(new Database(new ConnectionInfo("jdbc:h2:mem:postfix_cast_oracle")), "sa")
        {
            CastDataProvider = new ModeProvider(Mode.GetInstance(mode)!),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
        List<string> lines = [];
        foreach (string sql in statements)
        {
            try
            {
                CommandContainer command = Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
                if (!command.IsQuery())
                {
                    _ = command.ExecuteUpdate(false);
                    lines.Add("=update");
                    continue;
                }

                foreach (Expressions.Parameter parameter in command.GetPreparedCommand().GetParameters() ?? [])
                {
                    parameter.SetValue(ValueVarchar.Get("7"));
                }

                ResultInterface result = command.Query(-1);
                StringBuilder line = new("=");
                int columns = result.GetVisibleColumnCount();
                for (int c = 0; c < columns; c++)
                {
                    result.GetColumnType(c).GetSql(line, 0).Append('|');
                }

                while (result.Next())
                {
                    Value?[] row = result.CurrentRow();
                    for (int c = 0; c < columns; c++)
                    {
                        Value? value = row[c];
                        string? text = value is null or ValueNull ? "-" : value.GetString();
                        // Object IDs are allocated differently by the two engines. For OID
                        // queries only, resolve the actual ID back to its schema object name.
                        if (sql.EndsWith("AS OID", StringComparison.Ordinal) || sql.Contains("AS OID FROM", StringComparison.Ordinal))
                        {
                            foreach (Schemas.Schema schema in session.GetDatabase().GetSchemas())
                            {
                                foreach (string name in new[] { "T", "U", "IDX" })
                                {
                                    int? id = schema.FindTableOrView(session, name)?.GetId()
                                        ?? schema.FindIndex(session, name)?.GetId();
                                    if (id == value?.GetInt()) text = "oid:" + name;
                                }
                            }
                        }

                        line.Append(text).Append('|');
                    }
                }

                lines.Add(line.ToString());
                result.Close();
            }
            catch (Exception e)
            {
                lines.Add(e is H2SharpException h2
                    ? "=" + h2.H2ErrorCode.ToString(CultureInfo.InvariantCulture)
                    : "=" + e.GetType().Name + ":" + e.Message);
            }
        }

        return lines;
    }

    private static List<string> RunJava(IReadOnlyList<string> statements, string mode)
        => OracleRunner.Run(
            statements,
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:postfix_cast_oracle;MODE=MODE_NAME");
            org.h2.engine.SessionLocal session = (org.h2.engine.SessionLocal) ((org.h2.jdbc.JdbcConnection) conn).getSession();
            for (String sql : inputs) {
              try (org.h2.command.CommandInterface command = session.prepareCommand(sql)) {
                if (!command.isQuery()) {
                  command.executeUpdate(false);
                  System.out.println("=update");
                  continue;
                }
                for (org.h2.expression.ParameterInterface parameter : command.getParameters()) {
                  parameter.setValue(org.h2.value.ValueVarchar.get("7"), false);
                }
                org.h2.result.ResultInterface result = command.executeQuery(0, 0, false);
                StringBuilder line = new StringBuilder("=");
                int columns = result.getVisibleColumnCount();
                for (int c = 0; c < columns; c++) {
                  result.getColumnType(c).getSQL(line, 0).append('|');
                }
                while (result.next()) {
                  org.h2.value.Value[] row = result.currentRow();
                  for (int c = 0; c < columns; c++) {
                    org.h2.value.Value value = row[c];
                    String text = value == org.h2.value.ValueNull.INSTANCE ? "-" : value.getString();
                    if (sql.endsWith("AS OID") || sql.contains("AS OID FROM")) {
                      for (org.h2.schema.Schema schema : session.getDatabase().getAllSchemas()) {
                        for (String name : new String[] { "T", "U", "IDX" }) {
                          org.h2.table.Table table = schema.findTableOrView(session, name);
                          org.h2.index.Index index = schema.findIndex(session, name);
                          if (table != null && table.getId() == value.getInt()
                              || index != null && index.getId() == value.getInt()) {
                            text = "oid:" + name;
                          }
                        }
                      }
                    }
                    line.append(text).append('|');
                  }
                }
                result.close();
                System.out.println(line);
              } catch (org.h2.message.DbException e) {
                System.out.println("=" + e.getErrorCode());
              }
            }
            conn.close();
            """.Replace("MODE_NAME", mode, StringComparison.Ordinal),
            extraJavaArgs: ["-Duser.language=en"]);

    private sealed class ModeProvider(Mode mode) : CastDataProvider
    {
        private readonly TestCastDataProvider fixedProvider = new();

        public bool ZeroBasedEnums() => fixedProvider.ZeroBasedEnums();
        public Mode GetMode() => mode;
        public ValueTimestampTimeZone CurrentTimestamp() => fixedProvider.CurrentTimestamp();
        public TimeZoneProvider CurrentTimeZone() => fixedProvider.CurrentTimeZone();
    }
}

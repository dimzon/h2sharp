/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for the tokenizer.
 *
 * The tokenizer is where a statement stops being text and becomes something a program can walk, so
 * the corpus is not a list of valid statements: it is built out of the places where the same
 * characters mean two things, and out of the places where a lexical mistake is a *different*
 * statement rather than an error.
 *
 * What is compared for every statement is every token's kind, where it began, the name it carries,
 * whether it is quoted, and how it prints. The last of those is the value for a literal, so a
 * NUMERIC and a DECFLOAT are compared by their digits. Each statement is read three times, once
 * with names as written, once folded to upper case and once to lower, because a name that is a
 * keyword in one setting and a name in another is the whole point of folding, and a tokenizer that
 * folds in the wrong place will pass a corpus that only uses one setting.
 *
 * The corpus also runs the statements that must be refused, and the refusal is compared down to the
 * error code and the whole message, because the message is where the place in the statement is.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class TokenizerOracleTests
{
    private static readonly string[] Statements =
    [
        // The words, in every spelling, because folding is what decides them.
        "SELECT * FROM TEST",
        "select * from test",
        "SeLeCt * FrOm TeSt",
        "SELECT DISTINCT A, B FROM T ORDER BY A",
        "SELECT ALL A FROM T",
        "SELECT * FROM T WHERE A = 1 AND B <> 2 OR NOT C",
        "SELECT * FROM T GROUP BY A HAVING COUNT(*) > 1",
        "SELECT * FROM T1 LEFT JOIN T2 ON T1.A = T2.A",
        "SELECT * FROM T1 INNER JOIN T2 USING (A)",
        "SELECT * FROM T1 FULL OUTER JOIN T2 ON TRUE",
        "SELECT * FROM T1 CROSS JOIN T2",
        "SELECT * FROM T NATURAL JOIN U",
        "INSERT INTO T VALUES (1)",
        "INSERT INTO T VALUES (1), (2), (3)",
        "UPDATE T SET A = 1 WHERE B = 2",
        "DELETE FROM T WHERE A IS NULL",
        "CREATE TABLE T (A INT PRIMARY KEY, B VARCHAR(10))",
        "CREATE TABLE T (A INT CONSTRAINT PK_T PRIMARY KEY)",
        "CREATE TABLE T (A INT CHECK (A > 0))",
        "CREATE TABLE T AS SELECT * FROM U",
        "CREATE INDEX I ON T (A)",
        "CREATE UNIQUE INDEX I ON T (A)",
        "CREATE VIEW V AS SELECT 1",
        "DROP TABLE T",
        "DROP TABLE IF EXISTS T",
        "ALTER TABLE T ADD COLUMN B INT",
        "SELECT CASE WHEN A = 1 THEN 'one' ELSE 'other' END FROM T",
        "SELECT CAST(A AS VARCHAR(10)) FROM T",
        "SELECT A BETWEEN 1 AND 2 FROM T",
        "SELECT A IN (1, 2, 3) FROM T",
        "SELECT A LIKE 'x%' FROM T",
        "SELECT A FROM T UNION SELECT B FROM U",
        "SELECT A FROM T EXCEPT SELECT B FROM U",
        "SELECT A FROM T INTERSECT SELECT B FROM U",
        "SELECT A FROM T MINUS SELECT B FROM U",
        "SELECT * FROM T LIMIT 10",
        "SELECT * FROM T OFFSET 10 ROWS",
        "SELECT * FROM T FETCH FIRST 10 ROWS ONLY",
        "SELECT CURRENT_DATE, CURRENT_TIME, CURRENT_TIMESTAMP FROM T",
        "SELECT CURRENT_USER, CURRENT_SCHEMA, CURRENT_CATALOG, CURRENT_PATH, CURRENT_ROLE FROM T",
        "SELECT CURRENT_SCHEMA, CURRENT_CATALOG, CURRENT_TIMESTAMP FROM T",
        "SELECT LOCALTIME, LOCALTIMESTAMP FROM T",
        "SELECT SESSION_USER, SYSTEM_USER, USER FROM T",
        "SELECT DAY, HOUR, MINUTE, MONTH, SECOND, YEAR FROM T",
        "SELECT * FROM T FOR UPDATE",
        "SELECT * FROM T RIGHT JOIN U ON T.A = U.A",
        "SELECT * FROM T WHERE ROWNUM < 10",
        "SELECT ANY, SOME FROM T",
        "SELECT * FROM T ASYMMETRIC",
        "GRANT SELECT ON T TO CURRENT_USER",
        "GRANT SELECT ON T TO OTHER",
        "CREATE TABLE T (A INT DEFAULT 1)",
        "CREATE TABLE T (A INT, FOREIGN KEY (A) REFERENCES U (A))",
        "SELECT * FROM T UNION ALL SELECT * FROM U",
        "SELECT 1 FROM T SYMMETRIC",
        "SELECT CAST(1 AS INT) TO FROM T",
        "SELECT * FROM WINDOW W",
        "GRANT SELECT ON T TO CURRENT_USER AUTHORIZATION CURRENT_USER",
        "SELECT ARRAY[1, 2], ROW(1, 2) FROM T",
        "SELECT INTERVAL '1' DAY FROM T",
        "SELECT * FROM T QUALIFY ROW_NUMBER() OVER (PARTITION BY A) = 1",
        "WITH X AS (SELECT 1) SELECT * FROM X",
        "SELECT * FROM T WHERE A = TRUE AND B = FALSE AND C = UNKNOWN",
        "SELECT * FROM T WHERE A = NULL",
        "VALUES (1), (2)",
        "SELECT VALUE FROM T",

        // Numbers, which is where the tokenizer does arithmetic rather than comparing words.
        "SELECT 1",
        "SELECT 0",
        "SELECT 42 FROM T",
        "SELECT 2147483647",
        "SELECT 2147483648",
        "SELECT 9223372036854775807",
        "SELECT 9223372036854775808",
        "SELECT 123456789012345678901234567890",
        "SELECT 1.5",
        "SELECT .5",
        "SELECT 0.5",
        "SELECT 1.",
        "SELECT 1.50",
        "SELECT 1_000_000",
        "SELECT 1_000.000_1",
        "SELECT 1e10",
        "SELECT 1E10",
        "SELECT 1e+10",
        "SELECT 1e-10",
        "SELECT 1.5e3",
        "SELECT 1_0e1_0",
        "SELECT 0x1A",
        "SELECT 0X1a",
        "SELECT 0b1010",
        "SELECT 0B1010",
        "SELECT 0o17",
        "SELECT 0O17",
        "SELECT 07",
        "SELECT 42L",
        "SELECT 2147483648L",
        "SELECT 1_2L",
        "SELECT 0x100000000",
        "SELECT 0x100000000L",
        "SELECT 0b100000000000000000000000000000000",
        "SELECT 0o20000000000",
        "SELECT 0xFFFFFFFFFFFFFFFFFFFF",
        "SELECT 0Xabcdef1234567890ABCDEF",
        "SELECT 0b1010_1010_1010_1010_1010_1010_1010_1010_1",

        // Strings, where the quoting rules and the escapes are the whole story.
        "SELECT 'text'",
        "SELECT ''",
        "SELECT 'it''s'",
        "SELECT 'a' 'b'",
        "SELECT 'a'   'b'",
        "SELECT 'a' /* c */ 'b'",
        "SELECT N'text'",
        "SELECT X'414243'",
        "SELECT X'41 42'",
        "SELECT X'4142' '4344'",
        "SELECT $$dollar quoted$$",
        "SELECT $tag$dollar quoted$tag$",
        "SELECT 'multi",
        "SELECT 'multi",
        "line'",
        "SELECT U&'\\0041'",
        "SELECT U&'\\0041' UESCAPE '\\'",
        "SELECT U&\"\\0041\"",
        "SELECT U&'#0041' UESCAPE '#'",
        "SELECT U&'x0041' UESCAPE 'x'",

        // Names, and the quoting of them.
        "SELECT \"Quoted\" FROM \"Table\"",
        "SELECT `Quoted` FROM `Table`",
        "SELECT \"a\"\"b\" FROM T",
        "SELECT _ROWID_ FROM T",
        "SELECT _rowid_ FROM T",
        "SELECT _ROWID FROM T",
        "SELECT customers FROM customers",
        "SELECT Customers FROM CUSTOMERS",
        "SELECT x FROM T WHERE x = 1",
        "SELECT AB, Ab, aB, ab FROM T",
        "SELECT 日本語, ÿ, Ω FROM T",
        "SELECT a_1, _a, a$ FROM T",
        "SELECT A#B FROM T",

        // Operators and punctuation, each of which may be two characters.
        "SELECT A + B - C * D / E % F",
        "SELECT A || B",
        "SELECT A = B, A < B, A > B, A <= B, A >= B, A <> B, A != B, A ~ B, A !~ B",
        "SELECT A::B",
        "SELECT A := B",
        "SELECT (1), (1, 2), ((1))",
        "SELECT A[1], A[1:2], A[1][2]",
        "SELECT {fn now()}",
        "SELECT A && B",
        "SELECT @x",
        "SELECT A;",
        "SELECT * FROM T;",

        // Parameters, which is the one place where a question mark is not a question mark.
        "SELECT ?",
        "SELECT ?, ?",
        "SELECT ?1, ?2",
        "SELECT ?3, ?1",
        "SELECT $1",
        "SELECT $99",
        "SELECT $",
        "SELECT (?1)",
        "SELECT ??(",
        "SELECT ??)",

        // Comments and whitespace, including the ones .NET and Java disagree about.
        "SELECT 1 -- comment",
        "SELECT 1 -- comment\n, 2",
        "SELECT 1 /* comment */ , 2",
        "SELECT 1 /* nested /* comment */ still */ , 2",
        "SELECT 1 /* unterminated",
        "/* leading */ SELECT 1",
        "SELECT\t1\n,\r2",
        "SELECT 1 ",

        // What must be refused. The refusal is compared too.
        "SELECT !",
        "SELECT !x",
        "SELECT &",
        "SELECT |",
        "SELECT 'unterminated",
        "SELECT \"unterminated",
        "SELECT `unterminated",
        "SELECT 1.2.3",
        "SELECT 1_",
        "SELECT 1__0",
        "SELECT 0x",
        "SELECT 0b",
        "SELECT 0xZZ",
        "SELECT 0xFFFFFFFFFFFFFFFFFFL",
        "SELECT 1e",
        "SELECT 1e+",
        "SELECT 1_0e",
        "SELECT 12L34",
        "SELECT 1L2",
        "SELECT #",
        "SELECT 1 # 2",
        "SELECT ?0",
        "SELECT \\",
        "SELECT ümlaut",
        "SELECT ?1, ?",
        "SELECT X'4'",
        "SELECT X'ZZ'",
    ];

    [JavaOracleFact]
    public void TheTokensMatchTheReference()
    {
        (List<string> java, List<string> csharp) = Read();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// The corpus is not a sample: it has to reach every reader the class has, or the run says
    /// nothing about the readers it does not reach.
    /// </summary>
    [Fact]
    public void TheCorpusReachesEveryReader()
    {
        HashSet<int> kinds = [];
        TestProvider provider = new();
        foreach (string statement in Statements)
        {
            Tokenizer tokenizer = new(provider, false, false, null);
            try
            {
                foreach (Token token in tokenizer.Tokenize(statement, false, new BitSet()))
                {
                    kinds.Add(token.TokenType);
                }
            }
            catch (H2SharpException)
            {
                // A statement the tokenizer refuses is one the corpus meant to refuse; it tells
                // nothing about which kinds of token it would have produced.
            }
        }

        // Every keyword the tokenizer can produce, and nothing else. A kind missing here is a kind
        // no statement of the corpus produced, which is a hole in the corpus rather than a fact
        // about the tokenizer.
        //
        // UESCAPE is the exception and cannot be otherwise: the tokenizer takes the word and the
        // string after it out of the token list when it has read the escape character out of them,
        // so a statement with an escape in it never hands back a UESCAPE token. That is the
        // reference's doing and the oracle confirms it, but it means the kind is unreachable
        // here and the only way to see it is in a statement whose escape is refused, which is in
        // the corpus and is compared.
        List<string> missing = [];
        for (int type = 0; type < Token.Tokens.Length; type++)
        {
            if (Token.Tokens[type] is not null
                && !kinds.Contains(type)
                && type != ParserUtil.Uescape)
            {
                missing.Add(Token.Tokens[type]!);
            }
        }

        Assert.True(missing.Count == 0, "no statement produced: " + string.Join(", ", missing));
    }

    /// <summary>
    /// Reads the corpus on both sides and returns what each said, line by line.
    /// </summary>
    private static (List<string> Java, List<string> CSharp) Read()
    {
        if (JavaOracle.ReferenceRoot is null || JavaOracle.H2Jar is null)
        {
            throw new InvalidOperationException("reference H2 is not available");
        }

        string oracle = Path.Combine(JavaOracle.ReferenceRoot, "tools", "TokenOracle.java");
        string statements = Path.Combine(
            Path.GetTempPath(),
            "h2sharp-token-statements-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllLines(statements, Statements.Select(Escape));

        try
        {
            List<string> java = JavaOracle.OutputLines(
                JavaOracle.RunWithInputs(
                    File.ReadAllText(oracle),
                    "org.h2.command.TokenOracle",
                    [],
                    [JavaOracle.H2Jar],
                    extraJavaArgs: ["-Duser.language=en", "-Dstdout.encoding=UTF-8", "-Dfile.encoding=UTF-8"],
                    numbers: null,
                    programArgs: [statements]));

            List<string> csharp = [];
            foreach (string statement in Statements)
            {
                foreach ((bool upper, bool lower) in new[] { (false, false), (true, false), (false, true) })
                {
                    csharp.AddRange(Read(statement, upper, lower));
                }
            }

            return (java, csharp);
        }
        finally
        {
            File.Delete(statements);
        }
    }

    /// <summary>
    /// What this port makes of one statement, in the same shape the oracle prints.
    /// </summary>
    private static List<string> Read(string sql, bool identifiersToUpper, bool identifiersToLower)
    {
        List<string> lines = [];
        TestProvider provider = new();
        Tokenizer tokenizer = new(provider, identifiersToUpper, identifiersToLower, null);
        BitSet parameters = new();
        try
        {
            List<Token> tokens = tokenizer.Tokenize(sql, false, parameters);
            int count = 0;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters.Get(i))
                {
                    count++;
                }
            }

            lines.Add(Escape("OK "
                + count.ToString(CultureInfo.InvariantCulture)
                + "\n"
                + string.Join("\n", tokens.Select(Describe))));
        }
        catch (H2SharpException e)
        {
            lines.Add(Escape($"ERR {e.H2ErrorCode.ToString(CultureInfo.InvariantCulture)} "
                + TraceSuffix.Replace(e.OriginalMessage, string.Empty)));
        }

        return lines;
    }

    private static string Describe(Token token)
        => string.Join(
            "\t",
            token.TokenType.ToString(CultureInfo.InvariantCulture),
            token.Start.ToString(CultureInfo.InvariantCulture),
            token.AsIdentifier ?? "-",
            token.IsQuoted ? "true" : "false",
            token is Token.EndOfInputToken ? "-" : Escape(token.ToString() ?? string.Empty));

    /// <summary>
    /// Writes a statement so that a line of it cannot look like the end of it.
    /// </summary>
    private static string Escape(string s)
    {
        StringBuilder b = new(s.Length);
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\':
                    _ = b.Append("\\\\");
                    break;
                case '\t':
                    _ = b.Append("\\t");
                    break;
                case '\r':
                    _ = b.Append("\\r");
                    break;
                case '\n':
                    _ = b.Append("\\n");
                    break;
                default:
                    _ = b.Append(c);
                    break;
            }
        }

        return b.ToString();
    }

    private static readonly System.Text.RegularExpressions.Regex TraceSuffix =
        new(@"\s\[\d+-\d+\]", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// The tokenizer only asks its provider for the mode, so a provider that answers with the
    /// regular mode is a complete one.
    /// </summary>
    private static readonly TestCastDataProvider Provider = new();

    private sealed class TestProvider : CastDataProvider
    {
        public bool ZeroBasedEnums() => true;

        public Mode GetMode() => Mode.GetRegular();

        public ValueTimestampTimeZone CurrentTimestamp() => Provider.CurrentTimestamp();

        public TimeZoneProvider CurrentTimeZone() => Provider.CurrentTimeZone();
    }
}

// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.scripts.TestScript.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): the reference drives java.sql.Statement, PreparedStatement and
// ResultSet; the port's provider is ADO.NET, so they are DbCommand and DbDataReader. A
// statement's "is a query" answer is the reader's RecordsAffected (-1 for a result set,
// the update count otherwise), and the parameters a PreparedStatement fills by position
// are DbParameter values filled by position. setAutoCommit is the explicit transaction
// of ADO.NET: off begins one, on commits it.
//
// Modified (port gaps, named where they stand):
//  - The reference reads the prepared Query of a statement through reflection
//    (COMMAND/PREPARED fields) to cross-check what the script declares against what the
//    engine meant (a query without ORDER BY cannot promise an ordered result). The
//    port's H2Command builds its containers per execution and keeps none, so there is
//    nothing to read: the cross-check stays off, as it does in the reference's own
//    file-based run, where the statement goes through a plain Statement. When the port
//    keeps the last prepared command reachable, the check comes back here.
//  - containsTempTables asks DatabaseMetaData.getTables, and the reconnect-often branch
//    asks the session's mode and schema; the provider exposes none of those yet. The
//    branch is unreachable in the default configuration (reconnectOften needs
//    config.big), and it refuses loudly when reached.
//  - The LineNumberReader's line number is counted here, and the output file is written
//    UTF-8, because PrintStream's platform charset has no .NET counterpart.
//  - BlockedByPort: all user/role/password statements and their expected output were
//    removed from the local testScript.sql copy because D-004 has no users or roles.
//    UNIQUE, CHECK, TOP, explicit DUAL, qualified GROUP BY names, REFERENCES ... INDEX,
//    row-value UPDATE assignments, EXPLAIN, DELETE LIMIT, DIRECT/SORTED inserts and views
//    (CREATE/OR REPLACE/DROP/ALTER, dependencies, cascade) now run, as do IDENTITY shorthand,
//    built-in type name conflicts in CREATE DOMAIN and multiple qualified stars. Plan text at
//    372 still differs in scanCount; the first functional mismatch is @variables at line 541,
//    then SCRIPT NODATA and the remaining compatibility DDL.
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Text;
using H2Sharp;
using H2Sharp.Data;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Native.Tests.Scripts;

/// <summary>
/// This test runs a SQL script file and compares the output with the expected
/// output.
/// </summary>
public sealed class TestScript : TestDb
{
    private const string BaseDir = "H2Sharp.Native.Tests.Scripts.";

    private static readonly bool FixOutput = false;

    /// <summary>
    /// Set by the reference's own main() to cross-check the ordering the engine meant
    /// against what the script declares. The seam it needs (the prepared command of a
    /// statement) is not there yet; see the class header.
    /// </summary>
    private static bool CheckOrdering { get; set; }

    /// <summary>
    /// If set to true, the test will exit at the first failure.
    /// </summary>
    private bool FailFast { get; set; }

    /// <summary>
    /// If set to a value the test will add all executed statements to this list.
    /// </summary>
    private List<string>? statements;

    private bool reconnectOften;

    private DbConnection conn = null!;

    private DbCommand stat = null!;

    /// <summary>
    /// The transaction @autocommit off opened. ADO.NET has no autocommit flag: off is an
    /// open transaction, on is none.
    /// </summary>
    private DbTransaction? transaction;

    private string fileName = "";

    /// <summary>
    /// The reference's `in` — a keyword in C#, kept by the @ spelling.
    /// </summary>
    private StreamReader? @in;

    private int lineNumber;

    /// <summary>
    /// The reference's `out` — a keyword in C#, kept by the @ spelling.
    /// </summary>
    private StreamWriter? @out;

    private readonly List<string?[]> result = [];

    private readonly Queue<string> putBack = new();

    private bool foundErrors;

    private readonly JavaRandom random = new(1);

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    [Trait("Category", "BlockedByPort")]
    public override void Test()
    {
        reconnectOften = !Config.Memory && Config.Big;

        TestOneScript("testScript.sql");
        if (!Config.Memory && !Config.Big && !Config.Networked)
        {
            TestOneScript("testSimple.sql");
        }

        TestOneScript("dual.sql");
        TestOneScript("indexes.sql");
        TestOneScript("information_schema.sql");
        TestOneScript("range_table.sql");
        TestOneScript("altertable-index-reuse.sql");
        TestOneScript("altertable-fk.sql");
        TestOneScript("default-and-on_update.sql");

        foreach (string s in new[] { "add_months", "compatibility", "group_by",
            "strict_and_legacy" })
        {
            TestOneScript("compatibility/" + s + ".sql");
        }

        foreach (string s in new[] { "array", "bigint", "binary", "blob",
            "boolean", "char", "clob", "date", "decfloat", "double_precision", "enum",
            "geometry", "identity", "int", "interval", "java_object", "json", "numeric", "real", "row", "smallint",
            "time-with-time-zone", "time", "timestamp-with-time-zone", "timestamp", "tinyint",
            "uuid", "varbinary", "varchar", "varchar-ignorecase" })
        {
            TestOneScript("datatypes/" + s + ".sql");
        }

        foreach (string s in new[] { "alterDomain", "alterTableAdd", "alterTableAlterColumn", "alterTableDropColumn",
            "alterTableDropConstraint",
            "alterTableRename", "alterTableRenameConstraint",
            "analyze", "commentOn", "createAlias", "createConstant", "createDomain",
            "createIndex", "createSchema", "createSequence", "createSynonym",
            "createTable", "createTrigger", "createView", "dropAllObjects", "dropDomain", "dropIndex",
            "dropSchema", "dropTable", "grant", "truncateTable" })
        {
            TestOneScript("ddl/" + s + ".sql");
        }

        foreach (string s in new[] { "delete", "error_reporting", "execute_immediate", "insert", "insertIgnore",
            "merge", "mergeUsing", "replace", "script", "show", "update", "with" })
        {
            TestOneScript("dml/" + s + ".sql");
        }

        foreach (string s in new[] { "any_value", "any", "array_agg", "avg",
            "bit_and_agg", "bit_or_agg", "bit_xor_agg",
            "corr",
            "count",
            "covar_pop", "covar_samp",
            "envelope", "every", "gcd_agg", "histogram",
            "json_arrayagg", "json_objectagg",
            "listagg", "max", "min", "mode", "percentile", "rank",
            "regr_avgx", "regr_avgy", "regr_count", "regr_intercept", "regr_r2", "regr_slope",
            "regr_sxx", "regr_sxy", "regr_syy",
            "stddev_pop", "stddev_samp", "sum", "var_pop", "var_samp" })
        {
            TestOneScript("functions/aggregate/" + s + ".sql");
        }

        foreach (string s in new[] { "json_array", "json_object" })
        {
            TestOneScript("functions/json/" + s + ".sql");
        }

        foreach (string s in new[] { "abs", "acos", "asin", "atan", "atan2",
            "bitand", "bitcount", "bitget", "bitnot", "bitor", "bitxor", "ceil", "compress",
            "cos", "cosh", "cot", "decrypt", "degrees", "encrypt", "exp",
            "expand", "floor", "gcd", "hash", "length", "log", "lshift", "mod", "ora-hash", "pi",
            "power", "radians", "rand", "random-uuid", "rotate", "round",
            "roundmagic", "rshift", "secure-rand", "sign", "sin", "sinh", "sqrt",
            "tan", "tanh", "truncate", "zero" })
        {
            TestOneScript("functions/numeric/" + s + ".sql");
        }

        foreach (string s in new[] { "array-to-string",
            "ascii", "bit-length", "btrim", "char", "concat",
            "concat-ws", "difference", "hextoraw", "insert",
            "left", "length", "locate", "lower", "lpad", "ltrim",
            "octet-length", "quote_ident", "rawtohex", "regexp-like",
            "regex-replace", "regexp-substr", "repeat", "replace", "right", "rpad", "rtrim",
            "soundex", "space", "stringdecode", "stringencode",
            "stringtoutf8", "substring", "to-char", "translate", "trim",
            "upper", "utf8tostring", "xmlattr", "xmlcdata", "xmlcomment",
            "xmlnode", "xmlstartdoc", "xmltext" })
        {
            TestOneScript("functions/string/" + s + ".sql");
        }

        foreach (string s in new[] { "array-cat", "array-contains", "array-get",
            "array-slice", "autocommit", "cancel-session", "casewhen",
            "cardinality", "cast", "coalesce", "convert", "csvread", "csvwrite", "current_catalog",
            "current_schema", "current_user", "currval", "data_type_sql",
            "database-path", "db_object", "decode", "disk-space-used",
            "file-read", "file-write", "greatest", "h2version", "identity",
            "ifnull", "last-insert-id", "least", "link-schema", "lock-mode", "lock-timeout",
            "memory-free", "memory-used", "nextval", "nullif", "nvl2",
            "readonly", "rownum", "session-id",
            "table", "transaction-id", "trim_array", "truncate-value", "unnest" })
        {
            TestOneScript("functions/system/" + s + ".sql");
        }

        foreach (string s in new[] { "current_date", "current_timestamp",
            "current-time", "dateadd", "datediff", "dayname",
            "day-of-month", "day-of-week", "day-of-year", "extract",
            "formatdatetime", "hour", "last_day", "minute", "month", "monthname",
            "parsedatetime", "quarter", "second", "truncate", "week", "year", "date_trunc" })
        {
            TestOneScript("functions/timeanddate/" + s + ".sql");
        }

        foreach (string s in new[] { "lead", "nth_value", "ntile", "ratio_to_report", "row_number" })
        {
            TestOneScript("functions/window/" + s + ".sql");
        }

        foreach (string s in new[] { "at-time-zone", "boolean-test", "case", "concatenation", "conditions",
            "data-change-delta-table", "field-reference", "help", "invisible", "sequence", "set" })
        {
            TestOneScript("other/" + s + ".sql");
        }

        foreach (string s in new[] { "comments", "identifiers" })
        {
            TestOneScript("parser/" + s + ".sql");
        }

        foreach (string s in new[] { "between", "distinct", "in", "like", "null", "quantified-comparison-with-array",
            "type", "unique" })
        {
            TestOneScript("predicates/" + s + ".sql");
        }

        foreach (string s in new[] { "derived-column-names", "distinct", "duplicate-order-by", "joins",
            "query-cache", "query-optimisations", "select", "table", "values", "window" })
        {
            TestOneScript("queries/" + s + ".sql");
        }

        TestOneScript("other/two_phase_commit.sql");
        TestOneScript("other/unique_include.sql");
        if (!Config.Memory)
        {
            TestOneScript("persistent-only.sql");
        }

        DeleteDb("script");
        Console.Out.Flush();
        if (foundErrors)
        {
            throw new Exception("errors in script found");
        }
    }

    /// <summary>
    /// Get all SQL statements of this file.
    /// </summary>
    /// <param name="conf">the configuration</param>
    /// <returns>the list of statements</returns>
    public List<string> GetAllStatements(TestAll conf)
    {
        Config = conf;
        List<string> result = new(4000);
        try
        {
            statements = result;
            Test();
        }
        finally
        {
            this.statements = null;
        }

        return result;
    }

    private void TestOneScript(string scriptFileName)
    {
        DeleteDb("script");

        // Reset all the state in case there is anything left over from the previous file
        // we processed.
        conn = null!;
        stat = null!;
        fileName = null!;
        @in = null;
        @out = null;
        result.Clear();
        putBack.Clear();

        string outFile;
        if (FixOutput)
        {
            outFile = scriptFileName;
            int idx = outFile.LastIndexOf('/');
            if (idx >= 0)
            {
                outFile = outFile[(idx + 1)..];
            }
        }
        else
        {
            outFile = "test.out.txt";
        }

        conn = GetConnection("script");
        stat = conn.CreateCommand();
        @out = new StreamWriter(outFile);
        TestFile(BaseDir + ResourceName(scriptFileName));
        conn.Close();
        @out.Close();
    }

    /// <summary>
    /// The embedded resource name of a script path the reference writes with slashes.
    /// </summary>
    private static string ResourceName(string scriptFileName)
    {
        return scriptFileName.Replace('/', '.');
    }

    private string? ReadLine()
    {
        return putBack.TryDequeue(out string? s) ? s : ReadNextLine();
    }

    private string? ReadNextLine()
    {
        string? s;
        bool comment = false;
        while ((s = @in!.ReadLine()) is not null)
        {
            lineNumber++;
            if (s.StartsWith("--", StringComparison.Ordinal))
            {
                Write(s);
                comment = true;
                continue;
            }

            if (!FixOutput)
            {
                s = s.Trim();
            }

            if (s.Length != 0)
            {
                break;
            }

            if (comment)
            {
                Write("");
                comment = false;
            }
        }

        return s;
    }

    private void PutBack(string line)
    {
        putBack.Enqueue(line);
    }

    private void TestFile(string inFile)
    {
        Stream? stream = typeof(TestScript).Assembly.GetManifestResourceStream(inFile);
        if (stream is null)
        {
            throw new IOException("could not find " + inFile);
        }

        fileName = inFile;
        @in = new StreamReader(stream, Encoding.UTF8);
        lineNumber = 0;
        StringBuilder buff = new();
        bool allowReconnect = true;
        for (string? sql; (sql = ReadLine()) is not null;)
        {
            if (sql.StartsWith("--", StringComparison.Ordinal))
            {
                Write(sql);
            }
            else if (sql.StartsWith(">", StringComparison.Ordinal))
            {
                AddWriteResultError("<command>", sql);
            }
            else if (sql.EndsWith(";", StringComparison.Ordinal))
            {
                Write(sql);
                _ = buff.Append(sql, 0, sql.Length - 1);
                sql = buff.ToString();
                buff.Clear();
                Process(sql, allowReconnect);
            }
            else if (sql.StartsWith("@", StringComparison.Ordinal))
            {
                if (buff.Length > 0)
                {
                    AddWriteResultError("<command>", sql);
                }
                else
                {
                    switch (sql)
                    {
                        case "@reconnect":
                            Write(sql);
                            Write("");
                            if (!Config.Memory)
                            {
                                Reconnect(GetAutoCommit());
                            }

                            break;
                        case "@reconnect on":
                            Write(sql);
                            Write("");
                            allowReconnect = true;
                            break;
                        case "@reconnect off":
                            Write(sql);
                            Write("");
                            allowReconnect = false;
                            break;
                        case "@autocommit on":
                            SetAutoCommit(true);
                            break;
                        case "@autocommit off":
                            SetAutoCommit(false);
                            break;
                        default:
                            AddWriteResultError("<command>", sql);
                            break;
                    }
                }
            }
            else
            {
                Write(sql);
                _ = buff.Append(sql);
                _ = buff.Append('\n');
            }
        }
    }

    private bool ContainsTempTables()
    {
        // The reference walks DatabaseMetaData.getTables and looks for TEMPORARY in the
        // table SQL; the provider has no metadata surface yet. Unreachable in the default
        // configuration (only reconnectOften reaches it, and that needs config.big).
        throw new TODOException("DatabaseMetaData.getTables (containsTempTables)");
    }

    private void Process(string sql, bool allowReconnect)
    {
        if (allowReconnect && reconnectOften)
        {
            // The reference checks temporary tables, the session mode and the schema
            // before reconnecting; none of those are on the provider yet, so the branch
            // refuses rather than reconnecting blind. It cannot be reached in the default
            // configuration, where reconnectOften is false.
            throw new TODOException(
                "DatabaseMetaData.getTables, the session mode and schema (reconnect often)");
        }

        if (statements is not null)
        {
            statements.Add(sql);
        }

        if (!HasParameters(sql))
        {
            ProcessStatement(sql);
        }
        else
        {
            string param = ReadLine()!;
            Write(param);
            if (!param.Equals("{", StringComparison.Ordinal))
            {
                throw new AssertionError("expected '{', got " + param + " in " + sql);
            }

            try
            {
                using DbCommand prep = conn.CreateCommand();
                prep.CommandText = sql;
                prep.Transaction = transaction;
                int count = 0;
                while (true)
                {
                    param = ReadLine()!;
                    Write(param);
                    if (param.StartsWith("}", StringComparison.Ordinal))
                    {
                        break;
                    }

                    count += ProcessPrepared(sql, prep, param);
                }

                WriteResult(sql, "update count: " + count, null);
            }
            catch (DbException e)
            {
                WriteException(sql, e);
            }
        }

        Write("");
    }

    private static bool HasParameters(string sql)
    {
        int index = 0;
        while (true)
        {
            index = sql.IndexOf('?', index);
            if (index < 0)
            {
                return false;
            }

            int length = sql.Length;
            if (++index == length || sql[index] != '?')
            {
                return true;
            }

            index++;
        }
    }

    private void Reconnect(bool autocommit)
    {
        conn.Close();
        conn = GetConnection("script");
        SetAutoCommit(autocommit);
        stat = conn.CreateCommand();
    }

    /// <summary>
    /// The JDBC autocommit flag, read: a transaction is open exactly when autocommit is
    /// off.
    /// </summary>
    private bool GetAutoCommit()
    {
        return transaction is null;
    }

    private void SetAutoCommit(bool autocommit)
    {
        if (autocommit)
        {
            transaction?.Commit();
            transaction?.Dispose();
            transaction = null;
        }
        else
        {
            transaction ??= conn.BeginTransaction();
        }
    }

    private static void SetParameter(DbCommand prep, int i, string? param)
    {
        while (prep.Parameters.Count < i)
        {
            _ = prep.Parameters.Add(new H2Parameter());
        }

        prep.Parameters[i - 1].Value = param is null ? DBNull.Value : param;
    }

    private int ProcessPrepared(string sql, DbCommand prep, string param)
    {
        try
        {
            StringBuilder buff = new();
            int index = 0;
            for (int i = 0; i < param.Length; i++)
            {
                char c = param[i];
                if (c == ',')
                {
                    SetParameter(prep, ++index, buff.ToString());
                    buff.Clear();
                }
                else if (c == '"')
                {
                    while (true)
                    {
                        c = param[++i];
                        if (c == '"')
                        {
                            break;
                        }

                        _ = buff.Append(c);
                    }
                }
                else if (c > ' ')
                {
                    _ = buff.Append(c);
                }
            }

            if (buff.Length > 0)
            {
                SetParameter(prep, ++index, buff.ToString());
            }

            prep.Transaction = transaction;
            using DbDataReader reader = prep.ExecuteReader();
            if (reader.RecordsAffected < 0)
            {
                WriteResultSet(sql, reader);
                return 0;
            }

            return reader.RecordsAffected;
        }
        catch (DbException e)
        {
            WriteException(sql, e);
            return 0;
        }
    }

    private void ProcessStatement(string sql)
    {
        try
        {
            // The reference runs the statement through a PreparedStatement in the
            // configurations where the ordering cross-check can read the prepared Query
            // (see the class header); the port has no such seam, so both branches here
            // behave alike, and the structure stays for the day the seam exists.
            bool viaPrepared = CheckOrdering
                || Config.Memory && !Config.Lazy && !Config.Networked;
            DbCommand command = viaPrepared ? conn.CreateCommand() : stat;
            try
            {
                command.CommandText = sql;
                command.Transaction = transaction;
                using DbDataReader reader = command.ExecuteReader();
                if (reader.RecordsAffected < 0)
                {
                    WriteResultSet(sql, reader);
                }
                else
                {
                    int count = reader.RecordsAffected;
                    WriteResult(sql, count < 1 ? "ok" : "update count: " + count, null);
                }
            }
            finally
            {
                if (command != stat)
                {
                    command.Dispose();
                }
            }
        }
        catch (DbException e)
        {
            WriteException(sql, e);
        }
    }

    private static string FormatString(string? s)
    {
        if (s is null)
        {
            return "null";
        }

        s = StringUtils.ReplaceAll(s, "\r\n", "\n");
        s = s.Replace('\n', ' ');
        s = StringUtils.ReplaceAll(s, "    ", " ");
        while (true)
        {
            string s2 = StringUtils.ReplaceAll(s, "  ", " ");
            if (s2.Length == s.Length)
            {
                break;
            }

            s = s2;
        }

        return s;
    }

    private static string FormatBinary(byte[]? b)
    {
        if (b is null)
        {
            return "null";
        }

        return "X'" + StringUtils.ConvertBytesToHex(b) + "'";
    }

    private void WriteResultSet(string sql, DbDataReader rs)
    {
        int len = rs.FieldCount;
        int[] max = new int[len];
        result.Clear();
        while (rs.Read())
        {
            string?[] row = new string?[len];
            for (int i = 0; i < len; i++)
            {
                string data = ReadValue(rs, i);
                if (max[i] < data.Length)
                {
                    max[i] = data.Length;
                }

                row[i] = data;
            }

            result.Add(row);
        }

        string?[] head = new string?[len];
        for (int i = 0; i < len; i++)
        {
            string label = FormatString(rs.GetName(i));
            if (max[i] < label.Length)
            {
                max[i] = label.Length;
            }

            head[i] = label;
        }

        // The reference reads the prepared Query here (through reflection) and sets
        // gotOrdered from Query.hasOrder; the port keeps no prepared command to read, so
        // the cross-check reads nothing. See the class header.
        bool? gotOrdered = null;
        rs.Close();
        string? line = ReadLine();
        if (line is not null)
        {
            PutBack(line);
        }

        if (line is not null && line.StartsWith(">> ", StringComparison.Ordinal))
        {
            switch (result.Count)
            {
                case 0:
                    WriteResult(sql, "<no result>", null, ">> ");
                    return;
                case 1:
                    string?[] row = result[0];
                    if (row.Length == 1)
                    {
                        WriteResult(sql, row[0]!, null, ">> ");
                    }
                    else
                    {
                        WriteResult(sql, "<row with " + row.Length + " values>", null, ">> ");
                    }

                    return;
                default:
                    WriteResult(sql, "<" + result.Count + " rows>", null, ">> ");
                    return;
            }
        }

        bool? ordered;
        while (true)
        {
            line = ReadNextLine();
            if (line is null)
            {
                AddWriteResultError("<row count>", "<eof>");
                return;
            }

            PutBack(line);
            if (line.StartsWith("> rows: ", StringComparison.Ordinal))
            {
                ordered = false;
                break;
            }
            else if (line.StartsWith("> rows (ordered): ", StringComparison.Ordinal))
            {
                ordered = true;
                break;
            }
            else if (line.StartsWith("> rows (partially ordered): ", StringComparison.Ordinal))
            {
                ordered = null;
                break;
            }
        }

        if (gotOrdered is not null)
        {
            if (ordered is null || ordered.Value)
            {
                if (!gotOrdered.Value)
                {
                    AddWriteResultError("<ordered result set>", "<result set>");
                }
            }
            else
            {
                if (gotOrdered.Value)
                {
                    AddWriteResultError("<result set>", "<ordered result set>");
                }
            }
        }

        WriteResult(sql, Format(head!, max), null);
        WriteResult(sql, Format(null, max), null);
        string?[] array = new string?[result.Count];
        for (int i = 0; i < result.Count; i++)
        {
            array[i] = Format(result[i], max);
        }

        if (!true.Equals(ordered))
        {
            Sort(array);
        }

        int j = 0;
        for (; j < array.Length; j++)
        {
            WriteResult(sql, array[j]!, null);
        }

        WriteResult(sql,
            (ordered is not null ? ordered.Value ? "rows (ordered): " : "rows: "
                : "rows (partially ordered): ") + j,
            null);
    }

    /// <summary>
    /// The reference's readValue: a binary column renders as X'..', everything else as
    /// its string form. The reference names the columns by java.sql.Types through
    /// DataType.isBinaryColumn; the port names them by the declared type the reader
    /// answers with — the same value types under their SQL names.
    /// </summary>
    private static string ReadValue(DbDataReader rs, int column)
    {
        return IsBinaryColumn(rs, column)
            ? FormatBinary(rs.IsDBNull(column) ? null : (byte[])rs.GetValue(column))
            : FormatString(rs.IsDBNull(column) ? null : rs.GetString(column));
    }

    private static bool IsBinaryColumn(DbDataReader rs, int column)
    {
        switch (rs.GetDataTypeName(column))
        {
            case "BINARY":
            case "BINARY VARYING":
            case "BINARY LARGE OBJECT":
            case "JAVA_OBJECT":
                return true;
            default:
                return false;
        }
    }

    private static string Format(string?[]? row, int[] max)
    {
        int length = max.Length;
        StringBuilder buff = new();
        for (int i = 0; i < length; i++)
        {
            if (i > 0)
            {
                _ = buff.Append(' ');
            }

            if (row is null)
            {
                for (int j = 0; j < max[i]; j++)
                {
                    _ = buff.Append('-');
                }
            }
            else
            {
                int len = row[i]!.Length;
                _ = buff.Append(row[i]);
                if (i < length - 1)
                {
                    for (int j = len; j < max[i]; j++)
                    {
                        _ = buff.Append(' ');
                    }
                }
            }
        }

        return buff.ToString();
    }

    /// <summary>
    /// The error code to a symbolic name from ErrorCode.
    /// </summary>
    /// <remarks>
    /// CLR ErrorCode members are PascalCase and include deprecated aliases. The embedded
    /// snapshot was reflected from the pinned jar with the reference runner's exact filter;
    /// it preserves the original symbolic names expected by the unchanged SQL scripts.
    /// </remarks>
    private static readonly Dictionary<int, string> ErrorCodeToName = BuildErrorCodeToName();

    private static Dictionary<int, string> BuildErrorCodeToName()
    {
        Dictionary<int, string> map = new(256);
        using Stream stream = typeof(TestScript).Assembly.GetManifestResourceStream(BaseDir + "ErrorCodeNames.csv")
            ?? throw new FileNotFoundException("Pinned H2 ErrorCode name snapshot is missing");
        using StreamReader reader = new(stream);
        while (reader.ReadLine() is string line)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            int comma = line.IndexOf(',');
            map[int.Parse(line[..comma], CultureInfo.InvariantCulture)] = line[(comma + 1)..];
        }

        return map;
    }

    private void WriteException(string sql, DbException ex)
    {
        WriteResult(sql, "exception " + (ErrorCodeToName.TryGetValue(
            ex is H2SharpException h2Exception ? h2Exception.H2ErrorCode : 0,
            out string? name) ? name : "null"), ex);
    }

    private void WriteResult(string sql, string s, DbException? ex)
    {
        WriteResult(sql, s, ex, "> ");
    }

    private void WriteResult(string sql, string s, DbException? ex, string prefix)
    {
        AssertKnownException(sql, ex);
        s = (prefix + s).Trim();
        string? compare = ReadLine();
        if (compare is not null && compare.StartsWith(">", StringComparison.Ordinal))
        {
            if (!compare.Equals(s, StringComparison.Ordinal))
            {
                if (reconnectOften && sql.ToUpperInvariant().StartsWith("EXPLAIN",
                    StringComparison.Ordinal))
                {
                    return;
                }

                AddWriteResultError(compare, s);
                if (ex is not null)
                {
                    LogError("script", ex);
                }

                if (FailFast)
                {
                    conn.Close();
                    Environment.Exit(1);
                }
            }
        }
        else
        {
            AddWriteResultError("<nothing>", s);
            if (compare is not null)
            {
                PutBack(compare);
            }
        }

        Write(s);
    }

    private void AddWriteResultError(string expected, string got)
    {
        foundErrors = true;
        string msg = fileName + '\n'
            + "line: " + lineNumber + '\n'
            + "exp: " + expected + '\n'
            + "got: " + got + '\n';
        LogErrorMessage(msg);
    }

    private void Write(string s)
    {
        @out!.WriteLine(s);
    }

    private static void Sort(string?[] a)
    {
        for (int i = 1, j, len = a.Length; i < len; i++)
        {
            string? t = a[i];
            for (j = i - 1; j >= 0 && string.CompareOrdinal(t, a[j]) < 0; j--)
            {
                a[j + 1] = a[j];
            }

            a[j + 1] = t;
        }
    }
}

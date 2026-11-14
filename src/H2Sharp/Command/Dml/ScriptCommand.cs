/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.dml.ScriptCommand, H2 2.5.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * SCRIPT.
 *
 * Generates the SQL that recreates the database. Plain TO-file output, the CHARSET of that
 * file, and the SCHEMA/TABLE filters are ported; COMPRESSION and CIPHER remain unported
 * (CIPHER is out of scope by D-026). Settings and users/roles are not emitted: D-004 has no
 * users or roles and settings are not ported. Asking for either through the options still
 * builds the same script, because the default switches are read from the grammar exactly as
 * in the reference.
 */
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Command.Query;
using H2Sharp.Constraints;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Indexes;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Schemas;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;
using Index = H2Sharp.Indexes.Index;

namespace H2Sharp.Command.Dml;

/// <summary>The SCRIPT statement.</summary>
public sealed class ScriptCommand : Prepared
{
    private bool data = true;

    private bool passwords = true;

    private bool settings = true;

    private bool version = true;

    private bool drop;

    private bool simple;

    private bool withColumns;

    private int lobBlockSize = Constants.IoBufferSize;

    private Expressions.Expression? fileNameExpression;

    /// <summary>
    /// The charset of the TO file: UTF-8 without a byte order mark, as the reference's
    /// <c>StandardCharsets.UTF_8</c> writer produces.
    /// </summary>
    private Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The schemas to script, or null for all of them.</summary>
    private HashSet<string>? schemaNames;

    /// <summary>The tables to script, or null for all of them.</summary>
    private List<Table>? tables;

    private bool tempLobTableCreated;

    private int nextLobId;

    /// <summary>Creates the statement.</summary>
    public ScriptCommand(SessionLocal session)
        : base(session)
    {
    }

    /// <summary>Sets whether rows are written.</summary>
    public void SetData(bool value) => data = value;

    /// <summary>Sets whether passwords are included.</summary>
    public void SetPasswords(bool value) => passwords = value;

    /// <summary>Sets whether settings are included.</summary>
    public void SetSettings(bool value) => settings = value;

    /// <summary>Sets whether the version header is included.</summary>
    public void SetVersion(bool value) => version = value;

    /// <summary>Sets whether DROP statements are emitted.</summary>
    public void SetDrop(bool value) => drop = value;

    /// <summary>Sets one-line INSERT statements.</summary>
    public void SetSimple(bool value) => simple = value;

    /// <summary>Sets whether INSERT lists columns explicitly.</summary>
    public void SetWithColumns(bool value) => withColumns = value;

    /// <summary>Sets the maximum size of a LOB block.</summary>
    public void SetLobBlockSize(long value)
        => lobBlockSize = (int)Math.Min(int.MaxValue, value);

    /// <summary>Sets the charset of the TO file.</summary>
    /// <remarks>
    /// The reference asks <c>Charset.forName</c>; the port maps the charsets .NET names the
    /// same way and refuses the rest by name. Java's <c>UTF-16</c> writes a big-endian byte
    /// order mark, <c>UTF-16LE</c>/<c>UTF-16BE</c> write none, and <c>UTF-8</c> writes none —
    /// the mapping below keeps those differences.
    /// </remarks>
    /// <param name="name">the charset name</param>
    public void SetCharset(string name) => encoding = ParseCharset(name);

    /// <summary>Sets the schemas to script, or null for all of them.</summary>
    public void SetSchemaNames(HashSet<string> schemaNames) => this.schemaNames = schemaNames;

    /// <summary>Sets the tables to script, or null for all of them.</summary>
    public void SetTables(List<Table> tables) => this.tables = tables;

    private static Encoding ParseCharset(string name)
    {
        switch (name.ToUpperInvariant())
        {
            case "UTF-8":
            case "UTF8":
                return new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            case "UTF-16":
                return new UnicodeEncoding(
                    bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: true);
            case "UTF-16BE":
                return new UnicodeEncoding(
                    bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true);
            case "UTF-16LE":
                return new UnicodeEncoding(
                    bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);
            case "UTF-32":
                return new UTF32Encoding(
                    bigEndian: true, byteOrderMark: true, throwOnInvalidCharacters: true);
            case "UTF-32BE":
                return new UTF32Encoding(
                    bigEndian: true, byteOrderMark: false, throwOnInvalidCharacters: true);
            case "UTF-32LE":
                return new UTF32Encoding(
                    bigEndian: false, byteOrderMark: false, throwOnInvalidCharacters: true);
            case "US-ASCII":
            case "ASCII":
                // Java's encoder reports an unmappable character; .NET's default replaces it.
                return Encoding.GetEncoding("us-ascii",
                    EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            case "ISO-8859-1":
            case "ISO8859-1":
            case "LATIN1":
                return Encoding.GetEncoding("iso-8859-1",
                    EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            default:
                throw DbException.GetUnsupportedException("charset " + name);
        }
    }

    /// <summary>Sets the expression the TO file name is read from.</summary>
    /// <param name="expression">the file name expression</param>
    public void SetFileNameExpression(Expressions.Expression expression)
        => fileNameExpression = expression;

    /// <inheritdoc/>
    public override bool IsQuery() => true;

    /// <inheritdoc/>
    public override bool IsTransactional() => false;

    /// <inheritdoc/>
    public override bool IsReadOnly() => true;

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.Script;

    /// <inheritdoc/>
    public override ResultInterface QueryMeta()
    {
        LocalResult result = CreateResult();
        result.Done();
        return result;
    }

    private LocalResult CreateResult()
        => new(GetSession(),
            [new ExpressionColumn(GetSession().GetDatabase(), new Column("SCRIPT", TypeInfo.TypeVarchar))], 1, 1);

    /// <inheritdoc/>
    public override ResultInterface Run(long maxrows)
    {
        LocalResult result = CreateResult();
        List<(string Line, bool Insert)> lines = [];
        void Add(string? sql, bool insert = false)
        {
            if (!string.IsNullOrEmpty(sql))
            {
                lines.Add((sql + ";", insert));
            }
        }

        Database db = GetSession().GetDatabase();
        if (schemaNames is not null)
        {
            // The reference validates the requested schemas up front, so a typo names the
            // missing schema instead of silently producing an empty script.
            foreach (string schemaName in schemaNames)
            {
                _ = db.GetSchema(schemaName);
            }
        }

        if (version)
        {
            Add("-- H2 " + Constants.Version);
        }

        List<Schema> schemas = [];
        foreach (Schema schema in db.GetSchemas())
        {
            // PUBLIC is a system schema in the reference, but its user objects are scripted;
            // the other system schema (INFORMATION_SCHEMA) is skipped. System schemas answer
            // null from GetCreateSql, so no CREATE SCHEMA line is emitted for PUBLIC.
            if (schema.IsSystem() && !ReferenceEquals(schema, db.GetMainSchema()))
            {
                continue;
            }

            if (ExcludeSchema(schema))
            {
                continue;
            }

            schemas.Add(schema);
            Add(schema.GetCreateSql());
        }

        // Reference order: the domain dictation (drop and create) comes before the table drop
        // pass, then the table drops, then the sequences with their drops and creates.
        // The reference topologically sorts the domains: a domain that another one builds
        // on is printed first, so the script can be replayed in order (dumpDomains).
        Comparer<Domain> domainComparer = Comparer<Domain>.Create(static (a, b)
            => string.CompareOrdinal(a.GetName(), b.GetName()));
        SortedDictionary<Domain, SortedSet<Domain>> referencingDomains =
            new(domainComparer);
        SortedSet<Domain> known = new(domainComparer);
        foreach (Schema schema in schemas)
        {
            List<Domain> domains = schema.GetAllDomains();
            domains.Sort(static (a, b) => string.CompareOrdinal(a.GetName(), b.GetName()));
            foreach (Domain domain in domains)
            {
                Domain? parent = domain.GetDomain();
                if (parent is null)
                {
                    if (drop)
                    {
                        Add(domain.GetDropSql());
                    }

                    Add(domain.GetCreateSql());
                }
                else
                {
                    if (!referencingDomains.TryGetValue(parent, out SortedSet<Domain>? set))
                    {
                        set = new SortedSet<Domain>(domainComparer);
                        referencingDomains[parent] = set;
                    }

                    set.Add(domain);
                    if (parent.GetDomain() is null || !schemas.Contains(parent.GetSchema()))
                    {
                        known.Add(parent);
                    }
                }
            }
        }

        while (referencingDomains.Count > 0)
        {
            SortedSet<Domain> known2 = new(domainComparer);
            foreach (Domain d in known)
            {
                if (referencingDomains.Remove(d, out SortedSet<Domain>? set))
                {
                    foreach (Domain d2 in set)
                    {
                        if (drop)
                        {
                            Add(d2.GetDropSql());
                        }

                        Add(d2.GetCreateSql());
                        known2.Add(d2);
                    }
                }
            }

            known = known2;
        }

        // Reference order after the domains: constants, then the table drop pass.
        foreach (Schema schema in schemas)
        {
            List<Constant> constants = schema.GetAllConstants();
            constants.Sort(static (a, b) => string.CompareOrdinal(a.GetName(), b.GetName()));
            foreach (Constant constant in constants)
            {
                Add(constant.GetCreateSql());
            }
        }

        if (drop)
        {
            foreach (Table table in BuildTables(schemas))
            {
                Add(table.GetDropSql());
            }
        }

        foreach (Sequence sequence in BuildSequences(schemas))
        {
            if (drop)
            {
                Add(sequence.GetDropSql());
            }

            Add(sequence.GetCreateSql());
        }

        foreach (Table table in BuildTables(schemas))
        {
            string? createSql = table.GetCreateSql();
            if (createSql is null) continue;
            Add(createSql);
            foreach (Constraint constraint in table.GetConstraints())
            {
                if (constraint.GetConstraintType() == Constraint.Type.PRIMARY_KEY)
                {
                    Add(constraint.GetCreateSqlWithoutIndexes());
                }
            }

            if (table.GetTableType() == Table.TABLE)
            {
                if (table.CanGetRowCount(GetSession()))
                {
                    StringBuilder builder = new StringBuilder("-- ")
                        .Append(table.GetRowCountApproximation(GetSession()))
                        .Append(" +/- SELECT COUNT(*) FROM ");
                    _ = table.GetSql(builder, HasSql.TraceSqlFlags);
                    Add(builder.ToString());
                }

                if (data)
                {
                    GenerateInsertValues(table, sql => Add(sql, true));
                }
            }

            foreach (Index index in table.GetIndexes())
            {
                if (index.GetIndexType() is { } indexType && !indexType.GetBelongsToConstraint())
                {
                    Add(index.GetCreateSql());
                }
            }

        }

        // The reference drops the temporary LOB helpers after the whole table loop and before
        // the non-primary constraints.
        if (tempLobTableCreated)
        {
            Add("DROP TABLE IF EXISTS SYSTEM_LOB_STREAM", true);
            Add("DROP ALIAS IF EXISTS SYSTEM_COMBINE_CLOB", true);
            Add("DROP ALIAS IF EXISTS SYSTEM_COMBINE_BLOB", true);
            tempLobTableCreated = false;
        }

        foreach (Schema schema in schemas)
        {
            foreach (Constraint constraint in schema.GetAllConstraints())
            {
                if (constraint.GetConstraintType() != Constraint.Type.PRIMARY_KEY)
                {
                    Add(constraint.GetCreateSqlWithoutIndexes());
                }
            }
        }

        // The reference streams every line to the TO file when one is named, while the
        // result keeps only the non-data lines; without TO everything lands in the result.
        string? fileName = fileNameExpression?.GetValue(GetSession())?.GetString();
        if (fileName is not null)
        {
            // A StreamWriter over the chosen charset, not File.WriteAllLines: the default
            // Encoding.UTF8 writes a byte order mark and the reference's UTF-8 does not.
            using StreamWriter writer = new(fileName, append: false, encoding);
            foreach ((string line, bool _) in lines)
            {
                writer.WriteLine(line);
            }
        }

        foreach ((string line, bool insert) in lines)
        {
            if (fileName is null || !insert)
            {
                result.AddRow(ValueVarchar.Get(line));
            }
        }

        result.Done();
        return result;
    }

    /// <summary>
    /// Whether the schema is outside the requested SCHEMA/TABLE filters, as the reference's
    /// <c>excludeSchema</c> answers.
    /// </summary>
    /// <param name="schema">the schema</param>
    /// <returns>true when it is excluded</returns>
    private bool ExcludeSchema(Schema schema)
    {
        if (schemaNames is not null && !schemaNames.Contains(schema.GetName()!))
        {
            return true;
        }

        if (tables is not null)
        {
            // When filtering on specific tables, only the schemas that hold one are scripted.
            foreach (Table table in schema.GetAllTablesAndViews(GetSession()))
            {
                if (tables.Contains(table))
                {
                    return false;
                }
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the table is outside the requested TABLE filter.
    /// </summary>
    /// <param name="table">the table</param>
    /// <returns>true when it is excluded</returns>
    private bool ExcludeTable(Table table) => tables is not null && !tables.Contains(table);

    /// <summary>
    /// The tables of the schemas, in id order.
    /// /// </summary>
    private List<Table> BuildTables(List<Schema> schemas)
    {
        List<Table> tables = [];
        foreach (Schema schema in schemas)
        {
            tables.AddRange(schema.GetAllTablesAndViews(null!));
        }

        tables.Sort(static (a, b) => a.GetId().CompareTo(b.GetId()));
        tables.RemoveAll(ExcludeTable);
        return tables;
    }

    /// <summary>
    /// The sequence of the schemas, ordered by name; identity-column sequences are scripted
    /// with their tables and are not here.
    /// /// </summary>
    private static List<Sequence> BuildSequences(List<Schema> schemas)
    {
        List<Sequence> sequences = [];
        foreach (Schema schema in schemas)
        {
            foreach (Sequence sequence in schema.GetAllSequences())
            {
                if (!sequence.GetBelongsToTable())
                {
                    sequences.Add(sequence);
                }
            }
        }

        sequences.Sort(static (a, b) => string.CompareOrdinal(a.GetName(), b.GetName()));
        return sequences;
    }

    private void GenerateInsertValues(Table table, Action<string> add)
    {
        StringBuilder builder = new("INSERT INTO ");
        _ = table.GetSql(builder, HasSql.DefaultSqlFlags);

        Column[] columns = table.GetColumns();
        bool withGenerated = false, withGeneratedAlwaysAsIdentity = false;
        foreach (Column column in columns)
        {
            // The reference asks isGeneratedAlways() here, which includes an identity column:
            // isGenerated() is false for an identity without a default expression, and the
            // OVERRIDING SYSTEM VALUE would be lost.
            if (!column.IsGeneratedAlways()) continue;
            if (column.IsIdentity()) withGeneratedAlwaysAsIdentity = true;
            else withGenerated = true;
        }

        if (withGenerated || withGeneratedAlwaysAsIdentity || withColumns)
        {
            _ = builder.Append('(');
            bool needComma = false;
            foreach (Column column in columns)
            {
                if (column.IsGenerated()) continue;
                if (needComma) _ = builder.Append(", ");
                needComma = true;
                _ = column.GetSql(builder, HasSql.DefaultSqlFlags);
            }

            _ = builder.Append(')');
            if (withGeneratedAlwaysAsIdentity)
            {
                _ = builder.Append(" OVERRIDING SYSTEM VALUE");
            }
        }

        _ = builder.Append(" VALUES");
        if (!simple)
        {
            _ = builder.Append('\n');
        }

        Index? scan = table.GetScanIndex(GetSession());
        if (scan is null)
        {
            throw DbException.GetInternalError("SCRIPT: table has no scan index");
        }

        Cursor cursor = scan.GetCursor(GetSession(), null, null, false)
            ?? throw DbException.GetInternalError("SCRIPT: table has no cursor");
        // The reference opens the row with '(' before taking the header snapshot, so each
        // additional row starts at ",\n(" and every flushed chunk is a complete statement.
        _ = builder.Append('(');
        string insertHeader = builder.ToString();
        StringBuilder? rowBuilder = null;
        while (cursor.Next())
        {
            Row? row = cursor.Get()
                ?? throw DbException.GetInternalError("SCRIPT: cursor returned no row");
            if (rowBuilder is null)
            {
                rowBuilder = new StringBuilder(insertHeader);
            }
            else
            {
                _ = rowBuilder.Append(",\n(");
            }

            bool needComma = false;
            for (int i = 0; i < columns.Length; i++)
            {
                if (columns[i].IsGenerated()) continue;
                if (needComma) _ = rowBuilder.Append(", ");
                needComma = true;
                Value value = row.GetValue(i)!;
                if (value.GetTypeInfo().GetPrecision() > lobBlockSize)
                {
                    if (value.GetValueType() == Value.Clob)
                    {
                        int id = WriteLobStream(value, add);
                        _ = rowBuilder.Append("SYSTEM_COMBINE_CLOB(").Append(id).Append(')');
                        continue;
                    }

                    if (value.GetValueType() == Value.Blob)
                    {
                        int id = WriteLobStream(value, add);
                        _ = rowBuilder.Append("SYSTEM_COMBINE_BLOB(").Append(id).Append(')');
                        continue;
                    }
                }

                _ = value.GetSql(rowBuilder, HasSql.NoCasts);
            }

            _ = rowBuilder.Append(')');
            // SIMPLE writes one INSERT per row; otherwise the statement grows until it
            // crosses the buffer size, and the rows written so far stand as their own
            // complete INSERT.
            if (simple || rowBuilder.Length > Constants.IoBufferSize)
            {
                add(rowBuilder.ToString());
                rowBuilder = null;
            }
        }

        if (rowBuilder is not null)
        {
            add(rowBuilder.ToString());
        }
    }

    private int WriteLobStream(Value value, Action<string> add)
    {
        if (!tempLobTableCreated)
        {
            add("CREATE CACHED LOCAL TEMPORARY TABLE IF NOT EXISTS SYSTEM_LOB_STREAM"
                + "(ID INT NOT NULL, PART INT NOT NULL, CDATA VARCHAR, BDATA VARBINARY)");
            add("ALTER TABLE SYSTEM_LOB_STREAM ADD CONSTRAINT SYSTEM_LOB_STREAM_PRIMARY_KEY PRIMARY KEY(ID, PART)");
            add("CREATE ALIAS IF NOT EXISTS SYSTEM_COMBINE_CLOB FOR 'org.h2.command.dml.ScriptCommand.combineClob'");
            add("CREATE ALIAS IF NOT EXISTS SYSTEM_COMBINE_BLOB FOR 'org.h2.command.dml.ScriptCommand.combineBlob'");
            tempLobTableCreated = true;
        }

        int id = nextLobId++;
        if (value.GetValueType() == Value.Blob)
        {
            // The block buffer rents for the whole run; a rented array is longer than the
            // block size, so the read is bounded to it and the block boundaries stay what
            // they were.
            byte[] bytes = ArrayPool<byte>.Shared.Rent(lobBlockSize);
            try
            {
                using Stream input = value.GetInputStream();
                for (int part = 0;; part++)
                {
                    int len = ReadFully(input, bytes, lobBlockSize);
                    if (len <= 0)
                    {
                        break;
                    }

                    StringBuilder buff = new(lobBlockSize * 2);
                    _ = buff.Append("INSERT INTO SYSTEM_LOB_STREAM VALUES(").Append(id)
                        .Append(", ").Append(part).Append(", NULL, X'");
                    _ = StringUtils.ConvertBytesToHex(buff, bytes, len).Append("')");
                    add(buff.ToString());
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bytes);
            }
        }
        else
        {
            // The block buffer rents for the whole run, and the quoting reads the slice
            // directly: per block, what is left is the builder and its string.
            char[] chars = ArrayPool<char>.Shared.Rent(lobBlockSize);
            try
            {
                using TextReader reader = value.GetReader()!;
                for (int part = 0;; part++)
                {
                    int len = ReadFully(reader, chars, lobBlockSize);
                    if (len == 0)
                    {
                        break;
                    }

                    StringBuilder buff = new(lobBlockSize * 2);
                    _ = buff.Append("INSERT INTO SYSTEM_LOB_STREAM VALUES(").Append(id)
                        .Append(", ").Append(part).Append(", ");
                    _ = StringUtils.QuoteStringSql(buff, chars.AsSpan(0, len)).Append(", NULL)");
                    add(buff.ToString());
                }
            }
            finally
            {
                ArrayPool<char>.Shared.Return(chars);
            }
        }

        return id;
    }

    private static int ReadFully(Stream input, byte[] buffer, int len)
    {
        int total = 0;
        while (total < len)
        {
            int read = input.Read(buffer, total, len - total);
            if (read <= 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static int ReadFully(TextReader input, char[] buffer, int len)
    {
        int total = 0;
        while (total < len)
        {
            int read = input.Read(buffer, total, len - total);
            if (read <= 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}

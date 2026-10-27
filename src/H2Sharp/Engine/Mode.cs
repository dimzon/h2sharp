/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.engine.Mode.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using H2Sharp.Util;

namespace H2Sharp.Engine;

/// <summary>
/// How a CHAR value is padded, which decides whether a conversion to CHAR pads it or trims it.
/// </summary>
public enum CharPadding
{
    /// <summary>
    /// CHAR values are always right-padded with spaces.
    /// </summary>
    Always,

    /// <summary>
    /// Spaces are trimmed from the right side of CHAR values, but CHAR values in result sets are
    /// right-padded with spaces to the declared length.
    /// </summary>
    InResultSets,

    /// <summary>
    /// Spaces are trimmed from the right side of CHAR values.
    /// </summary>
    Never,
}

/// <summary>
/// The compatibility mode a database runs in, which decides how a statement is read rather than
/// what it means.
/// </summary>
/// <remarks>
/// The reference carries 59 flags and this carries the seven that something reads. Five of them
/// are read by the tokenizer and are the places where the same characters mean different things in
/// different databases; the other two are read by the conversion family, which is where a CHAR is
/// padded and where a NUMERIC's scale is reduced. Every other flag belongs to a feature that is
/// not ported yet; a flag nobody reads is a flag nobody can test. A flag arrives with the feature
/// that needs it, the same way <see cref="CastDataProvider"/> carries one member of the
/// reference's five for the same reason.
/// <para>
/// The table below is the reference's own, read off the static initialiser: for each mode, which of
/// the eight are on. It is worth having in full even though only the tokenizer and the conversion
/// family read it, because "which mode turns this on" is the question that every future feature
/// starts with.
/// </para>
/// </remarks>
public sealed class Mode
{
    /// <summary>
    /// The names of the modes, in the order the reference declares them.
    /// </summary>
    public enum ModeEnum
    {
        /// <summary>
        /// The mode a database gets when nothing else is asked for.
        /// </summary>
        Regular,

        /// <summary>
        /// The mode that allows nothing the standard does not.
        /// </summary>
        Strict,

        /// <summary>
        /// The mode that allows what earlier versions of the reference allowed.
        /// </summary>
        Legacy,

        /// <summary>
        /// The mode of IBM DB2.
        /// </summary>
        Db2,

        /// <summary>
        /// The mode of Apache Derby.
        /// </summary>
        Derby,

        /// <summary>
        /// The mode of MariaDB.
        /// </summary>
        MariaDb,

        /// <summary>
        /// The mode of Microsoft SQL Server.
        /// </summary>
        MsSqlServer,

        /// <summary>
        /// The mode of HyperSQL.
        /// </summary>
        HsqlDb,

        /// <summary>
        /// The mode of MySQL.
        /// </summary>
        MySql,

        /// <summary>
        /// The mode of Oracle.
        /// </summary>
        Oracle,

        /// <summary>
        /// The mode of PostgreSQL.
        /// </summary>
        PostgreSql,
    }

    /// <summary>
    /// Whether <c>#</c> begins a name, as it does in Oracle and in SQL Server.
    /// </summary>
    public bool SupportPoundSymbolForColumnNames { get; }

    /// <summary>
    /// Whether a number written <c>0x1A</c> is a string of bytes rather than the number 26.
    /// </summary>
    public bool ZeroExLiteralsAreBinaryStrings { get; }

    /// <summary>
    /// Whether a name may be written between square brackets, as in <c>[Test]</c>.
    /// </summary>
    public bool SquareBracketQuotedNames { get; }

    /// <summary>
    /// Whether <c>LIMIT</c> is a keyword rather than a name.
    /// </summary>
    public bool Limit { get; }

    /// <summary>
    /// Whether <c>MINUS</c> may be read as <c>EXCEPT</c>.
    /// </summary>
    public bool MinusIsExcept { get; }

    /// <summary>
    /// Whether a CHAR value is padded out to its declared length, which decides whether a
    /// conversion to CHAR restores the padding or trims it.
    /// </summary>
    public CharPadding CharPadding { get; }

    /// <summary>
    /// Whether a conversion may reduce a NUMERIC's scale, or has to leave it as it is.
    /// </summary>
    public bool ConvertOnlyToSmallerScale { get; }

    /// <summary>
    /// Whether a DISTINCT query may order by an expression that is not in its select list.
    /// </summary>
    /// <remarks>
    /// The eighth flag the port carries; the reference has dozens. Only MySQL and MariaDB
    /// turn it on, which is where the table below puts it.
    /// </remarks>
    public bool AllowUnrelatedOrderByExpressionsInDistinctQueries { get; }

    /// <summary>
    /// Whether an empty string is NULL.
    /// </summary>
    /// <remarks>
    /// The ninth flag the port carries. Only Oracle turns it on, which is where the table
    /// below puts it.
    /// </remarks>
    public bool TreatEmptyStringsAsNull { get; }

    /// <summary>
    /// Whether GREATEST and LEAST ignore NULL values instead of answering NULL as soon as one
    /// argument is NULL.
    /// </summary>
    /// <remarks>
    /// The tenth flag the port carries. Legacy and SQL Server turn it on, which is where the
    /// table below puts it.
    /// </remarks>
    public bool GreatestLeastIgnoreNulls { get; }

    /// <summary>
    /// Whether LOG() takes its arguments the other way round, as SQL Server's does.
    /// </summary>
    /// <remarks>
    /// The eleventh flag the port carries. Only SQL Server turns it on, which is where the
    /// table below puts it.
    /// </remarks>
    public bool SwapLogFunctionParameters { get; }

    /// <summary>Whether <c>+</c> concatenates strings.</summary>
    public bool AllowPlusForStringConcat { get; }

    /// <summary>
    /// Whether one-argument LOG() is base 10 rather than natural, as PostgreSQL's is.
    /// </summary>
    /// <remarks>
    /// The twelfth flag the port carries. Only PostgreSQL turns it on, which is where the table
    /// below puts it.
    /// </remarks>
    public bool LogIsLogBase10 { get; }

    /// <summary>Whether a comma may separate a JSON_OBJECT key and value (MySQL/MariaDB).</summary>
    public bool AcceptsCommaAsJsonKeyValueSeparator { get; }

    /// <summary>Whether sequences of this mode use NUMERIC instead of BIGINT by default.</summary>
    public bool DecimalSequences { get; }

    /// <summary>Whether an integer in GROUP BY is a select-list position (MySQL/MariaDB/PostgreSQL).</summary>
    public bool GroupByColumnIndex { get; }

    /// <summary>
    /// Whether a SELECT may start with TOP, as in SQL Server and H2's own regular mode.
    /// </summary>
    public bool TopInSelect { get; }

    /// <summary>
    /// Whether UPDATE and DELETE may start with TOP, as they may in SQL Server and Legacy.
    /// </summary>
    public bool TopInDml { get; }

    /// <summary>Whether IDENTITY is a data type, as in H2's regular and legacy modes.</summary>
    public bool IdentityDataType { get; }

    /// <summary>Whether SERIAL and BIGSERIAL declare identity columns.</summary>
    public bool SerialDataTypes { get; }

    /// <summary>Whether AUTO_INCREMENT declares an identity column.</summary>
    public bool AutoIncrementClause { get; }

    /// <summary>Whether the IDENTITY clause declares an identity column.</summary>
    public bool IdentityClause { get; }

    /// <summary>
    /// The type names this mode does not allow, as in the reference's disallowedTypes.
    /// </summary>
    public IReadOnlySet<string> DisallowedTypes { get; }

    /// <summary>
    /// The name of the mode, as it is written in a URL.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Which of the modes this is.
    /// </summary>
    public ModeEnum Enum { get; }

    private Mode(
        ModeEnum modeEnum,
        bool pound,
        bool zeroExIsBinary,
        bool squareBrackets,
        bool limit,
        bool minusIsExcept,
        CharPadding charPadding = CharPadding.Always,
        bool convertOnlyToSmallerScale = false,
        bool allowUnrelatedOrderByExpressionsInDistinctQueries = false,
        bool treatEmptyStringsAsNull = false,
        bool greatestLeastIgnoreNulls = false,
        bool swapLogFunctionParameters = false,
        bool allowPlusForStringConcat = false,
        bool logIsLogBase10 = false,
        bool acceptsCommaAsJsonKeyValueSeparator = false,
        bool decimalSequences = false,
        bool groupByColumnIndex = false,
        bool topInSelect = false,
        bool topInDml = false,
        bool identityDataType = false,
        bool serialDataTypes = false,
        bool autoIncrementClause = false,
        bool identityClause = false,
        IReadOnlySet<string>? disallowedTypes = null)
    {
        Enum = modeEnum;
        Name = NameOf(modeEnum);
        SupportPoundSymbolForColumnNames = pound;
        ZeroExLiteralsAreBinaryStrings = zeroExIsBinary;
        SquareBracketQuotedNames = squareBrackets;
        Limit = limit;
        MinusIsExcept = minusIsExcept;
        CharPadding = charPadding;
        ConvertOnlyToSmallerScale = convertOnlyToSmallerScale;
        AllowUnrelatedOrderByExpressionsInDistinctQueries = allowUnrelatedOrderByExpressionsInDistinctQueries;
        TreatEmptyStringsAsNull = treatEmptyStringsAsNull;
        GreatestLeastIgnoreNulls = greatestLeastIgnoreNulls;
        SwapLogFunctionParameters = swapLogFunctionParameters;
        AllowPlusForStringConcat = allowPlusForStringConcat;
        LogIsLogBase10 = logIsLogBase10;
        AcceptsCommaAsJsonKeyValueSeparator = acceptsCommaAsJsonKeyValueSeparator;
        DecimalSequences = decimalSequences;
        GroupByColumnIndex = groupByColumnIndex;
        TopInSelect = topInSelect;
        TopInDml = topInDml;
        IdentityDataType = identityDataType;
        SerialDataTypes = serialDataTypes;
        AutoIncrementClause = autoIncrementClause;
        IdentityClause = identityClause;
        DisallowedTypes = disallowedTypes ?? FrozenSet<string>.Empty;
    }

    /// <summary>
    /// The modes, looked up by name in upper case as the reference looks them up.
    /// </summary>
    private static readonly FrozenDictionary<string, Mode> Modes = Build();

    /// <summary>
    /// The mode of the given name, or null if there is no such mode.
    /// </summary>
    /// <param name="name">the name of the mode</param>
    /// <returns>the mode, or null</returns>
    public static Mode? GetInstance(string? name)
    {
        if (name is null)
        {
            return null;
        }

        return Modes.TryGetValue(StringUtils.ToUpperEnglish(name), out Mode? mode) ? mode : null;
    }

    /// <summary>
    /// The mode a database gets when nothing else is asked for.
    /// </summary>
    /// <returns>the regular mode</returns>
    public static Mode GetRegular() => Modes["REGULAR"];

    /// <inheritdoc/>
    public override string ToString() => Name;

    /// <summary>
    /// The reference's own table, one line per mode and one flag per column, in the order the
    /// static initialiser writes them.
    /// </summary>
    /// <returns>the modes by name</returns>
    private static FrozenDictionary<string, Mode> Build()
    {
        Dictionary<string, Mode> modes = new(StringComparer.Ordinal)
        {
            ["REGULAR"] = new(ModeEnum.Regular, false, false, false, true, true, topInSelect: true,
                identityDataType: true, serialDataTypes: true, autoIncrementClause: true),
            ["STRICT"] = new(ModeEnum.Strict, false, false, false, false, false),
            ["LEGACY"] = new(ModeEnum.Legacy, false, false, false, true, true,
                greatestLeastIgnoreNulls: true, topInSelect: true, topInDml: true,
                identityDataType: true, serialDataTypes: true, autoIncrementClause: true,
                identityClause: true),
            ["DB2"] = new(ModeEnum.Db2, false, false, false, true, true),
            ["DERBY"] = new(ModeEnum.Derby, false, false, false, false, false),
            ["HSQLDB"] = new(ModeEnum.HsqlDb, false, false, false, true, true,
                allowPlusForStringConcat: true, topInSelect: true),
            ["MSSQLSERVER"] = new(ModeEnum.MsSqlServer, true, true, true, false, false,
                greatestLeastIgnoreNulls: true, swapLogFunctionParameters: true,
                allowPlusForStringConcat: true, topInSelect: true, topInDml: true,
                identityClause: true),
            ["MARIADB"] = new(ModeEnum.MariaDb, false, true, false, true, false, CharPadding.Never,
                allowUnrelatedOrderByExpressionsInDistinctQueries: true, acceptsCommaAsJsonKeyValueSeparator: true, groupByColumnIndex: true,
                autoIncrementClause: true),
            ["MYSQL"] = new(ModeEnum.MySql, false, true, false, true, false, CharPadding.Never,
                allowUnrelatedOrderByExpressionsInDistinctQueries: true, acceptsCommaAsJsonKeyValueSeparator: true, groupByColumnIndex: true,
                autoIncrementClause: true),
            ["ORACLE"] = new(
                ModeEnum.Oracle, true, false, false, false, true,
                CharPadding.Always, convertOnlyToSmallerScale: true, treatEmptyStringsAsNull: true,
                decimalSequences: true),
            ["POSTGRESQL"] = new(
                ModeEnum.PostgreSql, false, false, false, true, false, CharPadding.InResultSets,
                logIsLogBase10: true, groupByColumnIndex: true, serialDataTypes: true,
                disallowedTypes: new HashSet<string>(StringComparer.Ordinal)
                {
                    "NUMBER", "TINYINT", "BLOB", "VARCHAR_IGNORECASE",
                }),
        };

        return modes.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    /// The name a mode is written with, which is the name of its enum constant, letter for
    /// letter: the reference's enum says <c>REGULAR</c>, <c>Derby</c> and <c>MSSQLServer</c>,
    /// and the SETTINGS row for MODE says <c>REGULAR</c>, so the names come from the enum's
    /// own spellings rather than from a table here.
    /// </summary>
    private static string NameOf(ModeEnum modeEnum) => modeEnum switch
    {
        ModeEnum.Regular => "REGULAR",
        ModeEnum.Strict => "STRICT",
        ModeEnum.Legacy => "LEGACY",
        ModeEnum.Db2 => "DB2",
        ModeEnum.Derby => "Derby",
        ModeEnum.MariaDb => "MariaDB",
        ModeEnum.MsSqlServer => "MSSQLServer",
        ModeEnum.HsqlDb => "HSQLDB",
        ModeEnum.MySql => "MySQL",
        ModeEnum.Oracle => "Oracle",
        ModeEnum.PostgreSql => "PostgreSQL",
        _ => throw new ArgumentOutOfRangeException(nameof(modeEnum)),
    };
}

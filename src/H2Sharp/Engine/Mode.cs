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
/// The compatibility mode a database runs in, which decides how a statement is read rather than
/// what it means.
/// </summary>
/// <remarks>
/// The reference carries 59 flags and this carries the five that the tokenizer reads. They are the
/// five places where the same characters mean different things in different databases, and every
/// other flag belongs to a feature that is not ported yet; a flag nobody reads is a flag nobody
/// can test. The flag arrives with the feature that needs it, the same way
/// <see cref="CastDataProvider"/> carries one member of the reference's five for the same reason.
/// <para>
/// The table below is the reference's own, read off the static initialiser: for each mode, which of
/// the five are on. It is worth having in full even though only the tokenizer reads it, because
/// "which mode turns this on" is the question that every future feature starts with.
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
    /// The name of the mode, as it is written in a URL.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Which of the modes this is.
    /// </summary>
    public ModeEnum Enum { get; }

    private Mode(ModeEnum modeEnum, bool pound, bool zeroExIsBinary, bool squareBrackets, bool limit, bool minusIsExcept)
    {
        Enum = modeEnum;
        Name = NameOf(modeEnum);
        SupportPoundSymbolForColumnNames = pound;
        ZeroExLiteralsAreBinaryStrings = zeroExIsBinary;
        SquareBracketQuotedNames = squareBrackets;
        Limit = limit;
        MinusIsExcept = minusIsExcept;
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
            ["REGULAR"] = new(ModeEnum.Regular, false, false, false, true, true),
            ["STRICT"] = new(ModeEnum.Strict, false, false, false, false, false),
            ["LEGACY"] = new(ModeEnum.Legacy, false, false, false, true, true),
            ["DB2"] = new(ModeEnum.Db2, false, false, false, true, true),
            ["DERBY"] = new(ModeEnum.Derby, false, false, false, false, false),
            ["HSQLDB"] = new(ModeEnum.HsqlDb, false, false, false, true, true),
            ["MSSQLSERVER"] = new(ModeEnum.MsSqlServer, true, true, true, false, false),
            ["MARIADB"] = new(ModeEnum.MariaDb, false, true, false, true, false),
            ["MYSQL"] = new(ModeEnum.MySql, false, true, false, true, false),
            ["ORACLE"] = new(ModeEnum.Oracle, true, false, false, false, true),
            ["POSTGRESQL"] = new(ModeEnum.PostgreSql, false, false, false, true, false),
        };

        return modes.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    /// The name a mode is written with, which is not the same as the name of its constant: the
    /// reference's enum says <c>Derby</c> and <c>MSSQLServer</c>, and a URL says <c>Derby</c> and
    /// <c>MSSQLServer</c> too, so the names come from the enum rather than from a table here.
    /// </summary>
    private static string NameOf(ModeEnum modeEnum) => modeEnum switch
    {
        ModeEnum.Regular => "Regular",
        ModeEnum.Strict => "Strict",
        ModeEnum.Legacy => "Legacy",
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

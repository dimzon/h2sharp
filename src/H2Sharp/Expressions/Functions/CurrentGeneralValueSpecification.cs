/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.CurrentGeneralValueSpecification.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// Simple general value specifications.
/// </summary>
public sealed class CurrentGeneralValueSpecification : Expression
{
    /// <summary>The "CURRENT_CATALOG" general value specification.</summary>
    public const int CurrentCatalog = 0;

    /// <summary>The "CURRENT_PATH" general value specification.</summary>
    public const int CurrentPath = CurrentCatalog + 1;

    /// <summary>The "CURRENT_ROLE" general value specification.</summary>
    public const int CurrentRole = CurrentPath + 1;

    /// <summary>The "CURRENT_SCHEMA" general value specification.</summary>
    public const int CurrentSchema = CurrentRole + 1;

    /// <summary>The "CURRENT_USER" general value specification.</summary>
    public const int CurrentUser = CurrentSchema + 1;

    /// <summary>The "SESSION_USER" general value specification.</summary>
    public const int SessionUser = CurrentUser + 1;

    /// <summary>The "SYSTEM_USER" general value specification.</summary>
    public const int SystemUser = SessionUser + 1;

    /// <summary>
    /// The name of the public role, which is what CURRENT_ROLE answers on a fresh
    /// database. Fixed as a constant: verified against H2 2.5.259 (fresh empty
    /// in-memory database answers 'PUBLIC'), and the port has no roles that could
    /// change it. Approved deviation, see docs/APPROVED_DEVIATIONS.md D-004.
    /// </summary>
    public const string PublicRoleName = "PUBLIC";

    private static readonly string[] Names =
    [
        "CURRENT_CATALOG", "CURRENT_PATH", "CURRENT_ROLE", "CURRENT_SCHEMA",
        "CURRENT_USER", "SESSION_USER", "SYSTEM_USER",
    ];

    private readonly int specification;

    /// <summary>
    /// Creates a general value specification.
    /// </summary>
    /// <param name="specification">one of the constants above</param>
    public CurrentGeneralValueSpecification(int specification)
    {
        this.specification = specification;
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        string? s;
        switch (specification)
        {
            case CurrentCatalog:
                s = session.GetDatabase().GetShortName();
                break;
            case CurrentPath:
            {
                string[]? searchPath = session.GetSchemaSearchPath();
                if (searchPath is not null)
                {
                    StringBuilder builder = new();
                    for (int i = 0; i < searchPath.Length; i++)
                    {
                        if (i > 0)
                        {
                            builder.Append(',');
                        }

                        ParserUtil.QuoteIdentifier(builder, searchPath[i], HasSql.DefaultSqlFlags);
                    }

                    s = builder.ToString();
                }
                else
                {
                    s = string.Empty;
                }

                break;
            }

            case CurrentRole:
                return ValueVarchar.Get(PublicRoleName, session);
            case CurrentSchema:
                s = session.GetCurrentSchemaName();
                break;
            case CurrentUser:
            case SessionUser:
            case SystemUser:
                s = session.GetUserName();
                break;
            default:
                throw DbException.GetInternalError("specification=" + specification);
        }

        return s is not null ? ValueVarchar.Get(s, session) : ValueNull.Instance;
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
        => builder.Append(GetName());

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        if (visitor.GetVisitorType() == ExpressionVisitor.Deterministic)
        {
            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => TypeInfo.TypeVarchar;

    /// <inheritdoc/>
    public override int GetCost() => 1;

    /// <summary>
    /// Gets the name of this specification.
    /// </summary>
    /// <returns>the name</returns>
    public string GetName() => Names[specification];
}

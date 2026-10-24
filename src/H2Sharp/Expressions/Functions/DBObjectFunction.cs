/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.DBObjectFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What is left out, and why:
 *
 * * The admin check. The port has no users, so there is nobody to ask; whoever can run
 *   a statement is the engine's only user, the same rationale as in the SET port.
 * * Object kinds the port cannot hold: DOMAIN, CONSTANT, ROUTINE, SYNONYM, TRIGGER,
 *   ROLE and USER. They fall into the default branch and answer NULL, which is what the
 *   reference answers when the lookup finds nothing — and here there is nothing to find.
 */
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// DB_OBJECT_ID() and DB_OBJECT_SQL() functions.
/// </summary>
public sealed class DBObjectFunction : FunctionN
{
    /// <summary>DB_OBJECT_ID() (non-standard).</summary>
    public const int DbObjectId = 0;

    /// <summary>DB_OBJECT_SQL() (non-standard).</summary>
    public const int DbObjectSql = DbObjectId + 1;

    /// <summary>DB_OBJECT_SIZE() (non-standard).</summary>
    public const int DbObjectSize = DbObjectSql + 1;

    /// <summary>DB_OBJECT_TOTAL_SIZE() (non-standard).</summary>
    public const int DbObjectTotalSize = DbObjectSize + 1;

    /// <summary>DB_OBJECT_APPROXIMATE_SIZE() (non-standard).</summary>
    public const int DbObjectApproximateSize = DbObjectTotalSize + 1;

    /// <summary>DB_OBJECT_APPROXIMATE_TOTAL_SIZE() (non-standard).</summary>
    public const int DbObjectApproximateTotalSize = DbObjectApproximateSize + 1;

    private static readonly string[] Names =
    [
        "DB_OBJECT_ID", "DB_OBJECT_SQL", "DB_OBJECT_SIZE", "DB_OBJECT_TOTAL_SIZE",
        "DB_OBJECT_APPROXIMATE_SIZE", "DB_OBJECT_APPROXIMATE_TOTAL_SIZE",
    ];

    private readonly int function;

    /// <summary>
    /// Creates a database object function.
    /// </summary>
    /// <param name="objectType">the object kind expression</param>
    /// <param name="arg1">the schema name or object name expression</param>
    /// <param name="arg2">the object name expression, or null for the two-argument form</param>
    /// <param name="function">one of the constants above</param>
    public DBObjectFunction(Expression objectType, Expression arg1, Expression? arg2, int function)
        : base(arg2 is null ? [objectType, arg1] : [objectType, arg1, arg2])
    {
        this.function = function;
    }

    /// <inheritdoc/>
    protected override Value GetValue(SessionLocal session, Value? v1, Value? v2, Value? v3)
    {
        string objectType = v1!.GetString()!;
        // Tables live outside the DbObject hierarchy in this port, so the found object
        // travels as plain object and each use below pattern-matches, as follows.
        object? found;
        if (v3 is not null)
        {
            Schemas.Schema? schema = session.GetDatabase().FindSchema(v2!.GetString()!);
            if (schema is null)
            {
                return ValueNull.Instance;
            }

            string objectName = v3.GetString()!;
            switch (objectType)
            {
                case "CONSTRAINT":
                    found = schema.FindConstraint(session, objectName);
                    break;
                case "INDEX":
                    found = schema.FindIndex(session, objectName);
                    break;
                case "SEQUENCE":
                    found = schema.FindSequence(objectName);
                    break;
                case "TABLE":
                    found = schema.FindTableOrView(session, objectName);
                    break;
                default:
                    return ValueNull.Instance;
            }
        }
        else
        {
            string objectName = v2!.GetString()!;
            Database database = session.GetDatabase();
            switch (objectType)
            {
                case "SCHEMA":
                    found = database.FindSchema(objectName);
                    break;
                case "SETTING":
                    found = database.FindSetting(objectName);
                    break;
                default:
                    return ValueNull.Instance;
            }
        }

        if (found is null)
        {
            return ValueNull.Instance;
        }

        switch (function)
        {
            case DbObjectId:
                return ValueInteger.Get(GetId(found));
            case DbObjectSql:
            {
                string? sql = GetCreateSqlForMeta(found);
                return sql is not null ? ValueVarchar.Get(sql) : ValueNull.Instance;
            }

            case DbObjectSize:
                return GetDbObjectSize(found, total: false, approximate: false);
            case DbObjectTotalSize:
                return GetDbObjectSize(found, total: true, approximate: false);
            case DbObjectApproximateSize:
                return GetDbObjectSize(found, total: false, approximate: true);
            case DbObjectApproximateTotalSize:
                return GetDbObjectSize(found, total: true, approximate: true);
            default:
                throw DbException.GetInternalError("function=" + function);
        }
    }

    private static int GetId(object found) => found switch
    {
        Tables.Table table => table.GetId(),
        DbObject obj => obj.GetId(),
        _ => throw DbException.GetInternalError("object=" + found),
    };

    private static string? GetCreateSqlForMeta(object found) => found switch
    {
        Tables.Table table => table.GetCreateSql(),
        DbObject obj => obj.GetCreateSqlForMeta(),
        _ => throw DbException.GetInternalError("object=" + found),
    };

    private static Value GetDbObjectSize(object found, bool total, bool approximate)
    {
        long size = 0L;
        if (found is Tables.Table table)
        {
            size = table.GetDiskSpaceUsed(total, approximate);
        }
        else if (found is Indexes.Index index)
        {
            size = index.GetDiskSpaceUsed(approximate);
        }

        return ValueBigint.Get(size);
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        OptimizeArguments(session, allConst: false);
        type = function switch
        {
            DbObjectId => TypeInfo.TypeInteger,
            DbObjectSql => TypeInfo.TypeVarchar,
            DbObjectSize or DbObjectTotalSize or DbObjectApproximateSize or DbObjectApproximateTotalSize
                => TypeInfo.TypeBigint,
            _ => throw DbException.GetInternalError("function=" + function),
        };
        return this;
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        if (visitor.GetVisitorType() == ExpressionVisitor.Deterministic)
        {
            return false;
        }

        return base.IsEverything(visitor);
    }

    /// <inheritdoc/>
    public override string GetName() => Names[function];
}

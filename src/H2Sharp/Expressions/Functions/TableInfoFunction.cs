/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.TableInfoFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What is left out, and why:
 *
 * The estimated envelope needs a spatial index on the column, and spatial indexes are not
 * ported; without one the answer is null, exactly as in the reference.
 */
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// A table information function.
/// </summary>
public sealed class TableInfoFunction : Function1_2
{
    /// <summary>DISK_SPACE_USED() (non-standard).</summary>
    public const int DiskSpaceUsed = 0;

    /// <summary>ESTIMATED_ENVELOPE().</summary>
    public const int EstimatedEnvelope = DiskSpaceUsed + 1;

    private static readonly string[] Names =
    [
        "DISK_SPACE_USED", "ESTIMATED_ENVELOPE",
    ];

    private readonly int function;

    /// <summary>
    /// Creates a table information function.
    /// </summary>
    /// <param name="arg1">the table name expression</param>
    /// <param name="arg2">the column name expression, or null</param>
    /// <param name="function">one of the constants above</param>
    public TableInfoFunction(Expression arg1, Expression? arg2, int function)
        : base(arg1, arg2)
    {
        this.function = function;
    }

    /// <inheritdoc/>
    protected override Value GetValue(SessionLocal session, Value v1, Value? v2)
    {
        Tables.Table? table = FindTable(session, v1.GetString()!);
        switch (function)
        {
            case DiskSpaceUsed:
                return table is null
                    ? ValueNull.Instance
                    : ValueBigint.Get(table.GetDiskSpaceUsed(total: false, approximate: false));
            case EstimatedEnvelope:
                // Without a spatial index on the column there is no envelope to estimate,
                // which is the reference's answer in that case too.
                return ValueNull.Instance;
            default:
                throw DbException.GetInternalError("function=" + function);
        }
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        left = left.Optimize(session);
        if (right is not null)
        {
            right = right.Optimize(session);
        }

        type = function switch
        {
            DiskSpaceUsed => TypeInfo.TypeBigint,
            EstimatedEnvelope => TypeInfo.TypeGeometry,
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

    private static Tables.Table? FindTable(SessionLocal session, string name)
    {
        int dot = name.IndexOf('.');
        if (dot >= 0)
        {
            Schemas.Schema? schema = session.GetDatabase().FindSchema(name[..dot]);
            return schema?.FindTableOrView(session, name[(dot + 1)..]);
        }

        return session.GetCurrentSchema().FindTableOrView(session, name);
    }
}

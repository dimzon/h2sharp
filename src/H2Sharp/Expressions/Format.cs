/* Copyright 2004-2026 H2 Group. MPL 2.0 or EPL 1.0.
 * Ported from org.h2.expression.Format. Copyright 2026 H2Sharp contributors. */
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Values;

namespace H2Sharp.Expressions;

/// <summary>A format clause such as FORMAT JSON.</summary>
public sealed class Format : Operation1
{
    /// <summary>Supported formats.</summary>
    public enum FormatEnum
    {
        /// <summary>JSON.</summary>
        Json,
    }

    private readonly FormatEnum format;

    /// <summary>Creates the format clause.</summary>
    /// <param name="arg">the argument</param>
    /// <param name="format">the format</param>
    public Format(Expression arg, FormatEnum format) : base(arg) => this.format = format;

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session) => GetValue(arg.GetValue(session));

    /// <summary>Returns the value with the format applied.</summary>
    /// <param name="value">the value</param>
    /// <returns>the formatted value</returns>
    public Value GetValue(Value value) => ApplyJson(value);

    /// <summary>Applies JSON format using the existing JSON value conversions.</summary>
    /// <param name="value">the value</param>
    /// <returns>the formatted value</returns>
    public static Value ApplyJson(Value value) => value.GetValueType() switch
    {
        Value.Null => ValueNull.Instance,
        Value.Varchar or Value.VarcharIgnorecase or Value.Char or Value.Clob => ValueJson.FromJson(value.GetString()!),
        _ => value.ConvertToJson(TypeInfo.TypeJson, Value.ConvertTo, null),
    };

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        arg = arg.Optimize(session);
        if (arg.IsConstant()) return ValueExpression.Get(GetValue(session));
        if (arg is Format other && format == other.format) return arg;
        type = TypeInfo.TypeJson;
        return this;
    }

    /// <inheritdoc/>
    public override bool IsIdentity() => arg.IsIdentity();

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
        => arg.GetSql(builder, sqlFlags, AutoParentheses).Append(" FORMAT JSON");

    /// <inheritdoc/>
    public override int GetNullable() => arg.GetNullable();

    /// <inheritdoc/>
    public override string? GetTableName() => arg.GetTableName();

    /// <inheritdoc/>
    public override string GetColumnName(SessionLocal session, int columnIndex) => arg.GetColumnName(session, columnIndex);
}

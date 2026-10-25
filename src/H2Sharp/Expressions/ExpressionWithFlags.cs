/* Copyright 2004-2026 H2 Group. MPL 2.0 or EPL 1.0.
 * Ported from org.h2.expression.ExpressionWithFlags. Copyright 2026 H2Sharp contributors. */
namespace H2Sharp.Expressions;

/// <summary>An expression with flags.</summary>
public interface ExpressionWithFlags
{
    /// <summary>Sets the flags.</summary>
    /// <param name="flags">the flags</param>
    void SetFlags(int flags);

    /// <summary>Returns the flags.</summary>
    /// <returns>the flags</returns>
    int GetFlags();
}

using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Aggregate;

///
/// Additional arguments of LISTAGG aggregate function.
///
public sealed class ListaggArguments
{
    private Expression? _separator;

    private bool _onOverflowTruncate;

    private Expression? _filter;

    private bool _withoutCount;

    ///
    /// Sets the custom LISTAGG separator.
    ///
    /// <param name="separator">the LISTAGG separator, null or empty string means no separator</param>
    public void SetSeparator(Expression separator) => _separator = separator;

    ///
    /// Returns the LISTAGG separator.
    ///
    /// <returns>the LISTAGG separator, null means the default</returns>
    public Expression? GetSeparator() => _separator;

    ///
    /// Returns the effective LISTAGG separator.
    ///
    /// <param name="session">the session to evaluate a constant separator on</param>
    /// <returns>the effective LISTAGG separator</returns>
    public string GetEffectiveSeparator(SessionLocal session)
    {
        if (_separator is not null)
        {
            // The reference evaluates with a null session; the reader only accepts
            // constant separators, which evaluate the same on any session, and a
            // null here would only trade one failure for another.
            string? s = _separator.GetValue(session)?.GetString();
            return s ?? string.Empty;
        }

        return ",";
    }

    ///
    /// Sets the LISTAGG overflow behavior.
    ///
    /// <param name="onOverflowTruncate">true for ON OVERFLOW TRUNCATE, false for ON OVERFLOW ERROR</param>
    public void SetOnOverflowTruncate(bool onOverflowTruncate) => _onOverflowTruncate = onOverflowTruncate;

    ///
    /// Returns the LISTAGG overflow behavior.
    ///
    /// <returns>true for ON OVERFLOW TRUNCATE, false for ON OVERFLOW ERROR</returns>
    public bool GetOnOverflowTruncate() => _onOverflowTruncate;

    ///
    /// Sets the custom LISTAGG truncation filter.
    ///
    /// <param name="filter">the LISTAGG truncation filter, null or empty string means no truncation filter</param>
    public void SetFilter(Expression filter) => _filter = filter;

    ///
    /// Returns the LISTAGG truncation filter.
    ///
    /// <returns>the LISTAGG truncation filter, null means the default</returns>
    public Expression? GetFilter() => _filter;

    ///
    /// Returns the effective LISTAGG truncation filter.
    ///
    /// <param name="session">the session to evaluate a constant filter on</param>
    /// <returns>the effective LISTAGG truncation filter</returns>
    public string GetEffectiveFilter(SessionLocal session)
    {
        if (_filter is not null)
        {
            string? f = _filter.GetValue(session)?.GetString();
            return f ?? string.Empty;
        }

        return "...";
    }

    ///
    /// Sets the LISTAGG count indication.
    ///
    /// <param name="withoutCount">true for WITHOUT COUNT, false for WITH COUNT</param>
    public void SetWithoutCount(bool withoutCount) => _withoutCount = withoutCount;

    ///
    /// Returns the LISTAGG count indication.
    ///
    /// <returns>true for WITHOUT COUNT, false for WITH COUNT</returns>
    public bool IsWithoutCount() => _withoutCount;
}

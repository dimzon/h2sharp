using System.Text;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Expression;

/// <summary>
/// A parameter of a prepared statement, as the tracing layer sees it.
/// </summary>
/// <remarks>
/// The reference's <c>Trace.formatParams</c> needs exactly these answers and no more: whether a value
/// was set, what it is, what type it declares, and whether it may be null. Porting the interface is
/// worth doing even though its implementations come later, because it is the only bridge between
/// tracing and parameters and it says precisely what tracing is allowed to know.
/// </remarks>
public interface ParameterInterface
{
    /// <summary>
    /// Sets the value.
    /// </summary>
    /// <param name="value">the value</param>
    /// <param name="closeOld">whether to close the value that was there</param>
    void SetValue(Value value, bool closeOld);

    /// <summary>
    /// The value.
    /// </summary>
    /// <returns>the value</returns>
    Value GetParamValue();

    /// <summary>
    /// Confirms that a value was set, refusing otherwise.
    /// </summary>
    /// <exception cref="H2SharpException">no value was set</exception>
    void CheckSet();

    /// <summary>
    /// Whether a value was set.
    /// </summary>
    /// <returns>true when set</returns>
    bool IsValueSet();

    /// <summary>
    /// The declared type.
    /// </summary>
    /// <returns>the type</returns>
    TypeInfo GetType();

    /// <summary>
    /// Whether the parameter may be null.
    /// </summary>
    /// <returns>the nullability code</returns>
    int GetNullable();
}

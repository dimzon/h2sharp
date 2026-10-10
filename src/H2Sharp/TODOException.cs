using System;

namespace H2Sharp;

/// <summary>
/// A stub that is reached at runtime: the port has not gotten here yet.
/// </summary>
/// <remarks>
/// Thrown by scaffolding that exists only so a half-ported tree compiles — a stub type, an
/// unported method, a branch for a case the core does not reach yet. It is deliberately distinct
/// from <see cref="NotImplementedException"/> so that unfinished port work is searchable: every
/// <c>TODOException</c> in the tree is a porting debt, not a design decision.
///
/// It sits in the root namespace rather than in a layer's own because it is a statement about
/// the port as a whole: an ADO.NET connection that refuses to open owes that refusal to the
/// same unfinished work as an MVStore chunk, and neither layer should have to reference the
/// other to say so.
/// </remarks>
public sealed class TODOException : Exception
{
    /// <summary>
    /// An unfinished corner of the port.
    /// </summary>
    public TODOException()
        : base("Not ported yet")
    {
    }

    /// <summary>
    /// An unfinished corner of the port, named.
    /// </summary>
    /// <param name="what">what is not ported yet</param>
    public TODOException(string what)
        : base("Not ported yet: " + what)
    {
    }
}

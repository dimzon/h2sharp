using System;

namespace H2Sharp.MvStore;

/// <summary>
/// Various kinds of MVStore problems, along with associated error code.
/// </summary>
public class MvStoreException : Exception
{
    private readonly int errorCode;

    /// <summary>
    /// A store problem with the code that names it.
    /// </summary>
    /// <param name="errorCode">the error code</param>
    /// <param name="message">the message</param>
    public MvStoreException(int errorCode, string message)
        : base(message) => this.errorCode = errorCode;

    /// <summary>
    /// The error code that names this problem.
    /// </summary>
    public int GetErrorCode() => errorCode;
}

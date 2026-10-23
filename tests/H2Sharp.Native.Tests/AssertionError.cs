// Copyright 2026 H2Sharp contributors.
// Ported from java.lang.AssertionError (no Java source file in the reference tree).
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified: the reference's TestBase.fail() throws java.lang.AssertionError, which the
// runtime provides. .NET has no such type, and inventing a different failure channel
// would hide failures from the runner. The type keeps the name and the behavior the
// tests rely on: carrying the message, and failing the test by being thrown.
using System;

namespace H2Sharp.Native.Tests;

/// <summary>
/// The exception <see cref="TestBase"/> throws when a check fails. The mapping of
/// java.lang.AssertionError.
/// </summary>
public class AssertionError : Exception
{
    /// <summary>
    /// Builds the error with a message.
    /// </summary>
    /// <param name="message">the detail message, or <c>null</c> for none</param>
    public AssertionError(string? message) : base(message ?? "Assertion failed")
    {
    }

    /// <summary>
    /// Builds the error with a message and the exception that caused it, the way the
    /// reference's AssertionError(message, cause) does.
    /// </summary>
    /// <param name="message">the detail message, or <c>null</c> for none</param>
    /// <param name="cause">the cause of the failure</param>
    public AssertionError(string? message, Exception cause)
        : base(message ?? "Assertion failed", cause)
    {
    }

    /// <summary>
    /// Builds the error without a message.
    /// </summary>
    public AssertionError() : this(null)
    {
    }
}

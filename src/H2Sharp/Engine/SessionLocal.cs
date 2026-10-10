/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.engine.SessionLocal.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A stub, deliberately: this is the type every part of the engine passes around — expressions
 * take it to read the current timestamp and the identity, tables take it to add rows, the
 * parser takes it to prepare commands — so nothing above it can be ported until this type
 * exists, and nothing in it can be ported until the layers above exist. It is created as an
 * empty shell so the port can proceed from the leaves upward, and every member will be filled
 * in as the classes it delegates to arrive.
 *
 * No member invents behaviour. A session that answered "who am I" or "what is the current
 * timestamp" without the database behind it would produce a plausible wrong answer in the one
 * place where a wrong answer is hardest to notice: a row written with the wrong identity looks
 * exactly like a row written with the right one.
 */
namespace H2Sharp.Engine;

/// <summary>
/// A session in a local (in-process) database.
/// </summary>
/// <remarks>
/// The reference declares a Session interface and SessionLocal as its embedded implementation.
/// Only the implementation is sketched here; the interface comes with it and is ported in the
/// same block, because a type that half of the tree can call is a type whose contract nobody
/// has stated.
/// </remarks>
public abstract class SessionLocal
{
}

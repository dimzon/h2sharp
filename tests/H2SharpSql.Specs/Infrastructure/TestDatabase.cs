// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// The only way a test gets at the database. Every conformance test opens its connection
// through here and never through a provider type directly, so that the whole suite keeps
// working when the real connection is wired up and the suite itself never has to change.
//
// Nothing here talks to the provider's own members. Dapper is given an IDbConnection and
// that is the entire surface it needs, so the suite is shaped like a real micro-ORM rather
// than like the provider's test suite.
//
// THE CONTRACT:
//
//     Open() returns an OPEN connection to a NEW, EMPTY in-memory database, unique per
//     call, and the caller disposes it. Two calls never see each other's tables, rows or
//     settings, and nothing survives the call.
//
// A fresh GUID in the name is what implements the isolation: an in-memory database lives
// as long as a connection to it is open, so a unique name per call is a fresh database.
using System;
using System.Data;
using H2Sharp.Data;
using H2Sharp.Crypto;
using H2Sharp.Hashes;
using H2Sharp.JavaLegacy;

namespace H2SharpSql.Specs.Infrastructure;

internal static class TestDatabase
{
    /// <summary>
    /// Opens a fresh, empty, unique in-memory database and returns the open connection.
    /// </summary>
    /// <remarks>
    /// The returned connection is owned by the caller and should be disposed by it,
    /// normally through <c>using</c>. Isolation between calls is what makes a conformance
    /// test independent of every other one: a test that creates a table must not find it
    /// already there, whatever ran before it or whatever runs after it.
    /// </remarks>
    public static IDbConnection Open()
    {
        H2Connection connection = new($"jdbc:h2:mem:{Guid.NewGuid():N}");
        ((IH2Extensions)connection).UseJavaLegacy();
        ((IH2Extensions)connection).UseH2Hashes();
        ((IH2Extensions)connection).UseCrypto();
        connection.Open();
        return connection;
    }
}

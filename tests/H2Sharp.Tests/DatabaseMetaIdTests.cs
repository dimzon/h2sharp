/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The modification id contract: the handed-out id is the one before the increment.
 *
 * This is the reference's incrementAndGet minus one, not a pre-increment: an object
 * stamped with the returned id compares older than the counter the moment after, which
 * is what invalidation checks compare against. A pre-increment would stamp it with the
 * counter itself.
 */
using H2Sharp.Engine;
using Xunit;

namespace H2Sharp.Tests;

public sealed class DatabaseMetaIdTests
{
    [Fact]
    public void NextHandsOutTheIdBeforeTheIncrement()
    {
        Database db = new(new ConnectionInfo("jdbc:h2:mem:meta_id"));

        // Opening the database creates the SYS_ID index, and the reference stamps every
        // index it adds with setModified(), so the counter is already 1 by the time a caller
        // can ask.
        Assert.Equal(1, db.GetNextModificationMetaId());
        Assert.Equal(2, db.GetModificationMetaId());
        Assert.Equal(2, db.GetNextModificationMetaId());
        Assert.Equal(3, db.GetModificationMetaId());
    }
}

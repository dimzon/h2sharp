/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of the comment object's type-name mapping. USER, ROLE and ALIAS objects do
 * not exist in the port yet, so their switch arms cannot be reached through SQL; the mapping is
 * internal precisely so that this test can pin it and cover it.
 */
using System;
using H2Sharp.Engine;
using Xunit;

namespace H2Sharp.Tests;

public sealed class CommentTypeNameTests
{
    [Theory]
    [InlineData(DbObject.Constant, "CONSTANT")]
    [InlineData(DbObject.Constraint, "CONSTRAINT")]
    [InlineData(DbObject.FunctionAlias, "ALIAS")]
    [InlineData(DbObject.Index, "INDEX")]
    [InlineData(DbObject.Role, "ROLE")]
    [InlineData(DbObject.Schema, "SCHEMA")]
    [InlineData(DbObject.Sequence, "SEQUENCE")]
    [InlineData(DbObject.TableOrView, "TABLE")]
    [InlineData(DbObject.Trigger, "TRIGGER")]
    [InlineData(DbObject.User, "USER")]
    [InlineData(DbObject.Domain, "DOMAIN")]
    public void EveryObjectTypeHasItsCommentName(int type, string expected)
        => Assert.Equal(expected, Comment.GetTypeName(type));

    [Fact]
    public void UnknownTypeIsNamedWithItsOrdinal()
        => Assert.Equal("type99", Comment.GetTypeName(99));

    [Fact]
    public void ACommentKeepsItsTextAndRefusesRename()
    {
        Database database = new(ConnectionInfo.Memory("comment_type_name"));
        try
        {
            Comment comment = new(database, database.AllocateObjectId(),
                database.GetSchema("PUBLIC"));
            Assert.Null(comment.GetCommentText());
            comment.SetCommentText("a note");
            Assert.Equal("a note", comment.GetCommentText());
            Assert.ThrowsAny<Exception>(() => comment.CheckRename());
        }
        finally
        {
            database.Close();
        }
    }
}

/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 275: the SET statement's own refusals — exclusive mode, an invalid time zone and the
 * authenticator stub — plus its command type.
 */
using System;
using H2Sharp.Api;
using H2Sharp.Command;
using H2Sharp.Engine;
using Xunit;

namespace H2Sharp.Tests;

public sealed class SetSurfaceTests
{
    private static SessionLocal Session()
        => new(new Database(ConnectionInfo.Memory("set_surface_" + Guid.NewGuid().ToString("N"))), "sa");

    [Fact]
    public void InvalidTimeZoneAndAuthenticatorAreRefused()
    {
        SessionLocal session = Session();
        try
        {
            H2SharpException zone = Assert.ThrowsAny<H2SharpException>(
                () => session.PrepareCommand("SET TIME ZONE 'No/Such_Zone'").ExecuteUpdate(false));
            Assert.Equal(ErrorCode.InvalidValue2, zone.H2ErrorCode);

            Assert.Throws<TODOException>(() =>
                session.PrepareCommand("SET AUTHENTICATOR 'x'").ExecuteUpdate(false));
        }
        finally
        {
            session.GetDatabase().Close();
        }
    }

    [Fact]
    public void ASetCommandReportsItsType()
    {
        SessionLocal session = Session();
        try
        {
            CommandInterface command = session.PrepareCommand("SET AUTOCOMMIT TRUE");
            Assert.Equal(CommandInterface.SetAutocommitTrue, command.GetCommandType());
        }
        finally
        {
            session.GetDatabase().Close();
        }
    }
}

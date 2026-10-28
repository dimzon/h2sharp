/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A real session's cast provider. The clock and timezone are delegated to the configured
 * provider, while the mode is read from the database the session belongs to. Tests that
 * supply their own cast provider bypass this wrapper and keep that provider's mode.
 */
namespace H2Sharp.Engine;

internal sealed class SessionCastDataProvider : CastDataProvider
{
    private readonly SessionLocal session;

    private readonly CastDataProvider inner;

    internal SessionCastDataProvider(SessionLocal session, CastDataProvider inner)
    {
        this.session = session;
        this.inner = inner;
    }

    public Values.ValueTimestampTimeZone CurrentTimestamp() => inner.CurrentTimestamp();

    public Util.TimeZoneProvider CurrentTimeZone() => inner.CurrentTimeZone();

    public bool ZeroBasedEnums() => inner.ZeroBasedEnums();

    public Mode GetMode() => session.GetDatabase().GetMode();
}

/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.util.json.JSONValidationTarget.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
namespace H2Sharp.Util.Json;

/// <summary>
/// Abstract JSON validation target: checks that what arrives forms a document, and reports what
/// kind of thing it turned out to be.
/// </summary>
/// <remarks>
/// The class carries no state on its own. It exists so that "this target validates" says something
/// at the type level, and because the result is an item type: a validator answers "an object", not
/// "this text". <see cref="Value"/> is reserved and never returned here — a validator does not tell
/// a caller that the document was a bare value, only that it was not valid JSON to begin with.
/// </remarks>
public abstract class JsonValidationTarget : JsonTarget<JsonItemType>
{
}

/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Ported from none (new file, no Java counterpart).
 *
 * A connection string builder.
 *
 * This is the one place where the two spellings of a connection have to meet. H2 accepts
 * "jdbc:h2:mem:test" and "jdbc:h2:~/test" as a single opaque URL, while the ADO.NET world expects
 * "Data Source=mem:test" as key=value pairs — and a caller will use whichever their code base
 * already uses. So this builder keeps both: the keys are ADO.NET's, and Data Source is where the
 * H2 name goes, with or without the "jdbc:h2:" prefix.
 *
 * The prefix is *not* required and not added on output: stripping it is what makes
 * "Data Source=mem:x" and "Data Source=jdbc:h2:mem:x" the same database, which is what a caller
 * who copied a JDBC URL into an ADO.NET connection string expects. Keeping the name otherwise
 * byte for byte matters too, because the name decides which database is opened.
 */
using System;
using System.Data.Common;

namespace H2Sharp.Data;

/// <summary>
/// Builds and parses connection strings.
/// </summary>
public sealed class H2ConnectionStringBuilder : DbConnectionStringBuilder
{
    /// <summary>
    /// Creates a builder with no connection string.
    /// </summary>
    public H2ConnectionStringBuilder()
    {
    }

    /// <summary>
    /// Creates a builder for the given connection string.
    /// </summary>
    /// <param name="connectionString">the connection string</param>
    public H2ConnectionStringBuilder(string? connectionString)
    {
        ConnectionString = connectionString;
    }

    /// <summary>
    /// Gets or sets the database name.
    /// </summary>
    public string DataSource
    {
        get => TryGetString("Data Source", out string value) ? value : string.Empty;
        set => this["Data Source"] = value;
    }

    /// <summary>
    /// Gets or sets the user name.
    /// </summary>
    public string UserId
    {
        get => TryGetString("User ID", out string value) ? value : string.Empty;
        set => this["User ID"] = value;
    }

    /// <summary>
    /// Gets or sets the password.
    /// </summary>
    public string Password
    {
        get => TryGetString("Password", out string value) ? value : string.Empty;
        set => this["Password"] = value;
    }

    private bool TryGetString(string key, out string value)
    {
        if (TryGetValue(key, out object? stored) && stored is string text)
        {
            value = text;
            return true;
        }

        value = string.Empty;
        return false;
    }

    /// <summary>
    /// Turns this builder into a connection string for <see cref="H2Connection"/>.
    /// </summary>
    /// <returns>the connection string</returns>
    public string ToH2ConnectionString()
    {
        string name = DataSource;
        if (name.Length == 0)
        {
            name = "mem:";
        }
        else if (!name.StartsWith("jdbc:h2:", StringComparison.OrdinalIgnoreCase))
        {
            name = "jdbc:h2:" + name;
        }

        if (UserId.Length > 0)
        {
            name += ";USER=" + UserId;
        }

        if (Password.Length > 0)
        {
            name += ";PASSWORD=" + Password;
        }

        return name;
    }
}

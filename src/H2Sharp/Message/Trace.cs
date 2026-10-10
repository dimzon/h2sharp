using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Expression;
using H2Sharp.Util;

namespace H2Sharp.Message;

/// <summary>
/// One module's view of the trace: whether its lines go anywhere, and the lines themselves.
/// </summary>
/// <remarks>
/// A module asks its own trace whether a level is on, so that the hot path does not format a message
/// nobody will read. The check asks the parent writer when this trace was told to follow it, and reads
/// its own level otherwise. That is why the trace has both: a module that opted out still wants the
/// answer for everything above its own cutoff.
/// <para>
/// Messages are formatted with the same subset of <c>MessageFormat</c> the error catalogue uses, which
/// is what the reference formats them with. A parameter list goes behind the SQL in the comment that
/// says how it starts, so that knowing the size of the statement is enough to separate the two
/// without parsing.
/// </para>
/// </remarks>
public sealed class Trace
{
    /// <summary>
    /// A command module.
    /// </summary>
    public const int Command = 0;

    /// <summary>
    /// A constraint module.
    /// </summary>
    public const int Constraint = 1;

    /// <summary>
    /// The database module.
    /// </summary>
    public const int Database = 2;

    /// <summary>
    /// A function module.
    /// </summary>
    public const int Function = 3;

    /// <summary>
    /// A file lock module.
    /// </summary>
    public const int FileLock = 4;

    /// <summary>
    /// An index module.
    /// </summary>
    public const int Index = 5;

    /// <summary>
    /// A JDBC module.
    /// </summary>
    public const int Jdbc = 6;

    /// <summary>
    /// A lock module.
    /// </summary>
    public const int Lock = 7;

    /// <summary>
    /// A schema module.
    /// </summary>
    public const int Schema = 8;

    /// <summary>
    /// A sequence module.
    /// </summary>
    public const int Sequence = 9;

    /// <summary>
    /// A setting module.
    /// </summary>
    public const int Setting = 10;

    /// <summary>
    /// A table module.
    /// </summary>
    public const int Table = 11;

    /// <summary>
    /// A trigger module.
    /// </summary>
    public const int Trigger = 12;

    /// <summary>
    /// A user module.
    /// </summary>
    public const int User = 13;

    /// <summary>
    /// A JDBCX module.
    /// </summary>
    public const int Jdbcx = 14;

    /// <summary>
    /// The module names, in module id order.
    /// </summary>
    internal static readonly string[] ModuleNames =
    [
        "command", "constraint", "database", "function", "fileLock", "index", "jdbc", "lock",
        "schema", "sequence", "setting", "table", "trigger", "user", "JDBCX",
    ];

    private readonly TraceWriter traceWriter;

    private readonly string module;

    private readonly string lineSeparator = Environment.NewLine;

    private int traceLevel = TraceLevels.Parent;

    /// <summary>
    /// The trace for a module id, writing wherever the writer writes.
    /// </summary>
    /// <param name="traceWriter">where the lines go</param>
    /// <param name="moduleId">which module</param>
    internal Trace(TraceWriter traceWriter, int moduleId)
        : this(traceWriter, ModuleNames[moduleId])
    {
    }

    /// <summary>
    /// The trace for a named module, writing wherever the writer writes.
    /// </summary>
    /// <param name="traceWriter">where the lines go</param>
    /// <param name="module">which module</param>
    internal Trace(TraceWriter traceWriter, string module)
    {
        this.traceWriter = traceWriter;
        this.module = module;
    }

    /// <summary>
    /// Sets how much this module traces, where more means more.
    /// </summary>
    /// <param name="level">the level</param>
    public void SetLevel(int level) => traceLevel = level;

    /// <summary>
    /// Whether information lines are written.
    /// </summary>
    /// <returns>true when they are</returns>
    public bool IsInfoEnabled() => IsEnabled(TraceLevels.Info);

    /// <summary>
    /// Whether debugging lines are written.
    /// </summary>
    /// <returns>true when they are</returns>
    public bool IsDebugEnabled() => IsEnabled(TraceLevels.Debug);

    /// <summary>
    /// Whether a level is written for this module.
    /// </summary>
    private bool IsEnabled(int level)
        => traceLevel == TraceLevels.Parent ? traceWriter.IsEnabled(level) : level <= traceLevel;

    /// <summary>
    /// Writes an error.
    /// </summary>
    /// <param name="t">the exception</param>
    /// <param name="s">the message</param>
    public void Error(Exception t, string s)
    {
        if (IsEnabled(TraceLevels.Error))
        {
            traceWriter.Write(TraceLevels.Error, module, s, t);
        }
    }

    /// <summary>
    /// Writes a formatted error.
    /// </summary>
    /// <param name="t">the exception</param>
    /// <param name="s">the pattern</param>
    /// <param name="parameters">the message arguments</param>
    public void Error(Exception t, string s, params object?[] parameters)
    {
        if (IsEnabled(TraceLevels.Error))
        {
            traceWriter.Write(TraceLevels.Error, module, ErrorMessages.Format(s, parameters), t);
        }
    }

    /// <summary>
    /// Writes an informational line.
    /// </summary>
    /// <param name="s">the line</param>
    public void Info(string s)
    {
        if (IsEnabled(TraceLevels.Info))
        {
            traceWriter.Write(TraceLevels.Info, module, s, null);
        }
    }

    /// <summary>
    /// Writes a formatted informational line.
    /// </summary>
    /// <param name="s">the pattern</param>
    /// <param name="parameters">the message arguments</param>
    public void Info(string s, params object?[] parameters)
    {
        if (IsEnabled(TraceLevels.Info))
        {
            traceWriter.Write(TraceLevels.Info, module, ErrorMessages.Format(s, parameters), null);
        }
    }

    /// <summary>
    /// Writes an informational line about an exception.
    /// </summary>
    /// <param name="t">the exception</param>
    /// <param name="s">the line</param>
    internal void Info(Exception t, string s)
    {
        if (IsEnabled(TraceLevels.Info))
        {
            traceWriter.Write(TraceLevels.Info, module, s, t);
        }
    }

    /// <summary>
    /// The parameters behind a statement, in the form the trace comment uses.
    /// </summary>
    /// <param name="parameters">the parameters</param>
    /// <returns>the text, or empty when there is nothing set</returns>
    public static string FormatParams(IReadOnlyList<ParameterInterface> parameters)
    {
        if (parameters.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new();
        int i = 0;
        foreach (ParameterInterface p in parameters)
        {
            if (p.IsValueSet())
            {
                _ = builder.Append(i == 0 ? " {" : ", ").Append(++i).Append(": ")
                    .Append(p.GetParamValue().GetTraceSql());
            }
        }

        if (i != 0)
        {
            _ = builder.Append('}');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Writes a statement and its parameters in the comment the trace file expects.
    /// </summary>
    /// <param name="sql">the statement</param>
    /// <param name="parameters">the parameters, already formatted</param>
    /// <param name="count">how many rows</param>
    /// <param name="time">how many milliseconds</param>
    public void InfoSql(string sql, string parameters, long count, long time)
    {
        if (!IsEnabled(TraceLevels.Info))
        {
            return;
        }

        StringBuilder buff = new(sql.Length + parameters.Length + 20);
        _ = buff.Append(lineSeparator).Append("/*SQL");
        bool space = false;
        if (parameters.Length > 0)
        {
            // This looks like a bug, but it is intentional:
            // If there are no parameters, the SQL statement is
            // the rest of the line. If there are parameters, they
            // are appended at the end of the line. Knowing the size
            // of the statement simplifies separating the SQL statement
            // from the parameters (no need to parse).
            space = true;
            _ = buff.Append(" l:").Append(sql.Length);
        }

        if (count > 0)
        {
            space = true;
            _ = buff.Append(" #:").Append(count);
        }

        if (time > 0)
        {
            space = true;
            _ = buff.Append(" t:").Append(time);
        }

        if (!space)
        {
            _ = buff.Append(' ');
        }

        _ = buff.Append("*/");
        StringUtils.JavaEncode(sql, buff, false);
        StringUtils.JavaEncode(parameters, buff, false);
        _ = buff.Append(';');
        traceWriter.Write(TraceLevels.Info, module, buff.ToString(), null);
    }

    /// <summary>
    /// Writes a formatted debugging line.
    /// </summary>
    /// <param name="s">the pattern</param>
    /// <param name="parameters">the message arguments</param>
    public void Debug(string s, params object?[] parameters)
    {
        if (IsEnabled(TraceLevels.Debug))
        {
            traceWriter.Write(TraceLevels.Debug, module, ErrorMessages.Format(s, parameters), null);
        }
    }

    /// <summary>
    /// Writes a debugging line.
    /// </summary>
    /// <param name="s">the line</param>
    public void Debug(string s)
    {
        if (IsEnabled(TraceLevels.Debug))
        {
            traceWriter.Write(TraceLevels.Debug, module, s, null);
        }
    }

    /// <summary>
    /// Writes a debugging line about an exception.
    /// </summary>
    /// <param name="t">the exception</param>
    /// <param name="s">the line</param>
    public void Debug(Exception t, string s)
    {
        if (IsEnabled(TraceLevels.Debug))
        {
            traceWriter.Write(TraceLevels.Debug, module, s, t);
        }
    }

    /// <summary>
    /// Writes a fragment of code into the trace.
    /// </summary>
    /// <param name="java">the fragment</param>
    public void InfoCode(string java)
    {
        if (IsEnabled(TraceLevels.Info))
        {
            traceWriter.Write(TraceLevels.Info, module, lineSeparator + "/**/" + java, null);
        }
    }

    /// <summary>
    /// Writes a fragment of code into the trace when debugging.
    /// </summary>
    /// <param name="java">the fragment</param>
    internal void DebugCode(string java)
    {
        if (IsEnabled(TraceLevels.Debug))
        {
            traceWriter.Write(TraceLevels.Debug, module, lineSeparator + "/**/" + java, null);
        }
    }
}

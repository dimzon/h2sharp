/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0 (see LICENSE-H2.txt).
 *
 * Ported from org.h2.command.dml.ScriptBase (the input path used by RUNSCRIPT).
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The output path, compression and encryption of the reference's base belong to SCRIPT and are
 * not part of this class: SCRIPT has its own committed implementation in the port, and the
 * cipher modes are an approved deviation (D-026).
 */
using System;
using System.IO;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;

namespace H2Sharp.Command.Dml;

/// <summary>
/// The base for the commands that read an SQL script from a file.
/// </summary>
public abstract class ScriptBase : Prepared
{
    /// <summary>The default name of the script file.</summary>
    protected const string ScriptSql = "script.sql";

    /// <summary>The reader of the input file.</summary>
    protected TextReader? Reader;

    private Expression? fileNameExpr;

    private string? fileName;

    private Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Creates the command.</summary>
    /// <param name="session">the session</param>
    protected ScriptBase(SessionLocal session) : base(session)
    {
    }

    /// <summary>Sets the expression of the file name.</summary>
    /// <param name="file">the file name expression</param>
    public void SetFileNameExpr(Expression file) => fileNameExpr = file;

    /// <summary>Gets the resolved file name.</summary>
    /// <returns>the name, or null</returns>
    protected string? GetFileName()
    {
        if (fileNameExpr is not null && fileName is null)
        {
            string? value = fileNameExpr.Optimize(GetSession()).GetValue(GetSession()).GetString();
            fileName = string.IsNullOrWhiteSpace(value) ? ScriptSql : value;
        }

        return fileName;
    }

    /// <inheritdoc/>
    public override bool IsTransactional() => false;

    /// <inheritdoc/>
    public override bool NeedRecompile() => false;

    /// <summary>Sets the compression algorithm; the reference's ZIP/KANZI streams are not ported.</summary>
    /// <param name="algorithm">the algorithm</param>
    public void SetCompressionAlgorithm(string algorithm)
        => throw new TODOException(
            "ScriptBase.setCompressionAlgorithm: script compression is not ported (" + algorithm + ")");

    /// <summary>Sets the cipher; encrypted scripts are not ported (D-026).</summary>
    /// <param name="cipher">the cipher</param>
    public void SetCipher(string cipher)
        => throw new TODOException(
            "ScriptBase.setCipher: encrypted scripts are not ported (D-026)");

    /// <summary>Sets the password; encrypted scripts are not ported (D-026).</summary>
    /// <param name="password">the password expression</param>
    public void SetPassword(Expression password)
        => throw new TODOException(
            "ScriptBase.setPassword: encrypted scripts are not ported (D-026)");

    /// <summary>Sets the charset of the input file.</summary>
    /// <param name="charset">the charset name</param>
    public void SetCharset(string charset) => encoding = ParseCharset(charset);

    /// <summary>
    /// The charset names the reference accepts, resolved the same way its
    /// <c>ScriptCommand</c> sibling resolves them.
    /// </summary>
    /// <param name="name">the charset name</param>
    /// <returns>the encoding</returns>
    private static Encoding ParseCharset(string name)
    {
        if (string.Equals(name, "UTF-8", StringComparison.OrdinalIgnoreCase))
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }

        if (string.Equals(name, "UTF-16", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "UNICODE", StringComparison.OrdinalIgnoreCase))
        {
            return new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        }

        return Encoding.GetEncoding(name);
    }

    /// <summary>Opens the input stream.</summary>
    /// <param name="charset">the charset, or null for the default</param>
    protected void OpenInput(Encoding? charset = null)
    {
        string? file = GetFileName();
        if (file is null)
        {
            return;
        }

        try
        {
            Reader = new StreamReader(
                new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read),
                charset ?? encoding,
                detectEncodingFromByteOrderMarks: true);
        }
        catch (IOException e)
        {
            throw DbException.Get(Api.ErrorCode.IoException1, e, file);
        }
    }

    /// <summary>Closes the input stream.</summary>
    protected void CloseIO()
    {
        Reader?.Dispose();
        Reader = null;
    }
}

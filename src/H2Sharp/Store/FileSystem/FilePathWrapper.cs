/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.store.fs.FilePathWrapper.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Generic;

namespace H2Sharp.Store.FileSystem;

/// <summary>
/// A path that stands for another one: the wrapper answers in its own scheme and passes the work on.
/// </summary>
/// <remarks>
/// Two things in the reference are Java-isms and are not here. It creates the wrapper through
/// reflection on a private constructor, which .NET does not need: a subclass is created by a factory
/// method instead. And it keeps the base path in a field set from outside the class, which is the
/// same thing with a wider door.
/// </remarks>
public abstract class FilePathWrapper : FilePath
{
    private readonly FilePath? wrapped;

    /// <summary>
    /// A wrapper over the given path.
    /// </summary>
    /// <param name="name">the wrapped name, with this file system's own prefix</param>
    /// <param name="wrapped">the path underneath, or null if there is none yet</param>
    protected FilePathWrapper(string name, FilePath? wrapped)
        : base(name) => this.wrapped = wrapped;

    /// <inheritdoc/>
    public override FilePath GetPath(string path) => Create(path, UnwrapFrom(path));

    /// <summary>
    /// A wrapper over another path, with this one's prefix in front of it.
    /// </summary>
    /// <param name="basePath">the path to wrap, or null</param>
    /// <returns>the wrapper, or null when there was nothing to wrap</returns>
    public FilePathWrapper? Wrap(FilePath? basePath)
        => basePath is null ? null : Create(GetPrefix() + basePath.Name, basePath);

    /// <inheritdoc/>
    public override FilePath Unwrap() => UnwrapFrom(Name);

    /// <summary>
    /// A wrapper of this kind over the given base path.
    /// </summary>
    /// <param name="path">the wrapped name</param>
    /// <param name="basePath">the path underneath</param>
    /// <returns>the wrapper</returns>
    protected abstract FilePathWrapper Create(string path, FilePath? basePath);

    /// <summary>
    /// What a path of this file system looks like before the name of the file in it.
    /// </summary>
    /// <returns>the prefix</returns>
    protected virtual string GetPrefix() => GetScheme() + ":";

    /// <summary>
    /// The path a name of this file system stands for.
    /// </summary>
    /// <param name="path">the name, with the scheme in front of it</param>
    /// <returns>the path underneath</returns>
    protected virtual FilePath UnwrapFrom(string path)
        => FilePath.Get(path[(GetScheme().Length + 1)..]);

    /// <summary>
    /// The path this one stands for.
    /// </summary>
    /// <returns>the path underneath, or null if there is none</returns>
    protected FilePath? GetBase() => wrapped;

    /// <inheritdoc/>
    public override bool CanWrite() => Base().CanWrite();

    /// <inheritdoc/>
    public override void CreateDirectory() => Base().CreateDirectory();

    /// <inheritdoc/>
    public override bool CreateFile() => Base().CreateFile();

    /// <inheritdoc/>
    public override bool Exists() => Base().Exists();

    /// <inheritdoc/>
    public override bool IsAbsolute() => Base().IsAbsolute();

    /// <inheritdoc/>
    public override bool IsDirectory() => Base().IsDirectory();

    /// <inheritdoc/>
    public override bool IsRegularFile() => Base().IsRegularFile();

    /// <inheritdoc/>
    public override long LastModified() => Base().LastModified();

    /// <inheritdoc/>
    public override FilePath? GetParent() => Wrap(Base().GetParent());

    /// <inheritdoc/>
    public override FilePath ToRealPath() => Wrap(Base().ToRealPath())!;

    /// <inheritdoc/>
    public override IReadOnlyList<FilePath> NewDirectoryStream()
    {
        List<FilePath> entries = [];
        foreach (FilePath entry in Base().NewDirectoryStream())
        {
            FilePathWrapper? wrappedEntry = Wrap(entry);
            if (wrappedEntry is not null)
            {
                entries.Add(wrappedEntry);
            }
        }

        return entries;
    }

    /// <inheritdoc/>
    public override void MoveTo(FilePath newName, bool atomicReplace)
    {
        ArgumentNullException.ThrowIfNull(newName);
        if (newName is not FilePathWrapper other)
        {
            throw new ArgumentException($"Not a {GetScheme()} path: {newName.Name}", nameof(newName));
        }

        Base().MoveTo(other.Base(), atomicReplace);
    }

    /// <inheritdoc/>
    public override FileChannel Open(string mode) => Base().Open(mode);

    /// <inheritdoc/>
    public override FilePath CreateTempFile(string suffix, bool inTempDir)
        => Wrap(Base().CreateTempFile(suffix, inTempDir))!;

    /// <summary>
    /// The path underneath, refusing to go on without it.
    /// </summary>
    /// <returns>the path underneath</returns>
    private FilePath Base()
        => GetBase()
            ?? throw new InvalidOperationException($"{GetScheme()} path without a base: {Name}");
}

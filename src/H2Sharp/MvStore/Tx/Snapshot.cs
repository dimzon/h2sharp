/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */

using System;
using System.Runtime.CompilerServices;

namespace H2Sharp.MvStore.Tx;

/// <summary>
/// A map root plus the committing transactions seen with it: the coherent picture one read
/// decides against. The root is held without its key type, because one transaction's
/// snapshot set spans maps of different key types; the data plane casts it back.
/// </summary>
public sealed class Snapshot
{
    internal readonly object Root;

    internal readonly long[] CommittingTransactions;

    internal Snapshot(object root, long[] committingTransactions)
    {
        Root = root;
        CommittingTransactions = committingTransactions;
    }

    public override int GetHashCode()
    {
        const int prime = 31;
        int result = 1;
        result = prime * result + RuntimeHelpers.GetHashCode(CommittingTransactions);
        result = prime * result + Root.GetHashCode();
        return result;
    }

    public override bool Equals(object? obj) => Equals(obj as Snapshot);

    public bool Equals(Snapshot? obj)
    {
        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj is null)
        {
            return false;
        }

        return ReferenceEquals(CommittingTransactions, obj.CommittingTransactions)
            && ReferenceEquals(Root, obj.Root);
    }
}

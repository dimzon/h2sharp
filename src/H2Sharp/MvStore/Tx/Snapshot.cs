using System;
using System.Runtime.CompilerServices;

namespace H2Sharp.MvStore.Tx;

/// <summary>
/// A map root plus the committing transactions seen with it: the coherent picture one read
/// decides against.
/// </summary>
internal sealed class Snapshot<K, V> : IEquatable<Snapshot<K, V>?>
{
    internal readonly RootReference<K, V> Root;

    internal readonly long[] CommittingTransactions;

    internal Snapshot(RootReference<K, V> root, long[] committingTransactions)
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

    public override bool Equals(object? obj) => Equals(obj as Snapshot<K, V>);

    public bool Equals(Snapshot<K, V>? obj)
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

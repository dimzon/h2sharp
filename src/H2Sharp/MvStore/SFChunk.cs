using System.Collections.Generic;

namespace H2Sharp.MvStore;

/// <summary>
/// A chunk in a single-file store: it reads through the file, which owns the channel.
/// </summary>
public sealed class SFChunk : Chunk
{
    internal SFChunk(int id)
        : base(id)
    {
    }

    internal SFChunk(string line)
        : base(line)
    {
    }

    internal SFChunk(Dictionary<string, string> map)
        : base(ToValues(map), false)
    {
    }
}

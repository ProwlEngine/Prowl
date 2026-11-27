using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using Prowl.Graphite.Debugger.Trace;

namespace Prowl.Graphite.Debugger.Data;

/// <summary>In-memory content-addressed blob store keyed by SHA-256.</summary>
public sealed class ContentStore
{
    private readonly object gate = new();
    private readonly Dictionary<BlobRef, byte[]> blobs = new();
    private ulong totalBytes;

    public int Count
    {
        get
        {
            lock (gate)
            {
                return blobs.Count;
            }
        }
    }

    public ulong TotalBytes
    {
        get
        {
            lock (gate)
            {
                return totalBytes;
            }
        }
    }

    public BlobRef Put(ReadOnlySpan<byte> data)
    {
        byte[] hash = SHA256.HashData(data);
        BlobRef key = new(new EquatableArray<byte>(ImmutableArray.Create(hash)), (ulong)data.Length);
        lock (gate)
        {
            if (!blobs.ContainsKey(key))
            {
                blobs.Add(key, data.ToArray());
                totalBytes += (ulong)data.Length;
            }
        }

        return key;
    }

    public KeyValuePair<BlobRef, byte[]>[] Snapshot()
    {
        lock (gate)
        {
            return blobs.ToArray();
        }
    }

    public bool Contains(BlobRef blob)
    {
        lock (gate)
        {
            return blobs.ContainsKey(blob);
        }
    }

    public bool TryGet(BlobRef blob, out byte[] data)
    {
        lock (gate)
        {
            if (blobs.TryGetValue(blob, out byte[]? found))
            {
                data = found;
                return true;
            }
        }

        data = Array.Empty<byte>();
        return false;
    }
}

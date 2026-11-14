// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Courier;

namespace Prowl.Runtime.Networking;

/// <summary>
///     Host: where RPC arguments hold engine references, by offset from the start of the arguments, so a copy can be
///     sent with Guid.Empty in place of the objects a recipient lacks. A reference is always a 16 byte identifier.
/// </summary>
internal sealed class ReferenceSites : IDisposable
{
    private readonly List<(int Offset, GameObject Target)> _sites = new();
    private readonly NetWriter _masked = new();

    /// <summary>The writer whose references are recorded, or null while reading.</summary>
    internal NetWriter? Writer { get; private set; }

    /// <summary>Where the arguments begin in what is recorded.</summary>
    internal int Start { get; private set; }

    public void Dispose() => _masked.Dispose();

    internal void Begin(NetWriter? writer, int start)
    {
        _sites.Clear();
        Writer = writer;
        Start = start;
    }

    internal void Add(int position, GameObject target) => _sites.Add((position - Start, target));

    /// <summary>The recorder of <paramref name="writer" />'s references, after which nothing more is recorded.</summary>
    internal static ReferenceSites? Finish(NetWriter writer)
    {
        ReferenceSites? sites = NetSerializers.WrittenSites;
        NetSerializers.WrittenSites = null;
        return sites != null && ReferenceEquals(sites.Writer, writer) ? sites : null;
    }

    /// <summary>
    ///     <paramref name="payload" /> as <paramref name="peer" /> may receive it: the arguments starting at
    ///     <paramref name="arguments" /> keep only references to objects both it and <paramref name="caller" /> have.
    /// </summary>
    internal ReadOnlySpan<byte> For(NetworkSystem scene, NetPeer peer, NetPeer? caller, ReadOnlySpan<byte> payload, int arguments)
    {
        if (_sites.Count == 0 || scene.Restricted.Count == 0)
        {
            return payload;
        }

        int at = 0;
        bool masked = false;
        foreach ((int offset, GameObject target) in _sites)
        {
            if (scene.Sees(target, peer) && (caller == null || scene.Sees(target, caller)))
            {
                continue;
            }

            if (!masked)
            {
                _masked.Reset();
                masked = true;
            }

            int site = arguments + offset;
            _masked.WriteBytes(payload[at..site]);
            _masked.WriteGuid(Guid.Empty);
            at = site + 16;
        }

        if (!masked)
        {
            return payload;
        }

        _masked.WriteBytes(payload[at..]);
        return _masked.WrittenSpan;
    }
}

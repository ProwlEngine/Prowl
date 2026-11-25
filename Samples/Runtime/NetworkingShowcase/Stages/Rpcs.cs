// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Networking;
using Prowl.Vector;

namespace NetworkingShowcase;

/// <summary>
///     Stage 3. Calling an RPC runs it where its attribute says: <see cref="AskHost" /> on the host only, which keeps a
///     count only it may change, and <see cref="Ring" /> on every peer. Only the host calls a broadcast, so a client
///     asks with <see cref="RequestRing" /> and the host rings it for everyone. Inside an RPC,
///     <see cref="Rpc.Caller" /> is the peer that called it.
/// </summary>
public class Bell : NetworkBehaviour
{
    private readonly List<string> _log = [];
    private readonly Tint _tint = new();
    private Color _color = new(0.6f, 0.6f, 0.65f, 1f);
    private float _ring;
    [Sync] public int Requests { get; set; }

    public IReadOnlyList<string> Log => _log;

    [Rpc.Host(RpcFlags.AnyCaller)]
    public void RequestRing(int colorIndex) => Ring(colorIndex, Rpc.Caller?.Name ?? "The host");

    [Rpc.Broadcast]
    public void Ring(int colorIndex, string by)
    {
        // Wrapped into the palette, negative ones too: a client picks the index.
        int count = SyncLamp.Colors.Length;
        _color = SyncLamp.Colors[(colorIndex % count + count) % count];
        _ring = 1f;
        Note($"{by} rang it");
    }

    [Rpc.Host(RpcFlags.AnyCaller)]
    public void AskHost()
    {
        Requests++;
        Note($"{Rpc.Caller?.Name ?? "You"} asked the host, request {Requests}");
    }

    private void Note(string line)
    {
        _log.Add(line);
        if (_log.Count > 6)
        {
            _log.RemoveAt(0);
        }
    }

    public override void Update()
    {
        _ring = MathF.Max(0f, _ring - Time.DeltaTime * 1.5f);
        _tint.Apply(GameObject, _color * (0.3f + _ring), 1f + _ring * 6f);
        Transform.LocalEulerAngles = new Float3(0f, 0f, MathF.Sin(Time.TimeSinceStartup * 30f) * 20f * _ring);
    }
}

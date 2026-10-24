// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Networking.Attributes;
using Prowl.Runtime.Networking.Enums;
using Prowl.Vector;

namespace NetworkingShowcase;

/// <summary>
///     Stage 3. Calling an RPC runs it where its attribute says: <see cref="Ring" /> on every peer, the caller too;
///     <see cref="AskHost" /> on the host only, which keeps a count only it may change. Inside an RPC,
///     <see cref="Rpc.Caller" /> is the peer that called it.
/// </summary>
public class Bell : Component
{
    private readonly List<string> _log = [];
    private readonly Tint _tint = new();
    private Color _color = new(0.6f, 0.6f, 0.65f, 1f);
    private float _ring;
    [Sync(SyncFlags.FromHost)] public int Requests { get; set; }

    public IReadOnlyList<string> Log => _log;

    [Rpc.Broadcast]
    public void Ring(int colorIndex)
    {
        _color = SyncLamp.Colors[colorIndex % SyncLamp.Colors.Length];
        _ring = 1f;
        Note($"{Rpc.Caller?.Name ?? "You"} rang it");
    }

    [Rpc.Host]
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

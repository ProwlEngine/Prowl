// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Networking;
using Prowl.Vector;

namespace NetworkingShowcase;

/// <summary>
/// Stage 1. Two [Sync] properties and nothing else: whoever controls the lamp (the host, here) sets them,
/// and every peer's lamp shows them at the next network tick. Setting them on a client does nothing, since
/// the next update would undo it anyway. The lamp eases toward its height on every peer by itself, so its
/// transform isn't synced: only the two values travel.
/// </summary>
public class SyncLamp : Component
{
    public static readonly Color[] Colors =
    [
        new(1f, 0.35f, 0.05f, 1f), new(0.1f, 0.6f, 1f, 1f), new(0.3f, 1f, 0.3f, 1f), new(1f, 0.2f, 0.6f, 1f), new(1f, 0.9f, 0.2f, 1f),
    ];

    [Sync] public int ColorIndex { get; set; }

    [Sync] public float Height { get; set; } = 1.5f;

    private readonly Tint _tint = new();

    public void NextColor() => ColorIndex = (ColorIndex + 1) % Colors.Length;

    public override void Update()
    {
        _tint.Apply(GameObject, Colors[ColorIndex % Colors.Length], 3f);

        Float3 position = Transform.LocalPosition;
        Transform.LocalPosition = position with { Y = position.Y + (Height - position.Y) * MathF.Min(1f, Time.DeltaTime * 6f) };
    }
}

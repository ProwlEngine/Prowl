// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Networking;
using Prowl.Vector;

namespace NetworkingShowcase;

/// <summary>
/// Stage 4. A [Change] hook runs with the old and new value wherever the value changes: on the host, which
/// sets it, and on every client when it arrives. Anyone can ask for more heat with a host RPC; only the host
/// changes it. A late joiner gets the current heat without the hook running, since nothing changed for it.
/// </summary>
public class Reactor : Component
{
    [Sync, Change(nameof(OnHeatChanged))] public int Heat { get; set; } = 20;

    private readonly List<string> _log = [];
    private readonly Tint _tint = new();
    private float _pulse;

    public IReadOnlyList<string> Log => _log;

    [Rpc.Host]
    public void Stoke(int amount) => Heat = Math.Clamp(Heat + amount, 0, 100);

    private void OnHeatChanged(int oldValue, int newValue)
    {
        _pulse = 1f;
        _log.Add($"{oldValue} to {newValue}");
        if (_log.Count > 6)
            _log.RemoveAt(0);
    }

    public override void Update()
    {
        _pulse = MathF.Max(0f, _pulse - Time.DeltaTime * 2f);
        float t = Heat / 100f;
        Color cold = new(0.1f, 0.35f, 1f, 1f), hot = new(1f, 0.2f, 0.02f, 1f);
        _tint.Apply(GameObject, new Color(cold.R + (hot.R - cold.R) * t, cold.G + (hot.G - cold.G) * t, cold.B + (hot.B - cold.B) * t, 1f), 1f + t * 4f);
        float scale = 1f + _pulse * 0.35f;
        Transform.LocalScale = new Float3(scale, scale, scale);
    }
}

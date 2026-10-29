// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using static Prowl.Samples.Sample;

namespace NetworkingShowcase;

/// <summary>
///     Stage 5. Network collections send each change, not the whole collection: adding a bar sends one bar. The
///     host edits the bars; anyone can vote, through a host RPC that counts votes in a dictionary and voters in a
///     set. Every peer rebuilds its bars from the <c>Changed</c> events, and a late joiner gets the whole lot.
/// </summary>
public class Board : Component
{
    private readonly List<string> _events = [];
    private readonly List<GameObject> _shown = [];
    private bool _stale = true;
    [Sync] public NetList<int> Bars { get; } = new();

    [Sync] public NetDictionary<string, int> Votes { get; } = new();

    [Sync] public NetSet<string> Voters { get; } = new();

    public IReadOnlyList<string> Events => _events;

    public override void OnEnable()
    {
        Bars.Changed += OnBarsChanged;
        Votes.Changed += OnVotesChanged;
    }

    public override void OnDisable()
    {
        Bars.Changed -= OnBarsChanged;
        Votes.Changed -= OnVotesChanged;
    }

    public void AddBar() => Bars.Add(Random.Shared.Next(1, 10));

    public void GrowLast()
    {
        if (Bars.Count > 0)
        {
            Bars[^1] = Math.Min(Bars[^1] + 2, 12);
        }
    }

    public void RemoveFirst()
    {
        if (Bars.Count > 0)
        {
            Bars.RemoveAt(0);
        }
    }

    [Rpc.Host]
    public void Vote(string option)
    {
        Votes[option] = Votes.TryGetValue(option, out int count) ? count + 1 : 1;
        Voters.Add(Rpc.Caller?.Name ?? "Host");
    }

    private void OnBarsChanged(NetListChange<int> change)
    {
        Note(change.Kind switch
        {
            NetChangeKind.Added => $"bar {change.Index} added: {change.NewValue}",
            NetChangeKind.Removed => $"bar {change.Index} removed (was {change.OldValue})",
            NetChangeKind.Replaced => $"bar {change.Index}: {change.OldValue} to {change.NewValue}",
            _ => "bars cleared"
        });
        _stale = true;
    }

    private void OnVotesChanged(NetDictionaryChange<string, int> change) =>
        Note($"votes for {change.Key}: {change.NewValue}");

    private void Note(string line)
    {
        _events.Add(line);
        if (_events.Count > 6)
        {
            _events.RemoveAt(0);
        }
    }

    public override void Update()
    {
        // Bars may be in place before this component first runs, from a snapshot, with no events for them.
        if (!_stale && _shown.Count == Bars.Count)
        {
            return;
        }

        _stale = false;
        foreach (GameObject bar in _shown)
        {
            bar.Destroy();
        }

        _shown.Clear();

        for (int i = 0; i < Bars.Count; i++)
        {
            float height = Bars[i] * 0.3f;
            Color color = Hsv(i * 0.13f % 1f, 0.7f, 1f);
            GameObject bar = Model("Bar", Mesh.CreateCube(new Float3(0.5f, height, 0.5f)),
                Lit(color).Emissive(color, 1.5f),
                Transform.Position + new Float3((i - (Bars.Count - 1) * 0.5f) * 0.7f, height * 0.5f, 0f));

            // Only what the bars show is local: the bars themselves are the synced list.
            bar.Network.Mode = NetworkMode.Never;
            GameObject.Scene!.Add(bar);
            _shown.Add(bar);
        }
    }
}

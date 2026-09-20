using System.Collections.Generic;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

// ---- Random selector ---------------------------------------------------------------------------

/// <summary>
/// Plays one of its children, picked at random, and picks again every time the chosen child finishes
/// (or whenever the node is re entered). Each child may carry a weight, and by default the same child
/// is not chosen twice in a row. The usual way to vary idles and reactions without authoring a state
/// machine for them.
/// </summary>
public sealed class RandomSelectorDefinition : PoseNodeDefinition
{
    public RandomSelectorDefinition(IReadOnlyList<int> children, IReadOnlyList<float>? weights = null)
    {
        Children = new List<int>(children).ToArray();
        Weights = weights is null ? null : new List<float>(weights).ToArray();
    }

    public int[] Children { get; }

    /// <summary>Relative chance of each child, or null for an even spread.</summary>
    public float[]? Weights { get; }

    /// <summary>When true (the default) a child is never picked twice in a row, unless it is the only one.</summary>
    public bool AvoidRepeats { get; set; } = true;

    /// <summary>When false the node keeps playing the child it chose instead of picking again at the end.</summary>
    public bool RepickWhenFinished { get; set; } = true;

    /// <summary>Seed for the picks, so a graph can be replayed exactly. 0 seeds from the clock.</summary>
    public uint Seed { get; set; }

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PoseNodeInstance
    {
        private readonly RandomSelectorDefinition _def;
        private PoseNodeInstance[] _children = null!;
        private RandomSource _random;
        private int _selected = -1;
        private int _startLoop;

        public Instance(RandomSelectorDefinition def) => _def = def;

        public override SyncTrack SyncTrack => _selected >= 0 ? _children[_selected].SyncTrack : SyncTrack.Default;

        public override void Bind(GraphBindContext context)
        {
            if (_def.Children.Length == 0)
                throw context.Error("a random selector needs at least one child.");
            if (_def.Weights is not null && _def.Weights.Length != _def.Children.Length)
                throw context.Error("a random selector needs one weight per child.");

            Pose = new Pose(context.Skeleton);
            _children = new PoseNodeInstance[_def.Children.Length];
            for (int i = 0; i < _children.Length; i++)
                _children[i] = context.PoseNode(_def.Children[i]);
        }

        protected override void OnInitialize(GraphContext context, SyncTrackTime? initialTime)
        {
            _random = new RandomSource(_def.Seed);
            _selected = Pick();
            _children[_selected].Initialize(context, initialTime);
            _startLoop = _children[_selected].LoopCount;
            CopyTimingFrom(_children[_selected]);
        }

        protected override void OnShutdown(GraphContext context)
        {
            if (_selected >= 0)
                _children[_selected].Shutdown(context);
            _selected = -1;
        }

        protected override void OnUpdate(GraphContext context)
        {
            PoseNodeInstance child = _children[_selected];
            if (_def.RepickWhenFinished && Finished(child))
            {
                child.Shutdown(context);
                _selected = Pick();
                child = _children[_selected];
                child.Initialize(context, null);
                _startLoop = child.LoopCount;
            }

            child.Update(context);
            CopyResultFrom(child);
        }

        private bool Finished(PoseNodeInstance child)
            => child.LoopCount > _startLoop || (child.Duration > 0f && child.NormalizedTime >= 1f);

        private int Pick()
        {
            if (_children.Length == 1)
                return 0;

            int chosen = _def.Weights is null ? (int)_random.Next((uint)_children.Length) : PickWeighted();
            if (_def.AvoidRepeats && chosen == _selected)
                chosen = _def.Weights is null ? (chosen + 1 + (int)_random.Next((uint)(_children.Length - 1))) % _children.Length : PickWeighted(skip: _selected);
            return chosen;
        }

        private int PickWeighted(int skip = -1)
        {
            float total = 0f;
            for (int i = 0; i < _children.Length; i++)
                if (i != skip && _def.Weights![i] > 0f)
                    total += _def.Weights[i];

            // Nothing else carries weight, so keep the current child rather than force a zero chance one.
            if (total <= 0f)
                return skip >= 0 ? skip : 0;

            float pick = _random.NextFloat() * total;
            for (int i = 0; i < _children.Length; i++)
            {
                if (i == skip || !(_def.Weights![i] > 0f))
                    continue;
                pick -= _def.Weights[i];
                if (pick <= 0f)
                    return i;
            }
            return _children.Length - 1;
        }
    }
}

// ---- Sequence ----------------------------------------------------------------------------------

/// <summary>
/// Plays its children one after another, moving on when the current one reaches its end. It can loop
/// back to the first child or hold the last one. Handy for a scripted chain (get up, dust off, look
/// around) without a state machine and its transitions.
/// </summary>
public sealed class SequenceDefinition : PoseNodeDefinition
{
    public SequenceDefinition(IReadOnlyList<int> children) => Children = new List<int>(children).ToArray();

    public int[] Children { get; }

    /// <summary>When true the sequence starts again after the last child instead of holding it.</summary>
    public bool Loop { get; set; }

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PoseNodeInstance
    {
        private readonly SequenceDefinition _def;
        private PoseNodeInstance[] _children = null!;
        private int _index;
        private int _startLoop;
        private bool _done;

        public Instance(SequenceDefinition def) => _def = def;

        public override SyncTrack SyncTrack => _children[_index].SyncTrack;

        /// <summary>Which child is playing (0 based).</summary>
        public int Index => _index;

        public override void Bind(GraphBindContext context)
        {
            if (_def.Children.Length == 0)
                throw context.Error("a sequence needs at least one child.");

            Pose = new Pose(context.Skeleton);
            _children = new PoseNodeInstance[_def.Children.Length];
            for (int i = 0; i < _children.Length; i++)
                _children[i] = context.PoseNode(_def.Children[i]);
        }

        protected override void OnInitialize(GraphContext context, SyncTrackTime? initialTime)
        {
            _index = 0;
            _done = false;
            _children[0].Initialize(context, initialTime);
            _startLoop = _children[0].LoopCount;
            CopyTimingFrom(_children[0]);
        }

        protected override void OnShutdown(GraphContext context) => _children[_index].Shutdown(context);

        protected override void OnUpdate(GraphContext context)
        {
            PoseNodeInstance child = _children[_index];
            if (!_done && Finished(child))
            {
                if (_index + 1 < _children.Length)
                {
                    child.Shutdown(context);
                    _index++;
                    child = _children[_index];
                    child.Initialize(context, null);
                    _startLoop = child.LoopCount;
                }
                else if (_def.Loop)
                {
                    child.Shutdown(context);
                    _index = 0;
                    child = _children[0];
                    child.Initialize(context, null);
                    _startLoop = child.LoopCount;
                }
                else
                {
                    _done = true;
                }
            }

            child.Update(context);
            CopyResultFrom(child);
        }

        private bool Finished(PoseNodeInstance child)
            => child.LoopCount > _startLoop || (child.Duration > 0f && child.NormalizedTime >= 1f);
    }
}

/// <summary>A small deterministic random source, so a seeded graph replays its picks exactly.</summary>
internal struct RandomSource
{
    private uint _state;

    public RandomSource(uint seed) => _state = seed != 0 ? seed : (uint)Environment.TickCount | 1u;

    /// <summary>The next value below <paramref name="exclusiveMax"/>.</summary>
    public uint Next(uint exclusiveMax) => exclusiveMax == 0 ? 0 : NextBits() % exclusiveMax;

    /// <summary>The next value in [0,1).</summary>
    public float NextFloat() => (NextBits() >> 8) * (1f / 16777216f);

    private uint NextBits()
    {
        // xorshift32: small, fast and good enough to pick a clip.
        _state ^= _state << 13;
        _state ^= _state >> 17;
        _state ^= _state << 5;
        return _state;
    }
}

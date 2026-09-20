using System.Collections.Generic;

namespace Prowl.Motion;

// ---------------------------------------------------------------------------------------------
// Bone mask value nodes (produce a per-bone weight mask for masked / layered blending)
// ---------------------------------------------------------------------------------------------

/// <summary>
/// Builds a bone mask by feathering authored seed weights (by bone id) down the hierarchy. The mask is
/// constant, so it is built once at bind time.
/// </summary>
public sealed class HierarchicalBoneMaskDefinition : BoneMaskNodeDefinition
{
    public HierarchicalBoneMaskDefinition(IReadOnlyList<(StringID Bone, float Weight)> seeds)
        => Seeds = new List<(StringID, float)>(seeds).ToArray();

    public (StringID Bone, float Weight)[] Seeds { get; }
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : BoneMaskNodeInstance
    {
        private readonly HierarchicalBoneMaskDefinition _def;
        private BoneMask _mask = null!;
        public Instance(HierarchicalBoneMaskDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            var seeds = new List<(int Bone, float Weight)>(_def.Seeds.Length);
            foreach ((StringID bone, float weight) in _def.Seeds)
            {
                int index = context.Skeleton.GetBoneIndex(bone);
                if (index != Skeleton.InvalidIndex)
                    seeds.Add((index, weight));
            }
            _mask = BoneMask.CreateHierarchical(context.Skeleton, seeds);
        }

        protected override BoneMask Compute(GraphContext context) => _mask;
    }
}

/// <summary>A bone mask with a uniform fixed weight on every bone.</summary>
public sealed class FixedWeightBoneMaskDefinition : BoneMaskNodeDefinition
{
    public FixedWeightBoneMaskDefinition(float weight) => Weight = weight;
    public float Weight { get; }
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : BoneMaskNodeInstance
    {
        private readonly FixedWeightBoneMaskDefinition _def;
        private BoneMask _mask = null!;
        public Instance(FixedWeightBoneMaskDefinition def) => _def = def;
        public override void Bind(GraphBindContext context) => _mask = new BoneMask(context.Skeleton, _def.Weight);
        protected override BoneMask Compute(GraphContext context) => _mask;
    }
}

/// <summary>Lerps between two bone masks by a float parameter.</summary>
public sealed class BoneMaskBlendDefinition : BoneMaskNodeDefinition
{
    public BoneMaskBlendDefinition(int maskA, int maskB, int blendNodeIndex) { MaskA = maskA; MaskB = maskB; BlendNodeIndex = blendNodeIndex; }
    public int MaskA { get; }
    public int MaskB { get; }
    public int BlendNodeIndex { get; }
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : BoneMaskNodeInstance
    {
        private readonly BoneMaskBlendDefinition _def;
        private BoneMaskNodeInstance _a = null!, _b = null!;
        private ValueNodeInstance _blend = null!;
        private BoneMask _result = null!;
        public Instance(BoneMaskBlendDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            _a = context.BoneMaskNode(_def.MaskA);
            _b = context.BoneMaskNode(_def.MaskB);
            _blend = context.ValueNode(_def.BlendNodeIndex, ValueInputKind.Number);
            _result = new BoneMask(context.Skeleton);
        }

        protected override BoneMask Compute(GraphContext context)
        {
            _result.CopyFrom(_a.GetMask(context));
            _result.BlendTo(_b.GetMask(context), _blend.GetValue(context).AsFloat());
            return _result;
        }
    }
}

/// <summary>Selects one of several bone masks by an int parameter (clamped).</summary>
public sealed class BoneMaskSelectorDefinition : BoneMaskNodeDefinition
{
    public BoneMaskSelectorDefinition(int selectorNodeIndex, IReadOnlyList<int> maskNodes)
    { SelectorNodeIndex = selectorNodeIndex; MaskNodes = new List<int>(maskNodes).ToArray(); }
    public int SelectorNodeIndex { get; }
    public int[] MaskNodes { get; }
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : BoneMaskNodeInstance
    {
        private readonly BoneMaskSelectorDefinition _def;
        private ValueNodeInstance _selector = null!;
        private BoneMaskNodeInstance[] _masks = null!;
        public Instance(BoneMaskSelectorDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            if (_def.MaskNodes.Length == 0)
                throw context.Error("a bone mask selector needs at least one mask.");
            _selector = context.ValueNode(_def.SelectorNodeIndex, ValueInputKind.Number);
            _masks = new BoneMaskNodeInstance[_def.MaskNodes.Length];
            for (int i = 0; i < _masks.Length; i++)
                _masks[i] = context.BoneMaskNode(_def.MaskNodes[i]);
        }

        protected override BoneMask Compute(GraphContext context)
        {
            int index = Math.Clamp(_selector.GetValue(context).AsInt(), 0, _masks.Length - 1);
            return _masks[index].GetMask(context);
        }
    }
}

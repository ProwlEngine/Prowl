// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Motion;

namespace Prowl.Runtime.AnimationNodes;

public sealed class BoneMaskNode : AnimationGraphNode
{
    private readonly TextSetting _bones = BoneList("Bones");
    private readonly NumberSetting _rest = Number("Rest Weight");

    public BoneMaskNode()
    {
        Settings(_bones, _rest);
    }

    public override string Id => AnimationNodeIds.BoneMask;
    public override string DisplayName => "Bone Mask";
    public override string Category => "Masks";
    public override string Description => "A mask seeded from bones and their weights, spread down the hierarchy. Bones no seed reaches take the rest weight.";

    public override NodePinKind Output => NodePinKind.Mask;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        (StringID Bone, float Weight)[] seeds = ctx.Bones(r, _bones);
        return seeds.Length == 0
            ? ctx.Graph.AddFixedWeightBoneMask(r.Get(_rest))
            : ctx.Graph.AddNode(new HierarchicalBoneMaskDefinition(seeds) { RestWeight = r.Get(_rest) });
    }
}

public sealed class MaskAssetNode : AnimationGraphNode
{
    private readonly AssetSetting _mask = Asset("Mask", NodeValueKind.Mask);

    public MaskAssetNode()
    {
        Settings(_mask);
    }

    public override string Id => AnimationNodeIds.MaskAsset;
    public override string DisplayName => "Mask Asset";
    public override string Category => "Masks";
    public override string Description => "A mask saved as an asset.";

    public override NodePinKind Output => NodePinKind.Mask;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Mask(r, _mask) is { } bones ? ctx.Graph.AddStaticBoneMask(bones) : ctx.Graph.AddFixedWeightBoneMask(1f);
}

public sealed class MaskBlendNode : AnimationGraphNode
{
    private readonly InputPin _a = MaskInput("A", optional: false), _b = MaskInput("B", optional: false), _blend = NumberInput("Blend", optional: false);

    public MaskBlendNode()
    {
        Pins(_a, _b, _blend);
    }

    public override string Id => AnimationNodeIds.MaskBlend;
    public override string DisplayName => "Blend Masks";
    public override string Category => "Masks";
    public override string Description => "Mixes two masks by a number.";

    public override NodePinKind Output => NodePinKind.Mask;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddBoneMaskBlend(ctx.Input(r, _a), ctx.Input(r, _b), ctx.Input(r, _blend));
}

public sealed class MaskSelectorNode : AnimationGraphNode
{
    private readonly InputPin _index = IntegerInput("Index", optional: false), _masks = MaskInput("Masks", variadic: true);

    public MaskSelectorNode()
    {
        Pins(_index, _masks);
    }

    public override string Id => AnimationNodeIds.MaskSelector;
    public override string DisplayName => "Choose Mask";
    public override string Category => "Masks";
    public override string Description => "Picks whichever mask an integer chooses.";

    public override NodePinKind Output => NodePinKind.Mask;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        int[] options = ctx.Inputs(r, _masks);
        return options.Length == 0 ? ctx.Graph.AddFixedWeightBoneMask(1f) : ctx.Graph.AddBoneMaskSelector(ctx.Input(r, _index), options);
    }
}

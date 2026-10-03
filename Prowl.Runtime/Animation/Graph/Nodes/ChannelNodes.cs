// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Motion;

namespace Prowl.Runtime.AnimationNodes;

public sealed class ChannelLayerNode : AnimationGraphNode
{
    private readonly InputPin _pose = PoseInput(), _value = NumberInput("Value", variadic: true);
    private readonly ChoiceSetting<ChannelBlendMode> _mode = Choice("Mode", ChannelBlendMode.Override);

    public ChannelLayerNode()
    {
        Pins(_pose, _value);
        Settings(_mode);
    }

    public override string Id => AnimationNodeIds.ChannelLayer;
    public override string DisplayName => "Drive Channels";
    public override string Category => "Channels";
    public override string Description => "Writes scalar channels onto a pose. Each input names the channel it drives.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        var drivers = new List<ChannelDriver>();
        foreach (PinGroup driver in ctx.Groups(r))
            if (driver.Entry.Name.Length > 0)
                drivers.Add(new ChannelDriver(new StringID(driver.Entry.Name), ctx.Input(driver, _value), r.Get(_mode)));
        return drivers.Count == 0 ? ctx.Graph.AddPassthrough(ctx.Input(r, _pose)) : ctx.Graph.AddFloatChannelLayer(ctx.Input(r, _pose), drivers);
    }
}

public sealed class DrivenChannelNode : AnimationGraphNode
{
    private readonly InputPin _pose = PoseInput();
    private readonly TextSetting _bone = BoneName("Bone"), _channel = Name("Channel");
    private readonly NumberSetting _from = Number("From"), _to = Number("To", 90f);

    public DrivenChannelNode()
    {
        Pins(_pose);
        Settings(_bone, _channel, _from, _to);
    }

    public override string Id => AnimationNodeIds.DrivenChannel;
    public override string DisplayName => "Driven Channel";
    public override string Category => "Channels";
    public override string Description => "Drives a channel from how far a bone has turned, for a corrective shape.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddDrivenChannel(ctx.Input(r, _pose), ctx.BoneId(r, _bone), ctx.Id(r, _channel), r.Get(_from), r.Get(_to));
}

public sealed class PoseChannelNode : AnimationGraphNode
{
    private readonly InputPin _pose = PoseInput();
    private readonly TextSetting _channel = Name("Channel");
    private readonly NumberSetting _fallback = Number("Default");

    public PoseChannelNode()
    {
        Pins(_pose);
        Settings(_channel, _fallback);
    }

    public override string Id => AnimationNodeIds.PoseChannel;
    public override string DisplayName => "Read Channel";
    public override string Category => "Channels";
    public override string Description => "Reads a scalar channel out of a pose.";

    public override NodePinKind Output => NodePinKind.Number;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddPoseChannel(ctx.Input(r, _pose), ctx.Id(r, _channel), r.Get(_fallback));
}

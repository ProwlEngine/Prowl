// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;

using Prowl.Echo;
using Prowl.Editor.Inspector;
using Prowl.OrigamiUI;
using Prowl.Runtime;
using Prowl.Runtime.AnimationNodes;
using Prowl.Vector;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// The graph editor's model edits: what a wire does to the record behind it, what a delete leaves
/// behind, and what the validator notices. These run without a window, because none of it needs one.
/// </summary>
public class AnimationGraphEditorTests
{
    private static AnimationGraphView ViewOver(AnimationGraph graph)
    {
        var view = new AnimationGraphView { Graph = graph };
        view.Sync();
        return view;
    }

    private static ConnectionRequest Wire(string from, string to, int pin)
        => new(from, AnimationGraphView.OutputPort, to, AnimationGraphView.PinId(pin));

    private static ConnectionRequest WireToSocket(string from, string to)
        => new(from, AnimationGraphView.OutputPort, to, AnimationGraphView.PinId(0), toPlaceholder: true);

    // ---- pins -------------------------------------------------------------------------------

    [Fact]
    public void ANewNodeGetsItsDeclaredPins()
    {
        var graph = new AnimationGraph();
        var view = ViewOver(graph);

        GraphNodeRecord? layers = view.Add(AnimationNodeIds.LayerBlend, new Float2(0, 0));

        Assert.NotNull(layers);
        Assert.Equal(AnimationNodeRegistry.Get(AnimationNodeIds.LayerBlend)!.Inputs.Count, layers!.Inputs.Count);
    }

    [Fact]
    public void WiringTheOpenSocketFillsTheTrailingGroupBeforeStartingANewOne()
    {
        var graph = new AnimationGraph();
        var view = ViewOver(graph);

        GraphNodeRecord layers = view.Add(AnimationNodeIds.LayerBlend, new Float2(0, 0))!;
        GraphNodeRecord poseA = view.Add(AnimationNodeIds.Clip, new Float2(0, 0))!;
        GraphNodeRecord poseB = view.Add(AnimationNodeIds.Clip, new Float2(0, 0))!;
        view.Invalidate();
        view.Sync();

        // Layer blend takes a base pose then repeating groups of pose, weight, mask. A pose belongs in
        // a layer slot, so the second one starts a new group rather than landing in the weight beside
        // the first.
        view.Connect(WireToSocket(poseA.Id, layers.Id));
        view.Connect(WireToSocket(poseB.Id, layers.Id));

        Assert.Equal(poseA.Id, layers.Inputs[1].Node);
        Assert.Equal(string.Empty, layers.Inputs[2].Node);
        Assert.Equal(poseB.Id, layers.Inputs[4].Node);
        Assert.Equal(7, layers.Inputs.Count);
    }

    [Fact]
    public void ANumberDroppedOnTheSameSocketTakesTheWeightBesideItsLayer()
    {
        var graph = new AnimationGraph();
        var view = ViewOver(graph);

        GraphNodeRecord layers = view.Add(AnimationNodeIds.LayerBlend, new Float2(0, 0))!;
        GraphNodeRecord pose = view.Add(AnimationNodeIds.Clip, new Float2(0, 0))!;
        GraphNodeRecord weight = view.Add(AnimationNodeIds.ConstFloat, new Float2(0, 0))!;
        view.Invalidate();
        view.Sync();

        view.Connect(WireToSocket(pose.Id, layers.Id));
        view.Connect(WireToSocket(weight.Id, layers.Id));

        Assert.Equal(pose.Id, layers.Inputs[1].Node);
        Assert.Equal(weight.Id, layers.Inputs[2].Node);
        Assert.Equal(4, layers.Inputs.Count);
    }

    [Fact]
    public void UnwiringATrailingGroupDropsItsPins()
    {
        var graph = new AnimationGraph();
        var view = ViewOver(graph);

        GraphNodeRecord blend = view.Add(AnimationNodeIds.Blend1D, new Float2(0, 0))!;
        GraphNodeRecord pose = view.Add(AnimationNodeIds.Clip, new Float2(0, 0))!;
        view.Invalidate();
        view.Sync();

        view.Connect(WireToSocket(pose.Id, blend.Id));
        view.Connect(WireToSocket(pose.Id, blend.Id));
        int wired = blend.Inputs.Count;

        view.Disconnect(new GraphConnection(pose.Id, AnimationGraphView.OutputPort, blend.Id, AnimationGraphView.PinId(2)));
        view.Disconnect(new GraphConnection(pose.Id, AnimationGraphView.OutputPort, blend.Id, AnimationGraphView.PinId(1)));

        Assert.True(wired > blend.Inputs.Count);
        Assert.Equal(AnimationNodeRegistry.Get(AnimationNodeIds.Blend1D)!.VariadicStart, blend.Inputs.Count);
    }

    // ---- deleting ---------------------------------------------------------------------------

    [Fact]
    public void DeletingANodeUnwiresIt()
    {
        var graph = new AnimationGraph();
        var view = ViewOver(graph);

        GraphNodeRecord pose = view.Add(AnimationNodeIds.Clip, new Float2(0, 0))!;
        GraphNodeRecord speed = view.Add(AnimationNodeIds.SpeedScale, new Float2(0, 0))!;
        view.Invalidate();
        view.Sync();

        view.Connect(Wire(pose.Id, speed.Id, 0));
        graph.RootNode = pose.Id;

        view.Delete(new[] { view.Nodes.First(n => n.Id == pose.Id) }, System.Array.Empty<GraphConnection>());

        Assert.DoesNotContain(graph.Nodes, r => r.Id == pose.Id);
        Assert.Equal(string.Empty, speed.Inputs[0].Node);
        Assert.Equal(string.Empty, graph.RootNode);
    }

    // ---- graphs inside graphs ---------------------------------------------------------------

    /// <summary>A machine with one state, and a clip inside that state's graph wired into its output.</summary>
    private static (GraphNodeRecord Machine, GraphStateRecord State, GraphNodeRecord Clip) MachineWithState(AnimationGraph graph, AnimationGraphView view)
    {
        GraphNodeRecord machine = view.Add(AnimationNodeIds.StateMachine, new Float2(0, 0))!;
        var states = new AnimationGraphStateView { Machine = machine };
        states.Sync(graph);
        GraphStateRecord state = states.Add(new Float2(0, 0))!;

        view.Scope = state.Id;
        view.Sync();
        GraphNodeRecord clip = view.Add(AnimationNodeIds.Clip, new Float2(0, 0))!;
        GraphNodeRecord output = graph.OwnedNode(state.Id, AnimationNodeIds.StateOutput)!;
        output.Inputs[0].Node = clip.Id;

        view.Scope = string.Empty;
        view.Sync();
        return (machine, state, clip);
    }

    [Fact]
    public void AStateStartsWithAnOutputThatWaitsToBeEntered()
    {
        var graph = new AnimationGraph();
        var view = ViewOver(graph);
        (_, GraphStateRecord state, _) = MachineWithState(graph, view);

        GraphNodeRecord output = graph.OwnedNode(state.Id, AnimationNodeIds.StateOutput)!;
        Assert.False(output.Inputs[1].Flag);
        Assert.True(output.Inputs[2].Flag);
    }

    [Fact]
    public void EachGraphShowsOnlyItsOwnNodes()
    {
        var graph = new AnimationGraph();
        var view = ViewOver(graph);
        (GraphNodeRecord machine, GraphStateRecord state, GraphNodeRecord clip) = MachineWithState(graph, view);

        Assert.Contains(view.Nodes, n => n.Id == machine.Id);
        Assert.DoesNotContain(view.Nodes, n => n.Id == clip.Id);

        view.Scope = state.Id;
        view.Sync();
        Assert.Contains(view.Nodes, n => n.Id == clip.Id);
        Assert.DoesNotContain(view.Nodes, n => n.Id == machine.Id);
    }

    [Fact]
    public void DeletingAStateMachineTakesItsStatesGraphsWithIt()
    {
        var graph = new AnimationGraph();
        var view = ViewOver(graph);
        (GraphNodeRecord machine, GraphStateRecord state, GraphNodeRecord clip) = MachineWithState(graph, view);
        graph.Groups.Add(new GraphGroupRecord { Id = "g", Owner = state.Id });

        view.Delete(new[] { view.Nodes.First(n => n.Id == machine.Id) }, System.Array.Empty<GraphConnection>());

        Assert.Empty(graph.Nodes);
        Assert.Empty(graph.Groups);
    }

    [Fact]
    public void AStateOutputCannotBeDeleted()
    {
        var graph = new AnimationGraph();
        var view = ViewOver(graph);
        (_, GraphStateRecord state, _) = MachineWithState(graph, view);

        view.Scope = state.Id;
        view.Sync();
        GraphNodeRecord output = graph.OwnedNode(state.Id, AnimationNodeIds.StateOutput)!;
        view.Delete(new[] { view.Nodes.First(n => n.Id == output.Id) }, System.Array.Empty<GraphConnection>());

        Assert.Contains(graph.Nodes, n => n.Id == output.Id);
    }

    [Fact]
    public void ANewSubGraphIsEmbeddedWithItsOwnOutput()
    {
        var graph = new AnimationGraph();
        var view = ViewOver(graph);

        GraphNodeRecord sub = view.Add(AnimationNodeIds.SubGraph, new Float2(0, 0))!;

        Assert.True(sub.Get(SubGraphNode.Embedded));
        Assert.NotNull(graph.OwnedNode(sub.Id, AnimationNodeIds.GraphOutput));
    }

    [Fact]
    public void DuplicatingANodeCopiesItsSettingsButNotItsWires()
    {
        var graph = new AnimationGraph();
        var view = ViewOver(graph);

        GraphNodeRecord pose = view.Add(AnimationNodeIds.Clip, new Float2(0, 0))!;
        GraphNodeRecord speed = view.Add(AnimationNodeIds.SpeedScale, new Float2(0, 0))!;
        speed.Properties["Default"] = NodeValue.FromNumber(2.5f);
        view.Invalidate();
        view.Sync();
        view.Connect(Wire(pose.Id, speed.Id, 0));

        GraphNodeRecord copy = view.Duplicate(speed)!;

        Assert.NotEqual(speed.Id, copy.Id);
        Assert.Equal(2.5f, copy.Properties["Default"].Number);
        Assert.NotSame(speed.Properties["Default"], copy.Properties["Default"]);
        Assert.Equal(string.Empty, copy.Inputs[0].Node);
    }

    [Fact]
    public void ANodeIsNamedAfterWhatItIsSetTo()
    {
        var graph = new AnimationGraph();
        var view = ViewOver(graph);

        GraphNodeRecord clip = view.Add(AnimationNodeIds.Clip, new Float2(0, 0))!;
        GraphNodeRecord parameter = view.Add(AnimationNodeIds.Parameter, new Float2(0, 0))!;
        GraphNodeRecord speed = view.Add(AnimationNodeIds.SpeedScale, new Float2(0, 0))!;

        var asset = new AnimationClip { Name = "Run Forward" };
        clip.Properties["Clip"] = NodeValue.FromClip(asset);
        parameter.Properties["Name"] = NodeValue.FromText("Speed");

        view.Invalidate();
        view.Sync();

        Assert.Equal("Run Forward", clip.Name);
        Assert.Equal("Speed", parameter.Name);
        Assert.Equal(string.Empty, speed.Name);
    }

    // ---- what a wire is allowed to be -------------------------------------------------------

    [Fact]
    public void AWireBetweenMismatchedKindsIsRefused()
    {
        var graph = new AnimationGraph();
        var view = ViewOver(graph);

        GraphNodeRecord pose = view.Add(AnimationNodeIds.Clip, new Float2(0, 0))!;
        GraphNodeRecord blend = view.Add(AnimationNodeIds.Blend1D, new Float2(0, 0))!;
        view.Invalidate();
        view.Sync();

        // Pin 0 of a 1D blend is the number that drives it, not a pose.
        Assert.False(view.Validate(Wire(pose.Id, blend.Id, 0)));
        Assert.True(view.Validate(WireToSocket(pose.Id, blend.Id)));
    }

    [Fact]
    public void AWireThatWouldCloseALoopIsRefused()
    {
        var graph = new AnimationGraph();
        var view = ViewOver(graph);

        GraphNodeRecord a = view.Add(AnimationNodeIds.Clip, new Float2(0, 0))!;
        GraphNodeRecord b = view.Add(AnimationNodeIds.SpeedScale, new Float2(0, 0))!;
        GraphNodeRecord c = view.Add(AnimationNodeIds.SpeedScale, new Float2(0, 0))!;
        view.Invalidate();
        view.Sync();

        view.Connect(Wire(a.Id, b.Id, 0));
        view.Connect(Wire(b.Id, c.Id, 0));

        Assert.False(view.Validate(Wire(c.Id, b.Id, 0)));
        Assert.False(view.Validate(Wire(b.Id, b.Id, 0)));
    }

    // ---- what the validator notices ---------------------------------------------------------

    [Fact]
    public void AGraphWithNoOutputSaysSo()
    {
        var graph = new AnimationGraph();
        var view = ViewOver(graph);

        Assert.Contains(view.Problems, p => p.Blocking && p.Message.Contains("no output", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AParameterNodeNamingNothingDeclaredSaysSo()
    {
        var graph = new AnimationGraph();
        var view = ViewOver(graph);

        GraphNodeRecord pose = view.Add(AnimationNodeIds.Clip, new Float2(0, 0))!;
        GraphNodeRecord parameter = view.Add(AnimationNodeIds.Parameter, new Float2(0, 0))!;
        parameter.Properties["Name"] = NodeValue.FromText("Speed");
        graph.RootNode = pose.Id;

        view.Invalidate();
        view.Sync();
        Assert.Contains(view.Problems, p => p.NodeId == parameter.Id);

        graph.Parameters.Add(new GraphParameterRecord { Name = "Speed", Kind = NodeValueKind.Number });
        view.Invalidate();
        view.Sync();
        Assert.DoesNotContain(view.Problems, p => p.NodeId == parameter.Id);
    }

    // ---- states ------------------------------------------------------------------------------

    [Fact]
    public void RenamingAStateCarriesTheTransitionsIntoIt()
    {
        (AnimationGraph graph, GraphNodeRecord machine, AnimationGraphStateView states) = Machine();

        GraphStateRecord idle = states.Add(new Float2(0, 0))!;
        idle.Name = "Idle";
        GraphStateRecord walk = states.Add(new Float2(0, 0))!;
        walk.Name = "Walk";
        idle.Transitions.Add(new GraphTransitionRecord { To = "Walk" });

        states.Invalidate();
        states.Sync(graph);
        states.Rename(walk, "Run");

        Assert.Equal("Run", walk.Name);
        Assert.Equal("Run", idle.Transitions[0].To);
    }

    [Fact]
    public void DeletingAStateTakesTheTransitionsIntoItAndPromotesANewDefault()
    {
        (AnimationGraph graph, GraphNodeRecord machine, AnimationGraphStateView states) = Machine();

        GraphStateRecord idle = states.Add(new Float2(0, 0))!;
        idle.Name = "Idle";
        GraphStateRecord walk = states.Add(new Float2(0, 0))!;
        walk.Name = "Walk";
        walk.Transitions.Add(new GraphTransitionRecord { To = "Idle" });

        Assert.True(idle.IsDefault);

        states.Invalidate();
        states.Sync(graph);
        states.Delete(new[] { states.Nodes.First(n => n.Id == AnimationGraphStateView.StateId("Idle")) },
            System.Array.Empty<GraphConnection>());

        Assert.Single(machine.States);
        Assert.Empty(walk.Transitions);
        Assert.True(walk.IsDefault);
    }

    [Fact]
    public void AStateTakesOneTransitionToAnotherAndNoneToItself()
    {
        (AnimationGraph graph, GraphNodeRecord machine, AnimationGraphStateView states) = Machine();

        GraphStateRecord idle = states.Add(new Float2(0, 0))!;
        idle.Name = "Idle";
        GraphStateRecord walk = states.Add(new Float2(0, 0))!;
        walk.Name = "Walk";

        states.Invalidate();
        states.Sync(graph);

        string from = AnimationGraphStateView.StateId("Idle"), to = AnimationGraphStateView.StateId("Walk");
        Assert.True(states.CanConnect(new ConnectionRequest(from, "", to, "")));
        states.Connect(new ConnectionRequest(from, "", to, ""));
        states.Connect(new ConnectionRequest(from, "", to, ""));
        states.Connect(new ConnectionRequest(from, "", from, ""));

        Assert.Single(idle.Transitions);
        Assert.Equal("Walk", idle.Transitions[0].To);
        Assert.False(states.CanConnect(new ConnectionRequest(from, "", to, "")));
        Assert.False(states.CanConnect(new ConnectionRequest(from, "", from, "")));

        // The other way round is a transition of its own.
        Assert.True(states.CanConnect(new ConnectionRequest(to, "", from, "")));
    }

    private static (AnimationGraph, GraphNodeRecord, AnimationGraphStateView) Machine()
    {
        var graph = new AnimationGraph();
        var view = ViewOver(graph);
        GraphNodeRecord machine = view.Add(AnimationNodeIds.StateMachine, new Float2(0, 0))!;

        var states = new AnimationGraphStateView { Machine = machine };
        states.Sync(graph);
        return (graph, machine, states);
    }

    // ---- saving ------------------------------------------------------------------------------

    [Fact]
    public void AGraphSurvivesTheTextRoundTripTheWindowSavesWith()
    {
        var graph = new AnimationGraph();
        var view = ViewOver(graph);

        GraphNodeRecord pose = view.Add(AnimationNodeIds.Clip, new Float2(12, 34))!;
        GraphNodeRecord layers = view.Add(AnimationNodeIds.LayerBlend, new Float2(0, 0))!;
        view.Invalidate();
        view.Sync();

        view.Connect(Wire(pose.Id, layers.Id, 0));
        view.Connect(WireToSocket(pose.Id, layers.Id));
        layers.Inputs[2].Value = 0.75f;
        layers.Inputs[2].Flag = true;

        GraphNodeRecord machine = view.Add(AnimationNodeIds.StateMachine, new Float2(0, 0))!;
        machine.States.Add(new GraphStateRecord { Id = "idleState", Name = "Idle", IsDefault = true });
        graph.AddNode(AnimationNodeIds.StateOutput, "idleOut").Owner = "idleState";

        graph.RootNode = layers.Id;
        graph.Parameters.Add(new GraphParameterRecord { Name = "Speed", Kind = NodeValueKind.Number, Number = 1.5f });

        string text = Serializer.Serialize(typeof(object), graph).WriteToString();
        var loaded = Serializer.Deserialize<AnimationGraph>(EchoObject.ReadFromString(text));

        Assert.NotNull(loaded);
        Assert.Equal(graph.RootNode, loaded!.RootNode);
        Assert.Equal(graph.Nodes.Count, loaded.Nodes.Count);

        GraphNodeRecord loadedPose = loaded.Nodes.First(n => n.Id == pose.Id);
        Assert.Equal(new Float2(12, 34), loadedPose.EditorPosition);

        GraphNodeRecord loadedLayers = loaded.Nodes.First(n => n.Id == layers.Id);
        Assert.Equal(pose.Id, loadedLayers.Inputs[0].Node);
        Assert.Equal(0.75f, loadedLayers.Inputs[2].Value);
        Assert.True(loadedLayers.Inputs[2].Flag);

        Assert.Equal("Idle", loaded.Nodes.First(n => n.Type == AnimationNodeIds.StateMachine).States[0].Name);
        Assert.Equal("idleState", loaded.Nodes.First(n => n.Type == AnimationNodeIds.StateMachine).States[0].Id);
        Assert.Equal("idleState", loaded.Nodes.First(n => n.Id == "idleOut").Owner);
        Assert.Equal(1.5f, loaded.Parameters[0].Number);
    }

    [Fact]
    public void GroupsAndNotesSurviveTheSave()
    {
        var graph = new AnimationGraph();
        graph.Groups.Add(new GraphGroupRecord { Id = "g", Title = "Locomotion", Position = new Float2(10, 20), Size = new Float2(300, 200) });
        graph.Notes.Add(new GraphNoteRecord { Id = "n", Text = "Speed is in metres per second", Position = new Float2(5, 5) });

        string text = Serializer.Serialize(typeof(object), graph).WriteToString();
        var loaded = Serializer.Deserialize<AnimationGraph>(EchoObject.ReadFromString(text))!;

        Assert.Equal("Locomotion", loaded.Groups.Single().Title);
        Assert.Equal(new Float2(300, 200), loaded.Groups.Single().Size);
        Assert.Equal("Speed is in metres per second", loaded.Notes.Single().Text);
    }

    // ---- copying ----------------------------------------------------------------------------

    [Fact]
    public void CopyingSeveralNodesKeepsTheWiresBetweenThemAndDropsTheRest()
    {
        var graph = new AnimationGraph();
        var view = ViewOver(graph);

        GraphNodeRecord outside = view.Add(AnimationNodeIds.ConstFloat, new Float2(0, 0))!;
        GraphNodeRecord clip = view.Add(AnimationNodeIds.Clip, new Float2(0, 0))!;
        GraphNodeRecord speed = view.Add(AnimationNodeIds.SpeedScale, new Float2(0, 0))!;
        view.Invalidate();
        view.Sync();

        view.Connect(Wire(clip.Id, speed.Id, 0));
        view.Connect(Wire(outside.Id, speed.Id, 1));

        var copies = view.Copy(new[] { clip, speed }, new Float2(28, 28));

        GraphNodeRecord clipCopy = copies.Single(c => c.Type == AnimationNodeIds.Clip);
        GraphNodeRecord speedCopy = copies.Single(c => c.Type == AnimationNodeIds.SpeedScale);

        Assert.Equal(clipCopy.Id, speedCopy.Inputs[0].Node);
        Assert.Equal(string.Empty, speedCopy.Inputs[1].Node);
        Assert.Equal(speed.EditorPosition + new Float2(28, 28), speedCopy.EditorPosition);

        // The originals are untouched.
        Assert.Equal(clip.Id, speed.Inputs[0].Node);
        Assert.Equal(outside.Id, speed.Inputs[1].Node);
    }

    [Fact]
    public void ACopiedStateMachineCopiesItsStatesGraphs()
    {
        var graph = new AnimationGraph();
        var view = ViewOver(graph);
        (GraphNodeRecord machine, GraphStateRecord state, _) = MachineWithState(graph, view);

        GraphNodeRecord copy = view.Copy(new[] { machine }, new Float2(0, 0)).Single();
        GraphStateRecord copiedState = copy.States.Single();
        Assert.NotEqual(state.Id, copiedState.Id);

        // The copy's state has its own output, playing its own copy of the clip.
        GraphNodeRecord output = graph.OwnedNode(copiedState.Id, AnimationNodeIds.StateOutput)!;
        GraphNodeRecord clipCopy = graph.Find(output.Inputs[0].Node)!;
        Assert.Equal(copiedState.Id, clipCopy.Owner);
        Assert.Equal(AnimationNodeIds.Clip, clipCopy.Type);
    }

    [Fact]
    public void AStateMachinePastesIntoAnotherGraphWithWhatIsInsideIt()
    {
        var graph = new AnimationGraph();
        var view = ViewOver(graph);
        (GraphNodeRecord machine, _, _) = MachineWithState(graph, view);
        AnimationGraphView.Fragment clipboard = view.Capture(new[] { machine });

        var other = new AnimationGraph();
        var otherView = ViewOver(other);
        GraphNodeRecord pasted = otherView.Paste(clipboard, new Float2(0, 0)).Single();

        Assert.Equal(3, other.Nodes.Count);
        Assert.NotNull(other.OwnedNode(pasted.States.Single().Id, AnimationNodeIds.StateOutput));
    }

    [Fact]
    public void ACopiedStateMachineKeepsItsAnyState()
    {
        var graph = new AnimationGraph();
        var view = ViewOver(graph);
        (GraphNodeRecord machine, _, _) = MachineWithState(graph, view);
        machine.States.Add(new GraphStateRecord { Id = AnimationGraphView.NewStateId(), Name = "Any State", IsAny = true });

        GraphNodeRecord copy = view.Copy(new[] { machine }, new Float2(0, 0)).Single();

        Assert.Single(copy.States, s => s.IsAny);
    }

    // ---- parameter nodes --------------------------------------------------------------------

    [Theory]
    [InlineData(NodeValueKind.Number, NodePinKind.Number)]
    [InlineData(NodeValueKind.Flag, NodePinKind.Flag)]
    [InlineData(NodeValueKind.Vector, NodePinKind.Vector)]
    public void AParameterNodeOutputsWhatItsParameterWasDeclaredAs(NodeValueKind declared, NodePinKind expected)
    {
        var graph = new AnimationGraph();
        graph.Parameters.Add(new GraphParameterRecord { Name = "Input", Kind = declared });
        var view = ViewOver(graph);

        GraphNodeRecord parameter = view.Add(AnimationNodeIds.Parameter, new Float2(0, 0))!;
        parameter.Properties["Name"] = NodeValue.FromText("Input");

        Assert.Equal(expected, view.OutputKind(parameter));
    }

    [Fact]
    public void AFlagParameterWillNotWireIntoANumberPin()
    {
        var graph = new AnimationGraph();
        graph.Parameters.Add(new GraphParameterRecord { Name = "Grounded", Kind = NodeValueKind.Flag });
        var view = ViewOver(graph);

        GraphNodeRecord parameter = view.Add(AnimationNodeIds.Parameter, new Float2(0, 0))!;
        parameter.Properties["Name"] = NodeValue.FromText("Grounded");
        GraphNodeRecord blend = view.Add(AnimationNodeIds.Blend1D, new Float2(0, 0))!;
        view.Invalidate();
        view.Sync();

        Assert.False(view.Validate(Wire(parameter.Id, blend.Id, 0)));
    }
}

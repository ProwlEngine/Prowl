using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>Nodes written outside the library, working alongside the built in ones.</summary>
public class CustomNodes_Tests
{
    private static readonly Skeleton s_skeleton = TestSkeletons.MakeChain();

    // A pose node of its own: plays a clip, reports its events and root motion, and holds at the end.
    private sealed class PlayOnceDefinition : PoseNodeDefinition
    {
        public PlayOnceDefinition(AnimationClipBase clip) => Clip = clip;

        public AnimationClipBase Clip { get; }

        public override GraphNodeInstance CreateInstance() => new Instance(this);

        private sealed class Instance : PoseNodeInstance
        {
            private readonly PlayOnceDefinition _def;
            private ClipCursor _cursor;
            private bool _first;

            public Instance(PlayOnceDefinition def) => _def = def;

            public override SyncTrack SyncTrack => _def.Clip.SyncTrack;

            public override void Bind(GraphBindContext context) => Pose = new Pose(context.Skeleton);

            protected override void OnInitialize(GraphContext context, SyncTrackTime? initialTime)
            {
                _cursor = default;
                _first = true;
                Duration = _def.Clip.Duration;
            }

            protected override void OnUpdate(GraphContext context)
            {
                PlaybackSpan span = _cursor.Advance(context.DeltaTime, _def.Clip.Duration, loop: false, includeStart: _first);
                _first = false;

                PreviousTime = span.From;
                NormalizedTime = span.To;
                _def.Clip.GetPose(span.To, Pose);
                RootMotionDelta = _def.Clip.GetRootMotionDelta(span);
                _def.Clip.SampleEvents(span, context.Events, context.IsActiveBranch, NodeIndex);
            }
        }
    }

    // A value node of its own: counts the frames it has been updated for.
    private sealed class FrameCountDefinition : ValueNodeDefinition
    {
        public override AnimationValueType ValueType => AnimationValueType.Float;

        public override GraphNodeInstance CreateInstance() => new Instance();

        private sealed class Instance : ValueNodeInstance
        {
            private int _frames;

            protected override void OnInitialize(GraphContext context) => _frames = 0;

            protected override ParameterValue Compute(GraphContext context) => ParameterValue.FromFloat(++_frames);
        }
    }

    [Fact]
    public void ACustomPoseNode_PlaysInsideTheGraph()
    {
        var graph = new AnimationGraph();
        graph.SetRoot(graph.AddNode(new PlayOnceDefinition(TestClips.Ramp(s_skeleton, endZ: 10f))));
        AnimationGraphInstance instance = graph.CreateInstance(s_skeleton);

        // The clip runs a second, so 30 steps of a thirtieth reach the end and it holds there.
        for (int i = 0; i < 35; i++)
            instance.Update(1f / 30f, Transform3D.Identity);

        Assert.Equal(10f, instance.Pose.GetTransform(0).position.Z, 2);
        Assert.Equal(1f, instance.NormalizedTime, 3);
    }

    [Fact]
    public void ACustomPoseNode_ReportsRootMotionAndEvents()
    {
        AnimationClip clip = TestClips.Ramp(s_skeleton, rootTravel: 2f, events: new IdEvent(new StringID("step"), 0.5f));
        var graph = new AnimationGraph();
        graph.SetRoot(graph.AddNode(new PlayOnceDefinition(clip)));
        AnimationGraphInstance instance = graph.CreateInstance(s_skeleton);

        float travelled = 0f;
        int steps = 0;
        for (int i = 0; i < 30; i++)
        {
            instance.Update(1f / 30f, Transform3D.Identity);
            travelled += instance.RootMotionDelta.position.Z;
            steps += TestClips.CountId(instance.Events, "step");
        }

        Assert.Equal(2f, travelled, 2);
        Assert.Equal(1, steps);
    }

    [Fact]
    public void ACustomNode_WorksAsAChildOfTheBuiltInOnes()
    {
        var graph = new AnimationGraph();
        int custom = graph.AddNode(new PlayOnceDefinition(TestClips.Const(s_skeleton, 8f)));
        int other = graph.AddClip(TestClips.Const(s_skeleton, 0f));
        int weight = graph.AddFloatParameter("Weight", 0.5f);
        graph.SetRoot(graph.AddWeightedBlend(new[] { new WeightedPose(other, -1, 1f), new WeightedPose(custom, weight) }));
        AnimationGraphInstance instance = graph.CreateInstance(s_skeleton);

        instance.Update(1f / 60f, Transform3D.Identity);
        instance.Update(1f / 60f, Transform3D.Identity);

        Assert.Equal(8f / 3f, instance.Pose.GetTransform(0).position.Z, 3);
    }

    [Fact]
    public void ACustomValueNode_DrivesABuiltInNode()
    {
        var graph = new AnimationGraph();
        int frames = graph.AddNode(new FrameCountDefinition());
        int selector = graph.AddSelector(frames, new[]
        {
            graph.AddClip(TestClips.Const(s_skeleton, 0f)),
            graph.AddClip(TestClips.Const(s_skeleton, 1f)),
            graph.AddClip(TestClips.Const(s_skeleton, 2f)),
        });
        graph.SetRoot(selector);
        AnimationGraphInstance instance = graph.CreateInstance(s_skeleton);

        instance.Update(1f / 60f, Transform3D.Identity);
        float first = instance.Pose.GetTransform(0).position.Z;
        instance.Update(1f / 60f, Transform3D.Identity);

        Assert.Equal(1f, first, 3);
        Assert.Equal(2f, instance.Pose.GetTransform(0).position.Z, 3);
    }

    // Custom nodes get the same checks as the built in ones.
    [Fact]
    public void ACustomNode_IsValidatedLikeAnyOther()
    {
        var graph = new AnimationGraph();
        int custom = graph.AddNode(new PlayOnceDefinition(TestClips.Const(s_skeleton, 1f)));
        graph.SetRoot(graph.AddWeightedBlend(new[] { new WeightedPose(custom), new WeightedPose(custom) }));

        Assert.Throws<GraphValidationException>(() => graph.CreateInstance(s_skeleton));
    }
}

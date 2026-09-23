using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Character Motion node: speed, heading and turn rate read from the world transform.</summary>
public class N_CharacterMotion_Tests
{
    [Fact]
    public void CharacterMotion_ReadsSpeedHeadingAndTurnFromTheWorldTransform()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int speed = graph.AddCharacterMotion(CharacterMotionValue.Speed, halfLife: 0f);
        int heading = graph.AddCharacterMotion(CharacterMotionValue.Direction, halfLife: 0f);
        int turn = graph.AddCharacterMotion(CharacterMotionValue.TurnRate, halfLife: 0f);
        graph.SetRoot(graph.AddReferencePose());

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        float Value(int node) => instance.EvaluateValueNode(node).AsFloat();

        // Moving to the character's right at 2 units a second, then turning on the spot at 90 degrees a second.
        const float dt = 0.1f;
        for (int frame = 0; frame <= 3; frame++)
        {
            instance.Update(dt, new Transform3D(new Float3(2f * dt * frame, 0f, 0f), Quaternion.Identity, Float3.One));
            Value(speed); Value(heading); Value(turn);
        }

        Assert.Equal(2f, Value(speed), 2);
        Assert.Equal(90f, Value(heading), 1);

        Float3 still = new(0.6f, 0f, 0f);
        for (int frame = 1; frame <= 3; frame++)
        {
            instance.Update(dt, new Transform3D(still, Quaternion.AxisAngle(Float3.UnitY, MathF.PI / 2f * dt * frame), Float3.One));
            Value(turn);
        }

        Assert.Equal(90f, Value(turn), 0);
    }
}

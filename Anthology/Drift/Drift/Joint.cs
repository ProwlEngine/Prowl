using System;
using System.Numerics;

namespace Prowl.Drift
{
    public enum JointType
    {
        Angle = 0,
        Revolute = 1,
        Weld = 2,
        Wheel = 3,
        Prismatic = 4,
        Distance = 5,
        Rope = 6,
        Mouse = 7
    }

    public abstract class Joint
    {
        private static int _idCounter = 0;
        public readonly int Id;

        public Space? Space { get; internal set; }

        public JointType Type { get; }
        public Body Body1 { get; }
        public Body Body2 { get; }

        public bool CollideConnected { get; set; }
        public float MaxForce { get; set; } = 9_999_999_999f;
        public bool Breakable { get; set; }

        // Anchors in local coordinates
        protected Vector2 Anchor1;
        protected Vector2 Anchor2;

        protected Joint(JointType type, Body body1, Body body2, bool collideConnected)
        {
            Id = _idCounter++;
            Type = type;
            Body1 = body1;
            Body2 = body2;
            CollideConnected = collideConnected;
        }

        public virtual Vector2 GetWorldAnchor1() => Body1.TransformPoint(Anchor1);
        public virtual Vector2 GetWorldAnchor2() => Body2.TransformPoint(Anchor2);

        public virtual void SetWorldAnchor1(Vector2 anchor1) => Anchor1 = Body1.InverseTransformPoint(anchor1);
        public virtual void SetWorldAnchor2(Vector2 anchor2) => Anchor2 = Body2.InverseTransformPoint(anchor2);

        // Solver interface
        public abstract void InitSolver(float dt, bool warmStarting);
        public abstract void SolveVelocityConstraints();
        public abstract bool SolvePositionConstraints();

        public abstract Vector2 GetReactionForce(float dtInv);
        public abstract float GetReactionTorque(float dtInv);

        // Tolerances
        public readonly static float LINEAR_SLOP = 0.0008f;
        public readonly static float ANGULAR_SLOP = 2 * MathUtil.Deg2Rad;
        public readonly static float MAX_LINEAR_CORRECTION = 0.5f;
        public readonly static float MAX_ANGULAR_CORRECTION = 8 * MathUtil.Deg2Rad;

        public readonly static int LIMIT_STATE_INACTIVE = 0;
        public readonly static int LIMIT_STATE_AT_LOWER = 1;
        public readonly static int LIMIT_STATE_AT_UPPER = 2;
        public readonly static int LIMIT_STATE_EQUAL_LIMITS = 3;
    }
}

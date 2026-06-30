using Prowl.Drift;
using System;
using System.Numerics;

namespace Drift.Joints
{
    public class AngleJoint : Joint
    {
        private readonly float _refAngle;
        private float _lambdaAcc;
        private float _effectiveMass;

        public AngleJoint(Body body1, Body body2)
            : base(JointType.Angle, body1, body2, true)
        {
            Anchor1 = Vector2.Zero;
            Anchor2 = Vector2.Zero;
            _refAngle = body2.Angle - body1.Angle;
            _lambdaAcc = 0;
        }

        public override void SetWorldAnchor1(Vector2 anchor1) => Anchor1 = Vector2.Zero;
        public override void SetWorldAnchor2(Vector2 anchor2) => Anchor2 = Vector2.Zero;

        public override void InitSolver(float dt, bool warmStarting)
        {
            float emInv = Body1.InertiaInv + Body2.InertiaInv;
            _effectiveMass = emInv == 0 ? 0 : 1f / emInv;

            if (warmStarting)
            {
                Body1.AngularVelocity -= _lambdaAcc * Body1.InertiaInv;
                Body2.AngularVelocity += _lambdaAcc * Body2.InertiaInv;
            }
            else
            {
                _lambdaAcc = 0;
            }
        }

        public override void SolveVelocityConstraints()
        {
            float cdot = Body2.AngularVelocity - Body1.AngularVelocity;
            float lambda = -_effectiveMass * cdot;
            _lambdaAcc += lambda;

            Body1.AngularVelocity -= lambda * Body1.InertiaInv;
            Body2.AngularVelocity += lambda * Body2.InertiaInv;
        }

        public override bool SolvePositionConstraints()
        {
            float c = Body2.Angle - Body1.Angle - _refAngle;
            float correction = MathUtil.Clamp(c, -MAX_ANGULAR_CORRECTION, MAX_ANGULAR_CORRECTION);
            float lambdaDt = _effectiveMass * -correction;

            Body1.Angle -= lambdaDt * Body1.InertiaInv;
            Body2.Angle += lambdaDt * Body2.InertiaInv;

            return Math.Abs(c) < ANGULAR_SLOP;
        }

        public override Vector2 GetReactionForce(float dtInv) => Vector2.Zero;
        public override float GetReactionTorque(float dtInv) => _lambdaAcc * dtInv;
    }
}

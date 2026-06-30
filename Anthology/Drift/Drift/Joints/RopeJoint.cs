using Prowl.Drift;
using System;
using System.Numerics;

namespace Drift.Joints
{
    public class RopeJoint : Joint
    {
        private float _maxDistance;
        private float _distance;
        private Vector2 _u;

        private Vector2 _r1, _r2;
        private float _s1, _s2;

        private float _em;
        private float _lambdaAcc;

        private float _cdt;

        public RopeJoint(Body b1, Body b2, Vector2 anchor1, Vector2 anchor2)
            : base(JointType.Rope, b1, b2, true)
        {
            Anchor1 = Body1.InverseTransformPoint(anchor1);
            Anchor2 = Body2.InverseTransformPoint(anchor2);
            _maxDistance = Vector2.Distance(anchor1, anchor2);
        }

        public override void SetWorldAnchor1(Vector2 a1)
        {
            Anchor1 = Body1.InverseTransformPoint(a1);
            _maxDistance = Vector2.Distance(a1, GetWorldAnchor2());
        }

        public override void SetWorldAnchor2(Vector2 a2)
        {
            Anchor2 = Body2.InverseTransformPoint(a2);
            _maxDistance = Vector2.Distance(a2, GetWorldAnchor1());
        }

        public override void InitSolver(float dt, bool warmStarting)
        {
            _r1 = Body1.RotatePoint(Anchor1 - Body1.Centroid);
            _r2 = Body2.RotatePoint(Anchor2 - Body2.Centroid);

            var d = Body2.Position + _r2 - (Body1.Position + _r1);
            _distance = d.Length();

            float c = _distance - _maxDistance;
            if (c > 0)
            {
                _cdt = 0;
            }
            else
            {
                _cdt = c / dt;
            }

            _u = _distance > LINEAR_SLOP ? d / _distance : Vector2.Zero;

            _s1 = MathUtil.Cross(_r1, _u);
            _s2 = MathUtil.Cross(_r2, _u);

            float emInv = Body1.MassInv + Body2.MassInv + Body1.InertiaInv * _s1 * _s1 + Body2.InertiaInv * _s2 * _s2;
            _em = emInv == 0 ? 0 : 1f / emInv;

            if (warmStarting)
            {
                var impulse = _u * _lambdaAcc;

                Body1.LinearVelocity -= impulse * Body1.MassInv;
                Body1.AngularVelocity -= _s1 * _lambdaAcc * Body1.InertiaInv;

                Body2.LinearVelocity += impulse * Body2.MassInv;
                Body2.AngularVelocity += _s2 * _lambdaAcc * Body2.InertiaInv;
            }
            else
            {
                _lambdaAcc = 0;
            }
        }

        public override void SolveVelocityConstraints()
        {
            float cdot = Vector2.Dot(_u, Body2.LinearVelocity - Body1.LinearVelocity) + _s2 * Body2.AngularVelocity - _s1 * Body1.AngularVelocity;
            float lambda = -_em * (cdot + _cdt);

            float old = _lambdaAcc;
            _lambdaAcc = MathF.Min(old + lambda, 0);
            lambda = _lambdaAcc - old;

            var impulse = _u * lambda;

            Body1.LinearVelocity -= impulse * Body1.MassInv;
            Body1.AngularVelocity -= _s1 * lambda * Body1.InertiaInv;

            Body2.LinearVelocity += impulse * Body2.MassInv;
            Body2.AngularVelocity += _s2 * lambda * Body2.InertiaInv;
        }

        public override bool SolvePositionConstraints()
        {
            var r1 = MathUtil.Rotate(Anchor1 - Body1.Centroid, Body1.Angle);
            var r2 = MathUtil.Rotate(Anchor2 - Body2.Centroid, Body2.Angle);

            var d = Body2.Position + r2 - (Body1.Position + r1);
            float dist = d.Length();
            var u = d / dist;

            float c = dist - _maxDistance;
            float correction = MathUtil.Clamp(c, 0, MAX_LINEAR_CORRECTION);

            float s1 = MathUtil.Cross(r1, u);
            float s2 = MathUtil.Cross(r2, u);
            float emInv = Body1.MassInv + Body2.MassInv + Body1.InertiaInv * s1 * s1 + Body2.InertiaInv * s2 * s2;
            float lambdaDt = emInv == 0 ? 0 : -correction / emInv;

            var impulseDt = u * lambdaDt;

            Body1.Position -= impulseDt * Body1.MassInv;
            Body1.Angle -= s1 * lambdaDt * Body1.InertiaInv;

            Body2.Position += impulseDt * Body2.MassInv;
            Body2.Angle += s2 * lambdaDt * Body2.InertiaInv;

            return c < LINEAR_SLOP;
        }

        public override Vector2 GetReactionForce(float dtInv) => _u * (_lambdaAcc * dtInv);
        public override float GetReactionTorque(float dtInv) => 0;
    }
}

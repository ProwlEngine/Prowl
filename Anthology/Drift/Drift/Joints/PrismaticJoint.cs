using Prowl.Drift;
using System;
using System.Numerics;

namespace Drift.Joints
{
    public class PrismaticJoint : Joint
    {
        private Vector2 _nLocal;
        private float _da;

        private Vector2 _r1, _r2, _r1d;
        private Vector2 _n;
        private float _s1, _s2;

        private float _k11, _k12, _k22;
        private Vector2 _lambdaAcc = Vector2.Zero;

        public PrismaticJoint(Body b1, Body b2, Vector2 anchor1, Vector2 anchor2)
            : base(JointType.Prismatic, b1, b2, true)
        {
            Anchor1 = Body1.InverseTransformPoint(anchor1);
            Anchor2 = Body2.InverseTransformPoint(anchor2);

            var d = anchor2 - anchor1;
            _nLocal = Body1.InverseRotatePoint(Vector2.Normalize(MathUtil.Perp(d)));
            _da = b2.Angle - b1.Angle;
        }

        public override void SetWorldAnchor1(Vector2 a1)
        {
            Anchor1 = Body1.InverseTransformPoint(a1);
            var d = GetWorldAnchor2() - a1;
            _nLocal = Body1.InverseRotatePoint(Vector2.Normalize(MathUtil.Perp(d)));
        }

        public override void SetWorldAnchor2(Vector2 a2)
        {
            Anchor2 = Body2.InverseTransformPoint(a2);
            var d = a2 - GetWorldAnchor1();
            _nLocal = Body1.InverseRotatePoint(Vector2.Normalize(MathUtil.Perp(d)));
        }

        public override void InitSolver(float dt, bool warmStarting)
        {
            _r1 = Body1.RotatePoint(Anchor1 - Body1.Centroid);
            _r2 = Body2.RotatePoint(Anchor2 - Body2.Centroid);

            var p1 = Body1.Position + _r1;
            var p2 = Body2.Position + _r2;
            var d = p2 - p1;
            _r1d = _r1 + d;

            _n = Vector2.Normalize(MathUtil.Perp(d));

            _s1 = MathUtil.Cross(_r1d, _n);
            _s2 = MathUtil.Cross(_r2, _n);

            _k11 = Body1.MassInv + Body2.MassInv + Body1.InertiaInv * _s1 * _s1 + Body2.InertiaInv * _s2 * _s2;
            _k12 = Body1.InertiaInv * _s1 + Body2.InertiaInv * _s2;
            _k22 = Body1.InertiaInv + Body2.InertiaInv;

            if (warmStarting)
            {
                var impulse = _n * _lambdaAcc.X;

                Body1.LinearVelocity -= impulse * Body1.MassInv;
                Body1.AngularVelocity -= (_s1 * _lambdaAcc.X + _lambdaAcc.Y) * Body1.InertiaInv;

                Body2.LinearVelocity += impulse * Body2.MassInv;
                Body2.AngularVelocity += (_s2 * _lambdaAcc.X + _lambdaAcc.Y) * Body2.InertiaInv;
            }
            else
            {
                _lambdaAcc = Vector2.Zero;
            }
        }

        public override void SolveVelocityConstraints()
        {
            float cdot1 = Vector2.Dot(_n, Body2.LinearVelocity - Body1.LinearVelocity) + _s2 * Body2.AngularVelocity - _s1 * Body1.AngularVelocity;
            float cdot2 = Body2.AngularVelocity - Body1.AngularVelocity;

            var lambda = MathUtil.Solve(_k11, _k12, _k12, _k22, new Vector2(-cdot1, -cdot2));
            _lambdaAcc += lambda;

            var impulse = _n * lambda.X;

            Body1.LinearVelocity -= impulse * Body1.MassInv;
            Body1.AngularVelocity -= (_s1 * lambda.X + lambda.Y) * Body1.InertiaInv;

            Body2.LinearVelocity += impulse * Body2.MassInv;
            Body2.AngularVelocity += (_s2 * lambda.X + lambda.Y) * Body2.InertiaInv;
        }

        public override bool SolvePositionConstraints()
        {
            var r1 = MathUtil.Rotate(Anchor1 - Body1.Centroid, Body1.Angle);
            var r2 = MathUtil.Rotate(Anchor2 - Body2.Centroid, Body2.Angle);

            var p1 = Body1.Position + r1;
            var p2 = Body2.Position + r2;
            var d = p2 - p1;
            var r1d = r1 + d;

            var n = MathUtil.Rotate(_nLocal, Body1.Angle);

            float c1 = Vector2.Dot(n, d);
            float c2 = Body2.Angle - Body1.Angle - _da;

            var correction = new Vector2(
                MathUtil.Clamp(c1, -MAX_LINEAR_CORRECTION, MAX_LINEAR_CORRECTION),
                MathUtil.Clamp(c2, -MAX_ANGULAR_CORRECTION, MAX_ANGULAR_CORRECTION)
            );

            float s1 = MathUtil.Cross(r1d, n);
            float s2 = MathUtil.Cross(r2, n);
            float k11 = Body1.MassInv + Body2.MassInv + Body1.InertiaInv * s1 * s1 + Body2.InertiaInv * s2 * s2;
            float k12 = Body1.InertiaInv * s1 + Body2.InertiaInv * s2;
            float k22 = Body1.InertiaInv + Body2.InertiaInv;

            var lambdaDt = MathUtil.Solve(k11, k12, k12, k22, -correction);

            var impulseDt = n * lambdaDt.X;

            Body1.Position -= impulseDt * Body1.MassInv;
            Body1.Angle -= (MathUtil.Cross(r1d, impulseDt) + lambdaDt.Y) * Body1.InertiaInv;

            Body2.Position += impulseDt * Body2.MassInv;
            Body2.Angle += (MathUtil.Cross(r2, impulseDt) + lambdaDt.Y) * Body2.InertiaInv;

            return Math.Abs(c1) <= LINEAR_SLOP && Math.Abs(c2) <= ANGULAR_SLOP;
        }

        public override Vector2 GetReactionForce(float dtInv) => _n * (_lambdaAcc.X * dtInv);
        public override float GetReactionTorque(float dtInv) => _lambdaAcc.Y * dtInv;
    }
}

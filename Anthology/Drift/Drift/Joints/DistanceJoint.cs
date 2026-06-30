using System;
using System.Numerics;

namespace Prowl.Drift
{
    public class DistanceJoint : Joint
    {
        private float _restLength;
        private float _frequencyHz;
        private float _dampingRatio;

        private float _gamma;
        private float _betaC;
        private float _lambdaAcc;
        private float _effectiveMass;

        private Vector2 _r1, _r2;
        private Vector2 _u;
        private float _s1, _s2;

        public DistanceJoint(Body body1, Body body2, Vector2 anchor1, Vector2 anchor2)
            : base(JointType.Distance, body1, body2, true)
        {
            Anchor1 = Body1.InverseTransformPoint(anchor1);
            Anchor2 = Body2.InverseTransformPoint(anchor2);
            _restLength = Vector2.Distance(anchor1, anchor2);
            _lambdaAcc = 0;
        }

        public void SetSpringFrequencyHz(float frequencyHz) => _frequencyHz = frequencyHz;
        public void SetSpringDampingRatio(float dampingRatio) => _dampingRatio = dampingRatio;

        public override void SetWorldAnchor1(Vector2 anchor1)
        {
            Anchor1 = Body1.InverseTransformPoint(anchor1);
            _restLength = Vector2.Distance(anchor1, GetWorldAnchor2());
        }

        public override void SetWorldAnchor2(Vector2 anchor2)
        {
            Anchor2 = Body2.InverseTransformPoint(anchor2);
            _restLength = Vector2.Distance(anchor2, GetWorldAnchor1());
        }

        public override void InitSolver(float dt, bool warmStarting)
        {
            _r1 = Body1.RotatePoint(Anchor1 - Body1.Centroid);
            _r2 = Body2.RotatePoint(Anchor2 - Body2.Centroid);

            Vector2 d = (Body2.Position + _r2) - (Body1.Position + _r1);
            float dist = d.Length();

            _u = dist > LINEAR_SLOP ? d / dist : Vector2.Zero;
            _s1 = MathUtil.Cross(_r1, _u);
            _s2 = MathUtil.Cross(_r2, _u);

            float emInv = Body1.MassInv + Body2.MassInv + Body1.InertiaInv * _s1 * _s1 + Body2.InertiaInv * _s2 * _s2;
            _effectiveMass = emInv == 0 ? 0 : 1f / emInv;

            if (_frequencyHz > 0)
            {
                float omega = 2f * (float)Math.PI * _frequencyHz;
                float k = _effectiveMass * omega * omega;
                float c = _effectiveMass * 2f * _dampingRatio * omega;

                _gamma = (c + k * dt) * dt;
                _gamma = _gamma == 0 ? 0 : 1f / _gamma;
                float beta = dt * k * _gamma;

                float pc = dist - _restLength;
                _betaC = beta * pc;

                emInv += _gamma;
                _effectiveMass = emInv == 0 ? 0 : 1f / emInv;
            }
            else
            {
                _gamma = 0;
                _betaC = 0;
            }

            if (warmStarting)
            {
                Vector2 impulse = _u * _lambdaAcc;
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
            float soft = _betaC + _gamma * _lambdaAcc;
            float lambda = -_effectiveMass * (cdot + soft);
            _lambdaAcc += lambda;

            Vector2 impulse = _u * lambda;
            Body1.LinearVelocity -= impulse * Body1.MassInv;
            Body1.AngularVelocity -= _s1 * lambda * Body1.InertiaInv;

            Body2.LinearVelocity += impulse * Body2.MassInv;
            Body2.AngularVelocity += _s2 * lambda * Body2.InertiaInv;
        }

        public override bool SolvePositionConstraints()
        {
            if (_frequencyHz > 0)
                return true;

            Vector2 r1 = MathUtil.Rotate(Anchor1 - Body1.Centroid, Body1.Angle);
            Vector2 r2 = MathUtil.Rotate(Anchor2 - Body2.Centroid, Body2.Angle);

            Vector2 d = (Body2.Position + r2) - (Body1.Position + r1);
            float dist = d.Length();
            Vector2 u = d / dist;

            float c = dist - _restLength;
            float correction = MathUtil.Clamp(c, -MAX_LINEAR_CORRECTION, MAX_LINEAR_CORRECTION);

            float s1 = MathUtil.Cross(r1, u);
            float s2 = MathUtil.Cross(r2, u);
            float emInv = Body1.MassInv + Body2.MassInv + Body1.InertiaInv * s1 * s1 + Body2.InertiaInv * s2 * s2;
            float lambdaDt = emInv == 0 ? 0 : -correction / emInv;

            Vector2 impulseDt = u * lambdaDt;
            Body1.Position -= impulseDt * Body1.MassInv;
            Body1.Angle -= s1 * lambdaDt * Body1.InertiaInv;

            Body2.Position += impulseDt * Body2.MassInv;
            Body2.Angle += s2 * lambdaDt * Body2.InertiaInv;

            return Math.Abs(c) < LINEAR_SLOP;
        }

        public override Vector2 GetReactionForce(float dtInv) => _u * (_lambdaAcc * dtInv);
        public override float GetReactionTorque(float dtInv) => 0;
    }
}

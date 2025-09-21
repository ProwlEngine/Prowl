using Prowl.Drift;
using System;

namespace Drift.Joints
{
    public class MouseJoint : Joint
    {
        private Vec2 _r2;
        private float _k11, _k12, _k22;
        private Vec2 _lambdaAcc = Vec2.Zero;

        private float _gamma;
        private Vec2 _betaC;

        private float _frequencyHz = 5f;
        private float _dampingRatio = 0.9f;

        private float _maxImpulse;

        public MouseJoint(Body mouseBody, Body body, Vec2 anchor)
            : base(JointType.Mouse, mouseBody, body, true)
        {
            Anchor1 = Body1.InverseTransformPoint(anchor);
            Anchor2 = Body2.InverseTransformPoint(anchor);
        }

        public void SetSpringFrequencyHz(float hz) => _frequencyHz = hz;
        public void SetSpringDampingRatio(float ratio) => _dampingRatio = ratio;

        public override void InitSolver(float dt, bool warmStarting)
        {
            _maxImpulse = MaxForce * dt;

            var b2 = Body2;

            float omega = 2 * MathF.PI * _frequencyHz;
            float k = b2.Mass * omega * omega;
            float d = b2.Mass * 2f * _dampingRatio * omega;

            _gamma = (d + k * dt) * dt;
            _gamma = _gamma == 0 ? 0 : 1f / _gamma;
            float beta = dt * k * _gamma;

            _r2 = b2.RotatePoint(Anchor2 - b2.Centroid);

            float r2x = _r2.X, r2y = _r2.Y;
            float i2Inv = b2.InertiaInv;

            _k11 = b2.MassInv + r2y * r2y * i2Inv + _gamma;
            _k12 = -r2x * r2y * i2Inv;
            _k22 = b2.MassInv + r2x * r2x * i2Inv + _gamma;

            var c = b2.Position + _r2 - Body1.Position;
            _betaC = c * beta;

            b2.AngularVelocity *= 0.98f;

            if (warmStarting)
            {
                b2.LinearVelocity += _lambdaAcc * b2.MassInv;
                b2.AngularVelocity += Vec2.Cross(_r2, _lambdaAcc) * b2.InertiaInv;
            }
            else
            {
                _lambdaAcc = Vec2.Zero;
            }
        }

        public override void SolveVelocityConstraints()
        {
            var b2 = Body2;

            var cdot = b2.LinearVelocity + Vec2.Perp(_r2) * b2.AngularVelocity;
            var soft = _betaC + _lambdaAcc * _gamma;
            var lambda = MathUtil.Solve(_k11, _k12, _k12, _k22, Vec2.Neg(cdot + soft));

            var old = _lambdaAcc;
            _lambdaAcc += lambda;

            if (_lambdaAcc.LengthSquared() > _maxImpulse * _maxImpulse)
                _lambdaAcc *= _maxImpulse / _lambdaAcc.Length();

            lambda = _lambdaAcc - old;

            b2.LinearVelocity += lambda * b2.MassInv;
            b2.AngularVelocity += Vec2.Cross(_r2, lambda) * b2.InertiaInv;
        }

        public override bool SolvePositionConstraints() => true;

        public override Vec2 GetReactionForce(float dtInv) => _lambdaAcc * dtInv;
        public override float GetReactionTorque(float dtInv) => 0;
    }
}

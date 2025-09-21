using Prowl.Drift;
using System;
using System.Numerics;

namespace Drift.Joints
{
    public class WheelJoint : Joint
    {
        private Vector2 anchor1, anchor2;
        private Vector2 r1, r2, r1d;
        private Vector2 u, n;
        private float sn1, sn2, su1, su2;
        private float gamma, beta_c;
        private float lambdaAcc, springLambdaAcc, motorLambdaAcc;
        private float em, springEm, motorEm;

        private Vector2 uLocal, nLocal;

        public float RestLength { get; private set; }
        public bool MotorEnabled { get; private set; }
        public float MotorSpeed { get; private set; }
        public float MaxMotorTorque { get; private set; }
        public float MaxMotorImpulse { get; private set; }
        public float FrequencyHz { get; private set; }
        public float DampingRatio { get; private set; }

        public WheelJoint(Body body1, Body body2, Vector2 a1, Vector2 a2)
            : base(JointType.Wheel, body1, body2, true)
        {
            anchor1 = body1.InverseTransformPoint(a1);
            anchor2 = body2.InverseTransformPoint(a2);

            var d = a2 - a1;
            RestLength = d.Length();

            uLocal = body1.InverseRotatePoint(Vector2.Normalize(d));
            nLocal = MathUtil.Perp(uLocal);

            lambdaAcc = 0;
            motorLambdaAcc = 0;
            springLambdaAcc = 0;
            FrequencyHz = 0;
            DampingRatio = 0;
        }

        public void SetSpringFrequencyHz(float f) => FrequencyHz = f;
        public void SetSpringDampingRatio(float d) => DampingRatio = d;
        public void EnableMotor(bool f) => MotorEnabled = f;
        public void SetMotorSpeed(float s) => MotorSpeed = s;
        public void SetMaxMotorTorque(float t) => MaxMotorTorque = t;

        public override void InitSolver(float dt, bool warmStarting)
        {
            var b1 = Body1;
            var b2 = Body2;

            r1 = b1.RotatePoint(anchor1 - b1.Centroid);
            r2 = b2.RotatePoint(anchor2 - b2.Centroid);

            var p1 = b1.Position + r1;
            var p2 = b2.Position + r2;
            var d = p2 - p1;
            r1d = r1 + d;

            n = MathUtil.Rotate(nLocal, b1.Angle);

            sn1 = MathUtil.Cross(r1d, n);
            sn2 = MathUtil.Cross(r2, n);

            var emInv = b1.MassInv + b2.MassInv + b1.InertiaInv * sn1 * sn1 + b2.InertiaInv * sn2 * sn2;
            em = emInv > 0 ? 1 / emInv : 0;

            if (FrequencyHz > 0)
            {
                u = MathUtil.Rotate(uLocal, b1.Angle);
                su1 = MathUtil.Cross(r1d, u);
                su2 = MathUtil.Cross(r2, u);

                var springEmInv = b1.MassInv + b2.MassInv + b1.InertiaInv * su1 * su1 + b2.InertiaInv * su2 * su2;
                springEm = springEmInv == 0 ? 0 : 1 / springEmInv;

                float omega = 2 * MathF.PI * FrequencyHz;
                float k = springEm * omega * omega;
                float c = springEm * 2 * DampingRatio * omega;

                gamma = (c + k * dt) * dt;
                gamma = gamma == 0 ? 0 : 1 / gamma;
                float beta = dt * k * gamma;

                float pc = Vector2.Dot(d, u) - RestLength;
                beta_c = beta * pc;

                springEmInv += gamma;
                springEm = springEmInv == 0 ? 0 : 1 / springEmInv;
            }
            else
            {
                gamma = 0;
                beta_c = 0;
                springLambdaAcc = 0;
            }

            if (MotorEnabled)
            {
                MaxMotorImpulse = MaxMotorTorque * dt;
                var motorEmInv = b1.InertiaInv + b2.InertiaInv;
                motorEm = motorEmInv > 0 ? 1 / motorEmInv : 0;
            }
            else
            {
                motorEm = 0;
                motorLambdaAcc = 0;
            }

            if (warmStarting)
            {
                var linearImpulse = n * lambdaAcc;
                float angularImpulse1 = sn1 * lambdaAcc + motorLambdaAcc;
                float angularImpulse2 = sn2 * lambdaAcc + motorLambdaAcc;

                if (FrequencyHz > 0)
                {
                    linearImpulse += u * springLambdaAcc;
                    angularImpulse1 += su1 * springLambdaAcc;
                    angularImpulse2 += su2 * springLambdaAcc;
                }

                //b1.LinearVelocity.Mad(linearImpulse, -b1.MassInv);
                b1.LinearVelocity = MathUtil.Mad(b1.LinearVelocity, linearImpulse, -b1.MassInv);
                b1.AngularVelocity -= angularImpulse1 * b1.InertiaInv;

                //b2.LinearVelocity.Mad(linearImpulse, b2.MassInv);
                b2.LinearVelocity = MathUtil.Mad(b2.LinearVelocity, linearImpulse, b2.MassInv);
                b2.AngularVelocity += angularImpulse2 * b2.InertiaInv;
            }
            else
            {
                lambdaAcc = 0;
                springLambdaAcc = 0;
                motorLambdaAcc = 0;
            }
        }

        public override void SolveVelocityConstraints()
        {
            var b1 = Body1;
            var b2 = Body2;

            if (FrequencyHz > 0)
            {
                float cdot = Vector2.Dot(u, b2.LinearVelocity - b1.LinearVelocity) + su2 * b2.AngularVelocity - su1 * b1.AngularVelocity;
                float soft = beta_c + gamma * springLambdaAcc;
                float lambda = -springEm * (cdot + soft);
                springLambdaAcc += lambda;

                var impulse = u * lambda;
                //b1.LinearVelocity.Mad(impulse, -b1.MassInv);
                b1.LinearVelocity = MathUtil.Mad(b1.LinearVelocity, impulse, -b1.MassInv);
                b1.AngularVelocity -= su1 * lambda * b1.InertiaInv;

                //b2.LinearVelocity.Mad(impulse, b2.MassInv);
                b2.LinearVelocity = MathUtil.Mad(b2.LinearVelocity, impulse, b2.MassInv);
                b2.AngularVelocity += su2 * lambda * b2.InertiaInv;
            }

            if (MotorEnabled)
            {
                float cdot = b2.AngularVelocity - b1.AngularVelocity - MotorSpeed;
                float lambda = -motorEm * cdot;

                float motorLambdaOld = motorLambdaAcc;
                motorLambdaAcc = Math.Clamp(motorLambdaAcc + lambda, -MaxMotorImpulse, MaxMotorImpulse);
                lambda = motorLambdaAcc - motorLambdaOld;

                b1.AngularVelocity -= lambda * b1.InertiaInv;
                b2.AngularVelocity += lambda * b2.InertiaInv;
            }

            {
                float cdot = Vector2.Dot(n, b2.LinearVelocity - b1.LinearVelocity) + sn2 * b2.AngularVelocity - sn1 * b1.AngularVelocity;
                float lambda = -em * cdot;
                lambdaAcc += lambda;

                var impulse = n * lambda;
                //b1.LinearVelocity.Mad(impulse, -b1.MassInv);
                b1.LinearVelocity = MathUtil.Mad(b1.LinearVelocity, impulse, -b1.MassInv);
                b1.AngularVelocity -= sn1 * lambda * b1.InertiaInv;

                //b2.LinearVelocity.Mad(impulse, b2.MassInv);
                b2.LinearVelocity = MathUtil.Mad(b2.LinearVelocity, impulse, b2.MassInv);
                b2.AngularVelocity += sn2 * lambda * b2.InertiaInv;
            }
        }

        public override bool SolvePositionConstraints()
        {
            var b1 = Body1;
            var b2 = Body2;

            var r1 = MathUtil.Rotate(anchor1 - b1.Centroid, b1.Angle);
            var r2 = MathUtil.Rotate(anchor2 - b2.Centroid, b2.Angle);

            var p1 = b1.Position + r1;
            var p2 = b2.Position + r2;
            var d = p2 - p1;
            var r1d = r1 + d;
            var n = MathUtil.Rotate(nLocal, b1.Angle);

            float c = Vector2.Dot(n, d);
            float correction = Math.Clamp(c, -MAX_LINEAR_CORRECTION, MAX_LINEAR_CORRECTION);

            float s1 = MathUtil.Cross(r1d, n);
            float s2 = MathUtil.Cross(r2, n);
            float emInv = b1.MassInv + b2.MassInv + b1.InertiaInv * s1 * s1 + b2.InertiaInv * s2 * s2;
            float kInv = emInv == 0 ? 0 : 1 / emInv;
            float lambdaDt = kInv * -correction;

            var impulseDt = n * lambdaDt;
            //b1.Position.Mad(impulseDt, -b1.MassInv);
            b1.Position = MathUtil.Mad(b1.Position, impulseDt, -b1.MassInv);
            b1.Angle -= s1 * lambdaDt * b1.InertiaInv;

            //b2.Position.Mad(impulseDt, b2.MassInv);
            b2.Position = MathUtil.Mad(b2.Position, impulseDt, b2.MassInv);
            b2.Angle += s2 * lambdaDt * b2.InertiaInv;

            return Math.Abs(c) < LINEAR_SLOP;
        }

        public override Vector2 GetReactionForce(float invDt) => n * (lambdaAcc * invDt);
        public override float GetReactionTorque(float invDt) => 0;
    }
}

using Prowl.Drift;
using System;
using System.Numerics;

namespace Drift.Joints
{
    public class WeldJoint : Joint
    {
        private Vec2 anchor1, anchor2;
        private Vec2 r1, r2;
        private float gamma, beta_c;
        private Vector3 lambdaAcc;
        private float em11, em12, em13, em22, em23, em33;

        public float FrequencyHz { get; private set; }
        public float DampingRatio { get; private set; }
        public float MaxImpulse { get; private set; }

        public WeldJoint(Body body1, Body body2, Vec2 anchor)
            : base(JointType.Weld, body1, body2, false)
        {
            anchor1 = body1.InverseTransformPoint(anchor);
            anchor2 = body2.InverseTransformPoint(anchor);

            FrequencyHz = 0;
            DampingRatio = 0;
            gamma = 0;
            beta_c = 0;
            lambdaAcc = new Vector3(0, 0, 0);
        }

        public void SetSpringFrequencyHz(float frequencyHz) => FrequencyHz = frequencyHz;
        public void SetSpringDampingRatio(float dampingRatio) => DampingRatio = dampingRatio;

        public override void InitSolver(float dt, bool warmStarting)
        {
            var b1 = Body1;
            var b2 = Body2;

            MaxImpulse = MaxForce * dt;

            r1 = b1.RotatePoint(anchor1 - b1.Centroid);
            r2 = b2.RotatePoint(anchor2 - b2.Centroid);

            float sumMinv = b1.MassInv + b2.MassInv;
            float r1x_i = r1.X * b1.InertiaInv;
            float r1y_i = r1.Y * b1.InertiaInv;
            float r2x_i = r2.X * b2.InertiaInv;
            float r2y_i = r2.Y * b2.InertiaInv;

            float k11 = sumMinv + r1.Y * r1y_i + r2.Y * r2y_i;
            float k12 = -r1.X * r1y_i - r2.X * r2y_i;
            float k13 = -r1y_i - r2y_i;
            float k22 = sumMinv + r1.X * r1x_i + r2.X * r2x_i;
            float k23 = r1x_i + r2x_i;
            float k33 = b1.InertiaInv + b2.InertiaInv;

            em11 = k11; em12 = k12; em13 = k13;
            em22 = k22; em23 = k23; em33 = k33;

            if (FrequencyHz > 0)
            {
                float m = k33 > 0 ? 1 / k33 : 0;
                float omega = 2 * MathF.PI * FrequencyHz;
                float k = m * omega * omega;
                float c = m * 2 * DampingRatio * omega;

                gamma = (c + k * dt) * dt;
                gamma = gamma == 0 ? 0 : 1 / gamma;
                float beta = dt * k * gamma;

                float pc = b2.Angle - b1.Angle;
                beta_c = beta * pc;

                em33 += gamma;
            }
            else
            {
                gamma = 0;
                beta_c = 0;
            }

            if (warmStarting)
            {
                var lambdaXY = new Vec2(lambdaAcc.X, lambdaAcc.Y);
                float lambdaZ = lambdaAcc.Z;

                b1.LinearVelocity.Mad(lambdaXY, -b1.MassInv);
                b1.AngularVelocity -= (Vec2.Cross(r1, lambdaXY) + lambdaZ) * b1.InertiaInv;

                b2.LinearVelocity.Mad(lambdaXY, b2.MassInv);
                b2.AngularVelocity += (Vec2.Cross(r2, lambdaXY) + lambdaZ) * b2.InertiaInv;
            }
            else
            {
                lambdaAcc = new Vector3(0, 0, 0);
            }
        }

        public override void SolveVelocityConstraints()
        {
            var b1 = Body1;
            var b2 = Body2;

            if (FrequencyHz > 0)
            {
                float cdot2 = b2.AngularVelocity - b1.AngularVelocity;
                float lambdaZ = -(cdot2 + beta_c + gamma * lambdaAcc.Z) / em33;

                b1.AngularVelocity -= lambdaZ * b1.InertiaInv;
                b2.AngularVelocity += lambdaZ * b2.InertiaInv;

                var v1 = b1.LinearVelocity + Vec2.Perp(r1) * b1.AngularVelocity;
                var v2 = b2.LinearVelocity + Vec2.Perp(r2) * b2.AngularVelocity;
                var cdot1 = v2 - v1;
                var lambdaXY = MathUtil.Solve(em11, em12, em12, em22, Vec2.Neg(cdot1));

                lambdaAcc.X += lambdaXY.X;
                lambdaAcc.Y += lambdaXY.Y;
                lambdaAcc.Z += lambdaZ;

                b1.LinearVelocity.Mad(lambdaXY, -b1.MassInv);
                b1.AngularVelocity -= Vec2.Cross(r1, lambdaXY) * b1.InertiaInv;

                b2.LinearVelocity.Mad(lambdaXY, b2.MassInv);
                b2.AngularVelocity += Vec2.Cross(r2, lambdaXY) * b2.InertiaInv;
            }
            else
            {
                var v1 = b1.LinearVelocity + Vec2.Perp(r1) * b1.AngularVelocity;
                var v2 = b2.LinearVelocity + Vec2.Perp(r2) * b2.AngularVelocity;
                var cdot1 = v2 - v1;
                float cdot2 = b2.AngularVelocity - b1.AngularVelocity;
                var cdot = new Vector3(cdot1.X, cdot1.Y, cdot2);

                var lambda = MathUtil.Solve3x3(em11, em12, em13, em12, em22, em23, em13, em23, em33, -cdot);
                lambdaAcc += lambda;

                var lambdaXY = new Vec2(lambda.X, lambda.Y);

                b1.LinearVelocity.Mad(lambdaXY, -b1.MassInv);
                b1.AngularVelocity -= (Vec2.Cross(r1, lambdaXY) + lambda.Z) * b1.InertiaInv;

                b2.LinearVelocity.Mad(lambdaXY, b2.MassInv);
                b2.AngularVelocity += (Vec2.Cross(r2, lambdaXY) + lambda.Z) * b2.InertiaInv;
            }
        }

        public override bool SolvePositionConstraints()
        {
            var b1 = Body1;
            var b2 = Body2;

            var r1 = Vec2.Rotate(anchor1 - b1.Centroid, b1.Angle);
            var r2 = Vec2.Rotate(anchor2 - b2.Centroid, b2.Angle);

            float sumMinv = b1.MassInv + b2.MassInv;
            float r1x_i = r1.X * b1.InertiaInv;
            float r1y_i = r1.Y * b1.InertiaInv;
            float r2x_i = r2.X * b2.InertiaInv;
            float r2y_i = r2.Y * b2.InertiaInv;

            float k11 = sumMinv + r1.Y * r1y_i + r2.Y * r2y_i;
            float k12 = -r1.X * r1y_i - r2.X * r2y_i;
            float k13 = -r1y_i - r2y_i;
            float k22 = sumMinv + r1.X * r1x_i + r2.X * r2x_i;
            float k23 = r1x_i + r2x_i;
            float k33 = b1.InertiaInv + b2.InertiaInv;


            var c1 = b2.Position + r2 - (b1.Position + r1);
            var c2 = b2.Angle - b1.Angle;

            if (FrequencyHz > 0)
            {
                var correction = Vec2.Truncate(c1, Joint.MAX_LINEAR_CORRECTION);
                var lambdaDtXY = MathUtil.Solve(k11, k12, k12, k22, Vec2.Neg(correction));

                b1.Position.Mad(lambdaDtXY, -b1.MassInv);
                b1.Angle -= Vec2.Cross(r1, lambdaDtXY) * b1.InertiaInv;

                b2.Position.Mad(lambdaDtXY, b2.MassInv);
                b2.Angle += Vec2.Cross(r2, lambdaDtXY) * b2.InertiaInv;
            }
            else
            {
                var correction = new Vector3(
                    Vec2.Truncate(c1, Joint.MAX_LINEAR_CORRECTION).X,
                    Vec2.Truncate(c1, Joint.MAX_LINEAR_CORRECTION).Y,
                    Math.Clamp(c2, -Joint.MAX_ANGULAR_CORRECTION, Joint.MAX_ANGULAR_CORRECTION)
                );

                var lambdaDt = MathUtil.Solve3x3(k11, k12, k13, k12, k22, k23, k13, k23, k33, -correction);
                var lambdaDtXY = new Vec2(lambdaDt.X, lambdaDt.Y);

                b1.Position.Mad(lambdaDtXY, -b1.MassInv);
                b1.Angle -= (Vec2.Cross(r1, lambdaDtXY) + lambdaDt.Z) * b1.InertiaInv;

                b2.Position.Mad(lambdaDtXY, b2.MassInv);
                b2.Angle += (Vec2.Cross(r2, lambdaDtXY) + lambdaDt.Z) * b2.InertiaInv;
            }

            return c1.Length() < Joint.LINEAR_SLOP && Math.Abs(c2) <= Joint.ANGULAR_SLOP;
        }

        public override Vec2 GetReactionForce(float invDt) => new Vec2(lambdaAcc.X, lambdaAcc.Y) * invDt;
        public override float GetReactionTorque(float invDt) => lambdaAcc.Z * invDt;
    }
}

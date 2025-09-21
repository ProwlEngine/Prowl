using Prowl.Drift;
using System;

namespace Drift.Joints
{
    public class RevoluteJoint : Joint
    {
        private Vec2 _r1, _r2;
        private Mat3 _emInv;
        private float _em2; // Angular effective mass

        private Vec3 _lambdaAcc = Vec3.Zero;
        private float _motorLambdaAcc;

        private float _refAngle;

        // Limits
        private bool _limitEnabled;
        private float _limitLowerAngle;
        private float _limitUpperAngle;
        private int _limitState;

        // Motor
        private bool _motorEnabled;
        private float _motorSpeed;
        private float _maxMotorTorque;
        private float _maxMotorImpulse;

        public RevoluteJoint(Body b1, Body b2, Vec2 anchor)
            : base(JointType.Revolute, b1, b2, false)
        {
            Anchor1 = Body1.GetLocalPoint(anchor);
            Anchor2 = Body2.GetLocalPoint(anchor);
            _refAngle = b2.Angle - b1.Angle;
        }

        public void EnableMotor(bool flag) => _motorEnabled = flag;
        public void SetMotorSpeed(float speed) => _motorSpeed = speed;
        public void SetMaxMotorTorque(float torque) => _maxMotorTorque = torque;

        public void EnableLimit(bool flag) => _limitEnabled = flag;
        public void SetLimits(float lower, float upper)
        {
            _limitLowerAngle = lower;
            _limitUpperAngle = upper;
        }

        public override void InitSolver(float dt, bool warmStarting)
        {
            if (!_motorEnabled) _motorLambdaAcc = 0;
            else _maxMotorImpulse = _maxMotorTorque * dt;

            if (_limitEnabled)
            {
                float da = Body2.Angle - Body1.Angle - _refAngle;

                if (Math.Abs(_limitUpperAngle - _limitLowerAngle) < ANGULAR_SLOP)
                    _limitState = LIMIT_STATE_EQUAL_LIMITS;
                else if (da <= _limitLowerAngle)
                {
                    if (_limitState != LIMIT_STATE_AT_LOWER) _lambdaAcc.Z = 0;
                    _limitState = LIMIT_STATE_AT_LOWER;
                }
                else if (da >= _limitUpperAngle)
                {
                    if (_limitState != LIMIT_STATE_AT_UPPER) _lambdaAcc.Z = 0;
                    _limitState = LIMIT_STATE_AT_UPPER;
                }
                else
                {
                    _limitState = LIMIT_STATE_INACTIVE;
                    _lambdaAcc.Z = 0;
                }
            }
            else
            {
                _limitState = LIMIT_STATE_INACTIVE;
            }

            _r1 = Body1.Transform.Rotate(Anchor1 - Body1.Centroid);
            _r2 = Body2.Transform.Rotate(Anchor2 - Body2.Centroid);

            float sumMinv = Body1.MassInv + Body2.MassInv;

            float r1x_i = _r1.X * Body1.InertiaInv;
            float r1y_i = _r1.Y * Body1.InertiaInv;
            float r2x_i = _r2.X * Body2.InertiaInv;
            float r2y_i = _r2.Y * Body2.InertiaInv;

            float k11 = sumMinv + _r1.Y * r1y_i + _r2.Y * r2y_i;
            float k12 = -_r1.X * r1y_i - _r2.X * r2y_i;
            float k13 = -r1y_i - r2y_i;

            float k22 = sumMinv + _r1.X * r1x_i + _r2.X * r2x_i;
            float k23 = r1x_i + r2x_i;

            float k33 = Body1.InertiaInv + Body2.InertiaInv;

            _emInv = new Mat3(k11, k12, k13, k12, k22, k23, k13, k23, k33);
            _em2 = k33 != 0 ? 1f / k33 : 0;

            if (warmStarting)
            {
                var lambdaXY = new Vec2(_lambdaAcc.X, _lambdaAcc.Y);
                float lambdaZ = _lambdaAcc.Z + _motorLambdaAcc;

                Body1.LinearVelocity -= lambdaXY * Body1.MassInv;
                Body1.AngularVelocity -= (Vec2.Cross(_r1, lambdaXY) + lambdaZ) * Body1.InertiaInv;

                Body2.LinearVelocity += lambdaXY * Body2.MassInv;
                Body2.AngularVelocity += (Vec2.Cross(_r2, lambdaXY) + lambdaZ) * Body2.InertiaInv;
            }
            else
            {
                _lambdaAcc = Vec3.Zero;
                _motorLambdaAcc = 0;
            }
        }

        public override void SolveVelocityConstraints()
        {
            // Motor
            if (_motorEnabled && _limitState != LIMIT_STATE_EQUAL_LIMITS)
            {
                float cdot = Body2.AngularVelocity - Body1.AngularVelocity - _motorSpeed;
                float lambda = -_em2 * cdot;

                float old = _motorLambdaAcc;
                _motorLambdaAcc = MathUtil.Clamp(old + lambda, -_maxMotorImpulse, _maxMotorImpulse);
                lambda = _motorLambdaAcc - old;

                Body1.AngularVelocity -= lambda * Body1.InertiaInv;
                Body2.AngularVelocity += lambda * Body2.InertiaInv;
            }

            // Limits active
            if (_limitEnabled && _limitState != LIMIT_STATE_INACTIVE)
            {
                var v1 = Body1.LinearVelocity + Vec2.Perp(_r1) * Body1.AngularVelocity;
                var v2 = Body2.LinearVelocity + Vec2.Perp(_r2) * Body2.AngularVelocity;
                var cdot1 = v2 - v1;
                float cdot2 = Body2.AngularVelocity - Body1.AngularVelocity;
                var cdot = new Vec3(cdot1.X, cdot1.Y, cdot2);

                var lambda = _emInv.Solve(cdot.Neg());

                if (_limitState == LIMIT_STATE_EQUAL_LIMITS)
                {
                    _lambdaAcc += lambda;
                }
                else
                {
                    float newZ = _lambdaAcc.Z + lambda.Z;
                    bool lowerLimited = _limitState == LIMIT_STATE_AT_LOWER && newZ < 0;
                    bool upperLimited = _limitState == LIMIT_STATE_AT_UPPER && newZ > 0;

                    if (lowerLimited || upperLimited)
                    {
                        var rhs = cdot1 + new Vec2(_emInv.M13, _emInv.M23) * newZ;
                        var reduced = _emInv.Solve2x2(rhs.Neg());
                        lambda.X = reduced.X;
                        lambda.Y = reduced.Y;
                        lambda.Z = -_lambdaAcc.Z;

                        _lambdaAcc.X += lambda.X;
                        _lambdaAcc.Y += lambda.Y;
                        _lambdaAcc.Z = 0;
                    }
                    else
                    {
                        _lambdaAcc += lambda;
                    }
                }

                var lambdaXY = new Vec2(lambda.X, lambda.Y);

                Body1.LinearVelocity -= lambdaXY * Body1.MassInv;
                Body1.AngularVelocity -= (Vec2.Cross(_r1, lambdaXY) + lambda.Z) * Body1.InertiaInv;

                Body2.LinearVelocity += lambdaXY * Body2.MassInv;
                Body2.AngularVelocity += (Vec2.Cross(_r2, lambdaXY) + lambda.Z) * Body2.InertiaInv;
            }
            else
            {
                var v1 = Body1.LinearVelocity + Vec2.Perp(_r1) * Body1.AngularVelocity;
                var v2 = Body2.LinearVelocity + Vec2.Perp(_r2) * Body2.AngularVelocity;
                var cdot = v2 - v1;

                var lambda = _emInv.Solve2x2(cdot.Neg());

                _lambdaAcc += new Vec3(lambda.X, lambda.Y, 0);

                Body1.LinearVelocity -= lambda * Body1.MassInv;
                Body1.AngularVelocity -= Vec2.Cross(_r1, lambda) * Body1.InertiaInv;

                Body2.LinearVelocity += lambda * Body2.MassInv;
                Body2.AngularVelocity += Vec2.Cross(_r2, lambda) * Body2.InertiaInv;
            }
        }

        public override bool SolvePositionConstraints()
        {
            float angularError = 0;
            float positionError = 0;

            if (_limitEnabled && _limitState != LIMIT_STATE_INACTIVE)
            {
                float da = Body2.Angle - Body1.Angle - _refAngle;
                float angularImpulseDt = 0;

                if (_limitState == LIMIT_STATE_EQUAL_LIMITS)
                {
                    float c = MathUtil.Clamp(da - _limitLowerAngle, -MAX_ANGULAR_CORRECTION, MAX_ANGULAR_CORRECTION);
                    angularError = Math.Abs(c);
                    angularImpulseDt = -_em2 * c;
                }
                else if (_limitState == LIMIT_STATE_AT_LOWER)
                {
                    float c = da - _limitLowerAngle;
                    angularError = -c;
                    c = MathUtil.Clamp(c + ANGULAR_SLOP, -MAX_ANGULAR_CORRECTION, 0);
                    angularImpulseDt = -_em2 * c;
                }
                else if (_limitState == LIMIT_STATE_AT_UPPER)
                {
                    float c = da - _limitUpperAngle;
                    angularError = c;
                    c = MathUtil.Clamp(c - ANGULAR_SLOP, 0, MAX_ANGULAR_CORRECTION);
                    angularImpulseDt = -_em2 * c;
                }

                Body1.Angle -= angularImpulseDt * Body1.InertiaInv;
                Body2.Angle += angularImpulseDt * Body2.InertiaInv;
            }

            {
                var r1 = Vec2.Rotate(Anchor1 - Body1.Centroid, Body1.Angle);
                var r2 = Vec2.Rotate(Anchor2 - Body2.Centroid, Body2.Angle);

                var c = Body2.Position + r2 - (Body1.Position + r1);
                var correction = Vec2.Truncate(c, MAX_LINEAR_CORRECTION);
                positionError = correction.Length();

                float sumMinv = Body1.MassInv + Body2.MassInv;
                float r1y_i = r1.Y * Body1.InertiaInv;
                float r2y_i = r2.Y * Body2.InertiaInv;
                float k11 = sumMinv + r1.Y * r1y_i + r2.Y * r2y_i;
                float k12 = -r1.X * r1y_i - r2.X * r2y_i;
                float k22 = sumMinv + r1.X * r1.X * Body1.InertiaInv + r2.X * r2.X * Body2.InertiaInv;

                var emInv = new Mat2(k11, k12, k12, k22);
                var lambdaDt = emInv.Solve(correction.Neg());

                Body1.Position -= lambdaDt * Body1.MassInv;
                Body1.Angle -= Vec2.Cross(r1, lambdaDt) * Body1.InertiaInv;

                Body2.Position += lambdaDt * Body2.MassInv;
                Body2.Angle += Vec2.Cross(r2, lambdaDt) * Body2.InertiaInv;

                return positionError < LINEAR_SLOP && angularError < ANGULAR_SLOP;
            }
        }

        public override Vec2 GetReactionForce(float dtInv)
        {
            return new Vec2(_lambdaAcc.X, _lambdaAcc.Y) * dtInv;
        }

        public override float GetReactionTorque(float dtInv)
        {
            return (_lambdaAcc.Z + _motorLambdaAcc) * dtInv;
        }
    }
}

namespace Prowl.Drift
{
    public class ContactSolver
    {
        public readonly Shape Shape1;
        public readonly Shape Shape2;
        public readonly List<Contact> Contacts;

        // Material properties
        public readonly float E = 1f; // restitution
        public readonly float U = 1f; // friction

        public const float CollisionSlop = 0.0008f;
        public const float Baumgarte = 0.28f;
        public const float MaxLinearCorrection = 1f;

        public ContactSolver(Shape s1, Shape s2, List<Contact> contacts, float e, float f)
        {
            Shape1 = s1;
            Shape2 = s2;
            Contacts = contacts;
            E = e;
            U = f;
        }

        public void Update(List<Contact> newContacts)
        {
            foreach (var newCon in newContacts)
            {
                for (int j = 0; j < Contacts.Count; j++)
                {
                    if (newCon.Hash == Contacts[j].Hash)
                    {
                        newCon.LambdaNAcc = Contacts[j].LambdaNAcc;
                        newCon.LambdaTAcc = Contacts[j].LambdaTAcc;
                        break;
                    }
                }
            }
            //ContactArr = newContacts; 
            Contacts.Clear();
            Contacts.AddRange(newContacts);

        }

        public void InitSolver(float dtInv)
        {
            var b1 = Shape1.Body;
            var b2 = Shape2.Body;
            float sumMinv = b1.MassInv + b2.MassInv;

            foreach (var con in Contacts)
            {
                con.R1 = con.Position - b1.Position;
                con.R2 = con.Position - b2.Position;
                con.R1Local = b1.Transform.Unrotate(con.R1);
                con.R2Local = b2.Transform.Unrotate(con.R2);

                var n = con.NormalTowardTwo;
                var t = Vec2.Perp(n);

                float sn1 = Vec2.Cross(con.R1, n);
                float sn2 = Vec2.Cross(con.R2, n);
                float emnInv = sumMinv + b1.InertiaInv * sn1 * sn1 + b2.InertiaInv * sn2 * sn2;
                con.Emn = emnInv == 0 ? 0 : 1f / emnInv;

                float st1 = Vec2.Cross(con.R1, t);
                float st2 = Vec2.Cross(con.R2, t);
                float emtInv = sumMinv + b1.InertiaInv * st1 * st1 + b2.InertiaInv * st2 * st2;
                con.Emt = emtInv == 0 ? 0 : 1f / emtInv;

                Vec2 v1 = b1.LinearVelocity + Vec2.Perp(con.R1) * b1.AngularVelocity;
                Vec2 v2 = b2.LinearVelocity + Vec2.Perp(con.R2) * b2.AngularVelocity;
                Vec2 rv = v2 - v1;

                //con.Bounce = Vec2.Dot(rv, n) * E;
                float vn = Vec2.Dot(rv, n);
                con.Bounce = vn < -1e-3f ? E * vn : 0f;
            }
        }

        public void WarmStart()
        {
            var b1 = Shape1.Body;
            var b2 = Shape2.Body;

            foreach (var con in Contacts)
            {
                var n = con.NormalTowardTwo;
                float ln = con.LambdaNAcc;
                float lt = con.LambdaTAcc;

                var impulse = new Vec2(ln * n.X - lt * n.Y, lt * n.X + ln * n.Y);

                b1.LinearVelocity += impulse * -b1.MassInv;
                b1.AngularVelocity -= Vec2.Cross(con.R1, impulse) * b1.InertiaInv;

                b2.LinearVelocity += impulse * b2.MassInv;
                b2.AngularVelocity += Vec2.Cross(con.R2, impulse) * b2.InertiaInv;
            }
        }

        public void SolveVelocityConstraints()
        {
            var b1 = Shape1.Body;
            var b2 = Shape2.Body;

            float m1Inv = b1.MassInv, i1Inv = b1.InertiaInv;
            float m2Inv = b2.MassInv, i2Inv = b2.InertiaInv;

            foreach (var con in Contacts)
            {
                var n = con.NormalTowardTwo;
                var t = Vec2.Perp(n);
                var r1 = con.R1;
                var r2 = con.R2;

                // Linear velocities at contact point
                // in 2D: cross(w, r) = perp(r) * w
                Vec2 v1 = Vec2.Mad(b1.LinearVelocity, Vec2.Perp(r1), b1.AngularVelocity);
                Vec2 v2 = Vec2.Mad(b2.LinearVelocity, Vec2.Perp(r2), b2.AngularVelocity);

                // Relative velocity at contact point
                Vec2 rv = v2 - v1;

                // Compute normal constraint impulse + adding bounce as a velocity bias
                // lambda_n = -EMn * J * V
                float lambdaN = -con.Emn * (Vec2.Dot(n, rv) + con.Bounce);

                // Accumulate and clamp
                float oldN = con.LambdaNAcc;
                con.LambdaNAcc = MathF.Max(oldN + lambdaN, 0);
                lambdaN = con.LambdaNAcc - oldN;

                // Compute frictional constraint impulse
                // lambda_t = -EMt * J * V
                float lambdaT = -con.Emt * Vec2.Dot(t, rv);

                // Max friction constraint impulse (Coulomb's Law)
                float lambdaTMax = con.LambdaNAcc * U;

                // Accumulate and clamp
                float oldT = con.LambdaTAcc;
                con.LambdaTAcc = MathUtil.Clamp(oldT + lambdaT, -lambdaTMax, lambdaTMax);
                lambdaT = con.LambdaTAcc - oldT;

                // Apply the final impulses
                var impulse = new Vec2(lambdaN * n.X - lambdaT * n.Y, lambdaT * n.X + lambdaN * n.Y);

                b1.LinearVelocity.Mad(impulse, -m1Inv);
                b1.AngularVelocity -= Vec2.Cross(r1, impulse) * i1Inv;

                b2.LinearVelocity.Mad(impulse, m2Inv);
                b2.AngularVelocity += Vec2.Cross(r2, impulse) * i2Inv;
            }
        }

        public bool SolvePositionConstraints()
        {
            var b1 = Shape1.Body;
            var b2 = Shape2.Body;

            float m1Inv = b1.MassInv, i1Inv = b1.InertiaInv;
            float m2Inv = b2.MassInv, i2Inv = b2.InertiaInv;
            float sumMinv = m1Inv + m2Inv;

            float maxPenetration = 0;

            foreach (var con in Contacts)
            {
                var n = con.NormalTowardTwo;

                var r1 = Vec2.Rotate(con.R1Local, b1.Angle);
                var r2 = Vec2.Rotate(con.R2Local, b2.Angle);

                var p1 = b1.Position + r1;
                var p2 = b2.Position + r2;

                var dp = p2 - p1;
                float c = Vec2.Dot(dp, n) + con.Depth;
                float correction = MathUtil.Clamp(Baumgarte * (c + CollisionSlop), -MaxLinearCorrection, 0);
                if (correction == 0) continue;

                maxPenetration = MathF.Max(maxPenetration, -c);

                float sn1 = Vec2.Cross(r1, n);
                float sn2 = Vec2.Cross(r2, n);
                float emInv = sumMinv + b1.InertiaInv * sn1 * sn1 + b2.InertiaInv * sn2 * sn2;
                float lambdaDt = emInv == 0 ? 0 : -correction / emInv;

                var impulseDt = n * lambdaDt;

                b1.Position += impulseDt * -m1Inv;
                b1.Angle -= sn1 * lambdaDt * i1Inv;

                b2.Position += impulseDt * m2Inv;
                b2.Angle += sn2 * lambdaDt * i2Inv;
            }

            return maxPenetration <= CollisionSlop * 3;
        }
    }
}

using System;
using Prowl.Vector;

namespace Prowl.Scribe.Sdf
{
    internal static class SdfMath
    {
        public static double Cross(Double2 a, Double2 b) => a.X * b.Y - a.Y * b.X;

        public static Double2 Mix(Double2 a, Double2 b, double weight)
            => (1 - weight) * a + weight * b;

        public static double Mix(double a, double b, double weight)
            => (1 - weight) * a + weight * b;

        public static int Sign(double n) => (0 < n ? 1 : 0) - (n < 0 ? 1 : 0);

        // Solves ax^2 + bx + c = 0. Returns number of real roots written to x.
        public static int SolveQuadratic(double[] x, double a, double b, double c)
        {
            if (a == 0 || Math.Abs(b) > 1e12 * Math.Abs(a))
            {
                if (b == 0)
                    return c == 0 ? -1 : 0;
                x[0] = -c / b;
                return 1;
            }
            double dscr = b * b - 4 * a * c;
            if (dscr > 0)
            {
                dscr = Math.Sqrt(dscr);
                x[0] = (-b + dscr) / (2 * a);
                x[1] = (-b - dscr) / (2 * a);
                return 2;
            }
            if (dscr == 0)
            {
                x[0] = -b / (2 * a);
                return 1;
            }
            return 0;
        }

        private static int SolveCubicNormed(double[] x, double a, double b, double c)
        {
            double a2 = a * a;
            double q = 1 / 9.0 * (a2 - 3 * b);
            double r = 1 / 54.0 * (a * (2 * a2 - 9 * b) + 27 * c);
            double r2 = r * r;
            double q3 = q * q * q;
            a *= 1 / 3.0;
            if (r2 < q3)
            {
                double t = r / Math.Sqrt(q3);
                if (t < -1) t = -1;
                if (t > 1) t = 1;
                t = Math.Acos(t);
                q = -2 * Math.Sqrt(q);
                x[0] = q * Math.Cos(1 / 3.0 * t) - a;
                x[1] = q * Math.Cos(1 / 3.0 * (t + 2 * Math.PI)) - a;
                x[2] = q * Math.Cos(1 / 3.0 * (t - 2 * Math.PI)) - a;
                return 3;
            }
            else
            {
                double u = (r < 0 ? 1 : -1) * Math.Pow(Math.Abs(r) + Math.Sqrt(r2 - q3), 1 / 3.0);
                double v = u == 0 ? 0 : q / u;
                x[0] = (u + v) - a;
                if (u == v || Math.Abs(u - v) < 1e-12 * Math.Abs(u + v))
                {
                    x[1] = -0.5 * (u + v) - a;
                    return 2;
                }
                return 1;
            }
        }

        public static int SolveCubic(double[] x, double a, double b, double c, double d)
        {
            if (a != 0)
            {
                double bn = b / a;
                if (Math.Abs(bn) < 1e6)
                    return SolveCubicNormed(x, bn, c / a, d / a);
            }
            return SolveQuadratic(x, b, c, d);
        }
    }
}

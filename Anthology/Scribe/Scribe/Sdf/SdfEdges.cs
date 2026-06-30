using Prowl.Vector;
using static Prowl.Scribe.Sdf.SdfMath;

namespace Prowl.Scribe.Sdf
{
    internal abstract class EdgeSegment
    {
        public static EdgeSegment Create(Double2 p0, Double2 p1)
            => new LinearSegment(p0, p1);

        public static EdgeSegment Create(Double2 p0, Double2 p1, Double2 p2)
        {
            if (Cross(p1 - p0, p2 - p1) == 0)
                return new LinearSegment(p0, p2);
            return new QuadraticSegment(p0, p1, p2);
        }

        public static EdgeSegment Create(Double2 p0, Double2 p1, Double2 p2, Double2 p3)
        {
            Double2 p12 = p2 - p1;
            if (Cross(p1 - p0, p12) == 0 && Cross(p12, p3 - p2) == 0)
                return new LinearSegment(p0, p3);
            p12 = 1.5 * p1 - 0.5 * p0;
            if (p12 == 1.5 * p2 - 0.5 * p3)
                return new QuadraticSegment(p0, p12, p3);
            return new CubicSegment(p0, p1, p2, p3);
        }

        public abstract Double2 Point(double param);
        public abstract int ScanlineIntersections(double[] x, int[] dy, double y);

        // Copy with X and Y swapped, so a horizontal scanline over it yields this edge's column crossings.
        public abstract EdgeSegment Transposed();
        public abstract void Bound(ref double xMin, ref double yMin, ref double xMax, ref double yMax);

        protected static void PointBounds(Double2 p, ref double xMin, ref double yMin, ref double xMax, ref double yMax)
        {
            if (p.X < xMin) xMin = p.X;
            if (p.Y < yMin) yMin = p.Y;
            if (p.X > xMax) xMax = p.X;
            if (p.Y > yMax) yMax = p.Y;
        }
    }

    internal sealed class LinearSegment : EdgeSegment
    {
        public Double2[] p = new Double2[2];

        public LinearSegment(Double2 p0, Double2 p1)
        {
            p[0] = p0; p[1] = p1;
        }

        public override EdgeSegment Transposed() => new LinearSegment(new Double2(p[0].Y, p[0].X), new Double2(p[1].Y, p[1].X));
        public override Double2 Point(double param) => Mix(p[0], p[1], param);

        public override int ScanlineIntersections(double[] x, int[] dy, double y)
        {
            if ((y >= p[0].Y && y < p[1].Y) || (y >= p[1].Y && y < p[0].Y))
            {
                double param = (y - p[0].Y) / (p[1].Y - p[0].Y);
                x[0] = Mix(p[0].X, p[1].X, param);
                dy[0] = Sign(p[1].Y - p[0].Y);
                return 1;
            }
            return 0;
        }

        public override void Bound(ref double xMin, ref double yMin, ref double xMax, ref double yMax)
        {
            PointBounds(p[0], ref xMin, ref yMin, ref xMax, ref yMax);
            PointBounds(p[1], ref xMin, ref yMin, ref xMax, ref yMax);
        }
    }

    internal sealed class QuadraticSegment : EdgeSegment
    {
        public Double2[] p = new Double2[3];

        public QuadraticSegment(Double2 p0, Double2 p1, Double2 p2)
        {
            if (p1 == p0 || p1 == p2)
                p1 = 0.5 * (p0 + p2);
            p[0] = p0; p[1] = p1; p[2] = p2;
        }

        public override EdgeSegment Transposed() => new QuadraticSegment(new Double2(p[0].Y, p[0].X), new Double2(p[1].Y, p[1].X), new Double2(p[2].Y, p[2].X));

        public override Double2 Point(double param)
            => Mix(Mix(p[0], p[1], param), Mix(p[1], p[2], param), param);

        public override int ScanlineIntersections(double[] x, int[] dy, double y)
        {
            int total = 0;
            int nextDY = y > p[0].Y ? 1 : -1;
            x[total] = p[0].X;
            if (p[0].Y == y)
            {
                if (p[0].Y < p[1].Y || (p[0].Y == p[1].Y && p[0].Y < p[2].Y))
                    dy[total++] = 1;
                else
                    nextDY = 1;
            }
            {
                Double2 ab = p[1] - p[0];
                Double2 br = p[2] - p[1] - ab;
                double[] t = new double[2];
                int solutions = SolveQuadratic(t, br.Y, 2 * ab.Y, p[0].Y - y);
                if (solutions >= 2 && t[0] > t[1]) { var tmp = t[0]; t[0] = t[1]; t[1] = tmp; }
                for (int i = 0; i < solutions && total < 2; ++i)
                {
                    if (t[i] >= 0 && t[i] <= 1)
                    {
                        x[total] = p[0].X + 2 * t[i] * ab.X + t[i] * t[i] * br.X;
                        if (nextDY * (ab.Y + t[i] * br.Y) >= 0)
                        {
                            dy[total++] = nextDY;
                            nextDY = -nextDY;
                        }
                    }
                }
            }
            if (p[2].Y == y)
            {
                if (nextDY > 0 && total > 0) { --total; nextDY = -1; }
                if ((p[2].Y < p[1].Y || (p[2].Y == p[1].Y && p[2].Y < p[0].Y)) && total < 2)
                {
                    x[total] = p[2].X;
                    if (nextDY < 0) { dy[total++] = -1; nextDY = 1; }
                }
            }
            if (nextDY != (y >= p[2].Y ? 1 : -1))
            {
                if (total > 0)
                    --total;
                else
                {
                    if (System.Math.Abs(p[2].Y - y) < System.Math.Abs(p[0].Y - y))
                        x[total] = p[2].X;
                    dy[total++] = nextDY;
                }
            }
            return total;
        }

        public override void Bound(ref double xMin, ref double yMin, ref double xMax, ref double yMax)
        {
            PointBounds(p[0], ref xMin, ref yMin, ref xMax, ref yMax);
            PointBounds(p[2], ref xMin, ref yMin, ref xMax, ref yMax);
            Double2 bot = (p[1] - p[0]) - (p[2] - p[1]);
            if (bot.X != 0)
            {
                double param = (p[1].X - p[0].X) / bot.X;
                if (param > 0 && param < 1)
                    PointBounds(Point(param), ref xMin, ref yMin, ref xMax, ref yMax);
            }
            if (bot.Y != 0)
            {
                double param = (p[1].Y - p[0].Y) / bot.Y;
                if (param > 0 && param < 1)
                    PointBounds(Point(param), ref xMin, ref yMin, ref xMax, ref yMax);
            }
        }
    }

    internal sealed class CubicSegment : EdgeSegment
    {
        public Double2[] p = new Double2[4];

        public CubicSegment(Double2 p0, Double2 p1, Double2 p2, Double2 p3)
        {
            if ((p1 == p0 || p1 == p3) && (p2 == p0 || p2 == p3))
            {
                p1 = Mix(p0, p3, 1 / 3.0);
                p2 = Mix(p0, p3, 2 / 3.0);
            }
            p[0] = p0; p[1] = p1; p[2] = p2; p[3] = p3;
        }

        public override EdgeSegment Transposed() => new CubicSegment(new Double2(p[0].Y, p[0].X), new Double2(p[1].Y, p[1].X), new Double2(p[2].Y, p[2].X), new Double2(p[3].Y, p[3].X));

        public override Double2 Point(double param)
        {
            Double2 p12 = Mix(p[1], p[2], param);
            return Mix(Mix(Mix(p[0], p[1], param), p12, param), Mix(p12, Mix(p[2], p[3], param), param), param);
        }

        public override int ScanlineIntersections(double[] x, int[] dy, double y)
        {
            int total = 0;
            int nextDY = y > p[0].Y ? 1 : -1;
            x[total] = p[0].X;
            if (p[0].Y == y)
            {
                if (p[0].Y < p[1].Y || (p[0].Y == p[1].Y && (p[0].Y < p[2].Y || (p[0].Y == p[2].Y && p[0].Y < p[3].Y))))
                    dy[total++] = 1;
                else
                    nextDY = 1;
            }
            {
                Double2 ab = p[1] - p[0];
                Double2 br = p[2] - p[1] - ab;
                Double2 @as = (p[3] - p[2]) - (p[2] - p[1]) - br;
                double[] t = new double[3];
                int solutions = SolveCubic(t, @as.Y, 3 * br.Y, 3 * ab.Y, p[0].Y - y);
                if (solutions >= 2)
                {
                    if (t[0] > t[1]) { var tmp = t[0]; t[0] = t[1]; t[1] = tmp; }
                    if (solutions >= 3 && t[1] > t[2])
                    {
                        var tmp = t[1]; t[1] = t[2]; t[2] = tmp;
                        if (t[0] > t[1]) { tmp = t[0]; t[0] = t[1]; t[1] = tmp; }
                    }
                }
                for (int i = 0; i < solutions && total < 3; ++i)
                {
                    if (t[i] >= 0 && t[i] <= 1)
                    {
                        x[total] = p[0].X + 3 * t[i] * ab.X + 3 * t[i] * t[i] * br.X + t[i] * t[i] * t[i] * @as.X;
                        if (nextDY * (ab.Y + 2 * t[i] * br.Y + t[i] * t[i] * @as.Y) >= 0)
                        {
                            dy[total++] = nextDY;
                            nextDY = -nextDY;
                        }
                    }
                }
            }
            if (p[3].Y == y)
            {
                if (nextDY > 0 && total > 0) { --total; nextDY = -1; }
                if ((p[3].Y < p[2].Y || (p[3].Y == p[2].Y && (p[3].Y < p[1].Y || (p[3].Y == p[1].Y && p[3].Y < p[0].Y)))) && total < 3)
                {
                    x[total] = p[3].X;
                    if (nextDY < 0) { dy[total++] = -1; nextDY = 1; }
                }
            }
            if (nextDY != (y >= p[3].Y ? 1 : -1))
            {
                if (total > 0)
                    --total;
                else
                {
                    if (System.Math.Abs(p[3].Y - y) < System.Math.Abs(p[0].Y - y))
                        x[total] = p[3].X;
                    dy[total++] = nextDY;
                }
            }
            return total;
        }

        public override void Bound(ref double xMin, ref double yMin, ref double xMax, ref double yMax)
        {
            PointBounds(p[0], ref xMin, ref yMin, ref xMax, ref yMax);
            PointBounds(p[3], ref xMin, ref yMin, ref xMax, ref yMax);
            Double2 a0 = p[1] - p[0];
            Double2 a1 = 2 * (p[2] - p[1] - a0);
            Double2 a2 = p[3] - 3 * p[2] + 3 * p[1] - p[0];
            double[] prm = new double[2];
            int solutions = SolveQuadratic(prm, a2.X, a1.X, a0.X);
            for (int i = 0; i < solutions; ++i)
                if (prm[i] > 0 && prm[i] < 1)
                    PointBounds(Point(prm[i]), ref xMin, ref yMin, ref xMax, ref yMax);
            solutions = SolveQuadratic(prm, a2.Y, a1.Y, a0.Y);
            for (int i = 0; i < solutions; ++i)
                if (prm[i] > 0 && prm[i] < 1)
                    PointBounds(Point(prm[i]), ref xMin, ref yMin, ref xMax, ref yMax);
        }
    }
}

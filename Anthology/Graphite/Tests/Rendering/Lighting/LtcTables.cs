using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Prowl.Graphite.Rendering.Tests;

// Linearly transformed cosines (Heitz et al. 2016) fitted to Graphite's GGX with height correlated Smith, no Fresnel,
// and the integrals over a polygon, a disk as the octagon of its area and a thin strip that the shaders mirror. The table holds
// each fit's inverse matrix, its middle entry 1, across sqrt(1 - N.V) and roughness, as Rendering/Library/Data/Ltc.bin.
internal static class LtcTables
{
    public const int Size = 64;
    public const double MinAlpha = 0.002;
    private const int FitSamples = 1024;

    public readonly record struct D3(double X, double Y, double Z)
    {
        public static D3 operator +(D3 a, D3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static D3 operator -(D3 a, D3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static D3 operator -(D3 a) => new(-a.X, -a.Y, -a.Z);
        public static D3 operator *(D3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
        public static D3 operator /(D3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);
        public double Length => Math.Sqrt(Dot(this, this));
        public D3 Normalized => this / Length;
        public static double Dot(D3 a, D3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static D3 Cross(D3 a, D3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        public override string ToString() => $"({X:G4}, {Y:G4}, {Z:G4})";
    }

    // A 3x3 matrix by rows
    public readonly record struct M3(double M00, double M01, double M02, double M10, double M11, double M12, double M20, double M21, double M22)
    {
        public static D3 operator *(M3 m, D3 v) => new(m.M00 * v.X + m.M01 * v.Y + m.M02 * v.Z, m.M10 * v.X + m.M11 * v.Y + m.M12 * v.Z, m.M20 * v.X + m.M21 * v.Y + m.M22 * v.Z);

        public double Determinant => M00 * (M11 * M22 - M12 * M21) - M01 * (M10 * M22 - M12 * M20) + M02 * (M10 * M21 - M11 * M20);

        public M3 Inverse()
        {
            double d = Determinant;
            return new M3(
                (M11 * M22 - M12 * M21) / d, (M02 * M21 - M01 * M22) / d, (M01 * M12 - M02 * M11) / d,
                (M12 * M20 - M10 * M22) / d, (M00 * M22 - M02 * M20) / d, (M02 * M10 - M00 * M12) / d,
                (M10 * M21 - M11 * M20) / d, (M01 * M20 - M00 * M21) / d, (M00 * M11 - M01 * M10) / d);
        }
    }

    public static double Axis(int i) => i / (double)(Size - 1);

    // The table's view axis is sqrt(1 - N.V), so grazing views get more of it
    public static double NdotVAt(int i) => Math.Max(1 - Axis(i) * Axis(i), 1e-3);

    public static double AlphaOf(double roughness) => Math.Max(roughness * roughness, MinAlpha);

    // ------------------------------------------------------------------------------------------------
    // GGX in the frame of the normal, the view in the xz plane
    // ------------------------------------------------------------------------------------------------

    private static double Lambda(double z, double alpha)
    {
        double t2 = (1 - z * z) / Math.Max(z * z, 1e-12);
        return (-1 + Math.Sqrt(1 + alpha * alpha * t2)) / 2;
    }

    private static double Ndf(D3 h, double alpha)
    {
        double a2 = alpha * alpha;
        double d = (h.X * h.X + h.Y * h.Y) / a2 + h.Z * h.Z;
        return 1 / (Math.PI * a2 * d * d);
    }

    // The specular BRDF times the cosine toward l, Fresnel 1
    public static double Brdf(D3 v, D3 l, double alpha)
    {
        if (l.Z <= 0 || v.Z <= 0)
            return 0;
        D3 h = (v + l).Normalized;
        double nv = v.Z, nl = l.Z, a2 = alpha * alpha;
        double vis = 0.5 / Math.Max(nl * Math.Sqrt(a2 * (1 - nv * nv) + nv * nv) + nv * Math.Sqrt(a2 * (1 - nl * nl) + nl * nl), 1e-12);
        return Ndf(h, alpha) * vis * nl;
    }

    // The pdf of l under visible normal sampling
    private static double BrdfPdf(D3 v, D3 l, double alpha)
    {
        if (l.Z <= 0)
            return 0;
        D3 h = (v + l).Normalized;
        double vh = D3.Dot(v, h);
        if (vh <= 0)
            return 0;
        double g1 = 1 / (1 + Lambda(v.Z, alpha));
        return g1 * Ndf(h, alpha) / (4 * v.Z);
    }

    // Heitz 2018, visible normals of an isotropic GGX
    private static D3 SampleBrdf(D3 v, double alpha, double u1, double u2)
    {
        D3 vh = new D3(alpha * v.X, alpha * v.Y, v.Z).Normalized;
        double lensq = vh.X * vh.X + vh.Y * vh.Y;
        D3 t1 = lensq > 0 ? new D3(-vh.Y, vh.X, 0) / Math.Sqrt(lensq) : new D3(1, 0, 0);
        D3 t2 = D3.Cross(vh, t1);
        double r = Math.Sqrt(u1), phi = 2 * Math.PI * u2;
        double p1 = r * Math.Cos(phi), p2 = r * Math.Sin(phi);
        double s = 0.5 * (1 + vh.Z);
        p2 = (1 - s) * Math.Sqrt(1 - p1 * p1) + s * p2;
        D3 nh = t1 * p1 + t2 * p2 + vh * Math.Sqrt(Math.Max(0, 1 - p1 * p1 - p2 * p2));
        D3 n = new D3(alpha * nh.X, alpha * nh.Y, Math.Max(0, nh.Z)).Normalized;
        return n * (2 * D3.Dot(v, n)) - v;
    }

    // ------------------------------------------------------------------------------------------------
    // The fit
    // ------------------------------------------------------------------------------------------------

    // The transformed cosine's density toward w, for its inverse matrix
    public static double Density(M3 inverse, double determinant, D3 w)
    {
        D3 o = inverse * w;
        double length = o.Length;
        double z = o.Z / length;
        return z <= 0 ? 0 : z / Math.PI * Math.Abs(determinant) / (length * length * length);
    }

    private static (double U1, double U2) Hammersley(int i, int n) => ((i + 0.5) / n, EnergyTables.RadicalInverse((uint)i));

    private static M3 Matrix(double a, double c, double b, double phi)
    {
        double cs = Math.Cos(phi), sn = Math.Sin(phi);
        // A rotation about y taking z to the lobe's average direction, after the scales and the skew
        return new M3(a * cs, 0, b * cs + sn, 0, c, 0, -a * sn, 0, -b * sn + cs);
    }

    private sealed class Cell
    {
        public required D3 V;
        public required double Alpha;
        public required double Albedo;
        public required double Phi;
        public required D3[] BrdfL;
        public required double[] BrdfF;
        public required double[] BrdfP;
    }

    private static Cell Prepare(double mu, double alpha)
    {
        D3 v = new(Math.Sqrt(1 - mu * mu), 0, mu);
        var ls = new D3[FitSamples];
        var fs = new double[FitSamples];
        var ps = new double[FitSamples];
        double albedo = 0;
        D3 moment = new(0, 0, 0);
        for (int k = 0; k < FitSamples; k++)
        {
            (double u1, double u2) = Hammersley(k, FitSamples);
            D3 l = SampleBrdf(v, alpha, u1, u2);
            double f = Brdf(v, l, alpha), p = BrdfPdf(v, l, alpha);
            ls[k] = l;
            fs[k] = f;
            ps[k] = p;
            if (p > 0)
            {
                albedo += f / p;
                moment += l * (f / p);
            }
        }
        albedo /= FitSamples;
        for (int k = 0; k < FitSamples; k++)
            fs[k] /= Math.Max(albedo, 1e-12);
        double phi = Math.Atan2(moment.X, moment.Z);
        return new Cell { V = v, Alpha = alpha, Albedo = albedo, Phi = phi, BrdfL = ls, BrdfF = fs, BrdfP = ps };
    }

    // The squared difference of the normalized lobe and the transformed cosine integrated by sampling both
    private static double Error(Cell cell, double a, double c, double b)
    {
        M3 m = Matrix(a, c, b, cell.Phi);
        M3 inverse = m.Inverse();
        double det = inverse.Determinant;
        double sum = 0;
        for (int k = 0; k < FitSamples; k++)
        {
            D3 l = cell.BrdfL[k];
            double d = Density(inverse, det, l), f = cell.BrdfF[k], p = cell.BrdfP[k];
            double mix = 0.5 * (d + p);
            if (mix > 0)
                sum += (f - d) * (f - d) / mix;
        }
        for (int k = 0; k < FitSamples; k++)
        {
            (double u1, double u2) = Hammersley(k, FitSamples);
            double r = Math.Sqrt(u1), phi = 2 * Math.PI * u2;
            D3 o = new(r * Math.Cos(phi), r * Math.Sin(phi), Math.Sqrt(Math.Max(0, 1 - u1)));
            D3 l = (m * o).Normalized;
            double d = Density(inverse, det, l);
            double f = Brdf(cell.V, l, cell.Alpha) / Math.Max(cell.Albedo, 1e-12), p = BrdfPdf(cell.V, l, cell.Alpha);
            double mix = 0.5 * (d + p);
            if (mix > 0)
                sum += (f - d) * (f - d) / mix;
        }
        return sum / (2 * FitSamples);
    }

    // Nelder-Mead over the log scales and the skew
    private static double[] Minimize(Func<double[], double> f, double[] start, double step, int iterations)
    {
        int n = start.Length;
        var points = new double[n + 1][];
        var values = new double[n + 1];
        for (int i = 0; i <= n; i++)
        {
            points[i] = (double[])start.Clone();
            if (i > 0)
                points[i][i - 1] += step;
            values[i] = f(points[i]);
        }
        for (int it = 0; it < iterations; it++)
        {
            Array.Sort(values, points);
            double[] centroid = new double[n];
            for (int i = 0; i < n; i++)
                for (int d = 0; d < n; d++)
                    centroid[d] += points[i][d] / n;
            double[] Along(double t)
            {
                double[] p = new double[n];
                for (int d = 0; d < n; d++)
                    p[d] = centroid[d] + t * (points[n][d] - centroid[d]);
                return p;
            }
            double[] reflected = Along(-1);
            double fr = f(reflected);
            if (fr < values[0])
            {
                double[] expanded = Along(-2);
                double fe = f(expanded);
                (points[n], values[n]) = fe < fr ? (expanded, fe) : (reflected, fr);
            }
            else if (fr < values[n - 1])
            {
                (points[n], values[n]) = (reflected, fr);
            }
            else
            {
                double[] contracted = Along(fr < values[n] ? -0.5 : 0.5);
                double fc = f(contracted);
                if (fc < Math.Min(fr, values[n]))
                {
                    (points[n], values[n]) = (contracted, fc);
                }
                else
                {
                    for (int i = 1; i <= n; i++)
                    {
                        for (int d = 0; d < n; d++)
                            points[i][d] = points[0][d] + 0.5 * (points[i][d] - points[0][d]);
                        values[i] = f(points[i]);
                    }
                }
            }
        }
        Array.Sort(values, points);
        return points[0];
    }

    // The inverse matrix of the fit at a view and roughness, its middle entry 1: (m00, m02, m20, m22)
    public static (double M00, double M02, double M20, double M22) FitInverse(double mu, double alpha, double[] start, out double[] solution)
    {
        Cell cell = Prepare(mu, alpha);
        // Restarted with smaller simplexes, so every cell settles in its minimum rather than wherever a fixed count stops it
        solution = start;
        foreach (double step in new[] { 0.1, 0.02, 0.004 })
            solution = Minimize(x => Error(cell, Math.Exp(x[0]), Math.Exp(x[1]), x[2]), solution, step, 300);
        M3 inverse = Matrix(Math.Exp(solution[0]), Math.Exp(solution[1]), solution[2], cell.Phi).Inverse();
        double s = inverse.M11;
        return (inverse.M00 / s, inverse.M02 / s, inverse.M20 / s, inverse.M22 / s);
    }

    // Every roughness's row from the normal toward grazing, each fit starting from the last
    public static ushort[] Bake()
    {
        var texels = new ushort[Size * Size * 4];
        Parallel.For(0, Size, j =>
        {
            double alpha = AlphaOf(Axis(j));
            double[] start = [Math.Log(alpha), Math.Log(alpha), 0];
            for (int i = 0; i < Size; i++)
            {
                (double m00, double m02, double m20, double m22) = FitInverse(NdotVAt(i), alpha, start, out double[] solution);
                start = solution;
                int at = (j * Size + i) * 4;
                texels[at] = EnergyTables.Half(m00);
                texels[at + 1] = EnergyTables.Half(m02);
                texels[at + 2] = EnergyTables.Half(m20);
                texels[at + 3] = EnergyTables.Half(m22);
            }
        });
        return texels;
    }

    public static ushort[] Embedded() => EnergyTables.Embedded("Ltc.bin", Size * Size * 4);

    // The inverse matrix the table gives, bilinear between texel centres as the shader samples it
    public static M3 Lookup(ushort[] texels, double mu, double roughness)
    {
        double u = Math.Sqrt(Math.Max(1 - mu, 0));
        double x = Math.Clamp(u, 0, 1) * (Size - 1), y = Math.Clamp(roughness, 0, 1) * (Size - 1);
        int x0 = Math.Min((int)x, Size - 2), y0 = Math.Min((int)y, Size - 2);
        double fx = x - x0, fy = y - y0;
        double[] value = new double[4];
        for (int c = 0; c < 4; c++)
        {
            double Texel(int i, int j) => EnergyTables.FromHalf(texels[(j * Size + i) * 4 + c]);
            value[c] = (Texel(x0, y0) * (1 - fx) + Texel(x0 + 1, y0) * fx) * (1 - fy) + (Texel(x0, y0 + 1) * (1 - fx) + Texel(x0 + 1, y0 + 1) * fx) * fy;
        }
        return new M3(value[0], 0, value[1], 0, 1, 0, value[2], 0, value[3]);
    }

    // ------------------------------------------------------------------------------------------------
    // Integrals of the clamped cosine over shapes already in the cosine's space, each the share of the lobe
    // ------------------------------------------------------------------------------------------------

    public static double Polygon(IReadOnlyList<D3> q)
    {
        var v = new List<D3>();
        for (int i = 0; i < q.Count; i++)
        {
            D3 a = q[i], b = q[(i + 1) % q.Count];
            if (a.Z >= 0)
                v.Add(a);
            if ((a.Z >= 0) != (b.Z >= 0))
                v.Add(a + (b - a) * (a.Z / (a.Z - b.Z)));
        }
        if (v.Count < 3)
            return 0;
        double sum = 0;
        for (int i = 0; i < v.Count; i++)
        {
            D3 a = v[i].Normalized, b = v[(i + 1) % v.Count].Normalized;
            D3 axb = D3.Cross(a, b);
            double s = axb.Length;
            if (s > 1e-12)
                sum += Math.Atan2(s, D3.Dot(a, b)) * axb.Z / s;
        }
        return Math.Abs(sum) / (2 * Math.PI);
    }

    // A disk of centre c and half axes x and y as Ltc.glsl takes it, the octagon of its area
    public static double Disk(D3 c, D3 x, D3 y)
    {
        double scale = Math.Sqrt(Math.PI / (2 * Math.Sqrt(2))), diagonal = scale * Math.Sqrt(0.5);
        D3 dx = x * diagonal, dy = y * diagonal;
        return Polygon([c + x * scale, c + dx + dy, c + y * scale, c - dx + dy, c - x * scale, c - dx - dy, c - y * scale, c + dx - dy]);
    }

    // A strip from p1 to p2 of width vector w in the cosine's space, clipped by the horizon: dw = |(t x w).p| dl / |p|^3
    public static double Strip(D3 p1, D3 p2, D3 w)
    {
        if (p1.Z <= 0 && p2.Z <= 0)
            return 0;
        if (p1.Z < 0)
            p1 = (p1 * p2.Z - p2 * p1.Z) / (p2.Z - p1.Z);
        if (p2.Z < 0)
            p2 = (p2 * p1.Z - p1 * p2.Z) / (p1.Z - p2.Z);
        D3 segment = p2 - p1;
        double length = segment.Length;
        if (length < 1e-12)
            return 0;
        D3 t = segment / length;
        double l1 = D3.Dot(p1, t), l2 = D3.Dot(p2, t);
        D3 po = p1 - t * l1;
        double d = Math.Max(po.Length, 1e-9);
        double width = Math.Abs(D3.Dot(D3.Cross(t, w), po)) / d;
        double Fpo(double l) => l / (d * (d * d + l * l)) + Math.Atan(l / d) / (d * d);
        double Fwt(double l) => l * l / (d * (d * d + l * l));
        double integral = (Fpo(l2) - Fpo(l1)) * po.Z + (Fwt(l2) - Fwt(l1)) * t.Z;
        return Math.Max(width * integral / (2 * Math.PI), 0);
    }
}

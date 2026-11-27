using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Prowl.Graphite.Rendering.Tests;

// The GGX energy tables of Graphite/PBR/Energy.glsl, integrated on the CPU in double precision. GGX alpha is r squared,
// masking is height correlated Smith, the view lies in the XZ plane around +Z. Texel i of n holds the value at
// i / (n - 1), NdotV kept at least MinNdotV.
internal static class EnergyTables
{
    public const int DfgSize = 128, DielectricSize = 32;
    public const double MinNdotV = 1e-4;
    public const int BakeSamples = 1 << 14;

    public static double Axis(int i, int n) => i / (double)(n - 1);

    public static double NdotVAt(int i, int n) => Math.Max(Axis(i, n), MinNdotV);

    public static double EtaOfSqrtF0(double z) => (1 + z) / (1 - z);

    public static double Lambda(double mu, double alpha)
    {
        double mu2 = mu * mu;
        return (Math.Sqrt(1 + alpha * alpha * (1 - mu2) / mu2) - 1) * 0.5;
    }

    // Unpolarized Fresnel reflectance of a dielectric of relative IOR eta at least 1, seen from outside
    public static double Fresnel(double c, double eta)
    {
        if (double.IsPositiveInfinity(eta))
            return 1;
        double g = Math.Sqrt(eta * eta - 1 + c * c);
        double a = (g - c) / (g + c);
        double b = (c * (g + c) - 1) / (c * (g - c) + 1);
        return 0.5 * a * a * (1 + b * b);
    }

    public static double Schlick(double c, double F0) => F0 + (1 - F0) * Math.Pow(1 - c, 5);

    public static double F82(double c, double F0, double specularColor)
    {
        double mu = 1.0 / 7;
        double a = Schlick(mu, F0) * (1 - specularColor) / (mu * Math.Pow(1 - mu, 6));
        return Schlick(c, F0) - a * c * Math.Pow(1 - c, 6);
    }

    public static double RadicalInverse(uint i) => ReverseBits(i) * (1.0 / 4294967296.0);

    private static uint ReverseBits(uint v)
    {
        v = ((v >> 1) & 0x55555555u) | ((v & 0x55555555u) << 1);
        v = ((v >> 2) & 0x33333333u) | ((v & 0x33333333u) << 2);
        v = ((v >> 4) & 0x0F0F0F0Fu) | ((v & 0x0F0F0F0Fu) << 4);
        v = ((v >> 8) & 0x00FF00FFu) | ((v & 0x00FF00FFu) << 8);
        return (v >> 16) | (v << 16);
    }

    // A reflected sample from Heitz's visible normal sampling: VdotH and the estimator weight G2 / G1(V), zero below the horizon
    public static (double VdotH, double Weight) SampleVisible(double mu, double alpha, double u1, double u2)
    {
        double vx = Math.Sqrt(1 - mu * mu), vz = mu;
        double hx = alpha * vx, hz = vz;
        double hl = Math.Sqrt(hx * hx + hz * hz);
        hx /= hl; hz /= hl;
        // T1 = (0, 1, 0) for a view in the XZ plane, T2 = cross(Vh, T1)
        double r = Math.Sqrt(u1), phi = 2 * Math.PI * u2;
        double t1 = r * Math.Cos(phi), t2 = r * Math.Sin(phi);
        double s = 0.5 * (1 + hz);
        t2 = (1 - s) * Math.Sqrt(1 - t1 * t1) + s * t2;
        double tn = Math.Sqrt(Math.Max(0, 1 - t1 * t1 - t2 * t2));
        double nx = -hz * t2 + tn * hx, ny = t1, nz = hx * t2 + tn * hz;
        return Reflect(mu, vx, vz, alpha * nx, alpha * ny, Math.Max(0, nz), alpha, visible: true);
    }

    // A reflected sample from the NDF times NdotH: VdotH and the estimator weight G2 VdotH / (NdotV NdotH)
    public static (double VdotH, double Weight) SampleNormal(double mu, double alpha, double u1, double u2)
    {
        double a2 = alpha * alpha;
        double cos = Math.Sqrt((1 - u1) / (1 + (a2 - 1) * u1)), sin = Math.Sqrt(Math.Max(0, 1 - cos * cos));
        double phi = 2 * Math.PI * u2;
        return Reflect(mu, Math.Sqrt(1 - mu * mu), mu, sin * Math.Cos(phi), sin * Math.Sin(phi), cos, alpha, visible: false);
    }

    private static (double, double) Reflect(double mu, double vx, double vz, double hx, double hy, double hz, double alpha, bool visible)
    {
        double hl = Math.Sqrt(hx * hx + hy * hy + hz * hz);
        hx /= hl; hz /= hl;
        double c = vx * hx + vz * hz;
        double NdotL = 2 * c * hz - vz;
        if (NdotL <= 0 || c <= 0)
            return (c, 0);
        double lv = Lambda(mu, alpha), g2 = 1 / (1 + lv + Lambda(NdotL, alpha));
        return (c, visible ? g2 * (1 + lv) : g2 * c / (mu * hz));
    }

    public delegate (double VdotH, double Weight) Sampler(double mu, double alpha, double u1, double u2);

    // Split sum scale and bias and the F82 term at one (NdotV, r)
    public static (double Scale, double Bias, double F82) Dfg(double mu, double r, int samples, Sampler sample)
    {
        double alpha = r * r, scale = 0, bias = 0, f82 = 0;
        for (int i = 0; i < samples; i++)
        {
            (double c, double w) = sample(mu, alpha, (i + 0.5) / samples, RadicalInverse((uint)i));
            if (w == 0)
                continue;
            double m = 1 - c, m5 = m * m * m * m * m;
            scale += (1 - m5) * w;
            bias += m5 * w;
            f82 += c * m5 * m * w;
        }
        return (scale / samples, bias / samples, f82 / samples);
    }

    // Directional albedo of GGX reflection with any Fresnel of VdotH
    public static double Albedo(double mu, double r, Func<double, double> fresnel, int samples, Sampler sample)
    {
        double alpha = r * r, sum = 0;
        for (int i = 0; i < samples; i++)
        {
            (double c, double w) = sample(mu, alpha, (i + 0.5) / samples, RadicalInverse((uint)i));
            if (w != 0)
                sum += fresnel(c) * w;
        }
        return sum / samples;
    }

    public static double DielectricE(double mu, double r, double eta, int samples, Sampler sample)
        => eta <= 1 ? 0 : Albedo(mu, r, c => Fresnel(c, eta), samples, sample);

    public static double DielectricAt(int i, int j, int k, int samples, Sampler sample)
        => DielectricE(NdotVAt(i, DielectricSize), Axis(j, DielectricSize), EtaOfSqrtF0(Axis(k, DielectricSize)), samples, sample);

    // RGBA halves, NdotV along x, r along y
    public static ushort[] BakeDfg(int samples)
    {
        ushort[] texels = new ushort[DfgSize * DfgSize * 4];
        Parallel.For(0, DfgSize, j =>
        {
            for (int i = 0; i < DfgSize; i++)
            {
                (double scale, double bias, double f82) = Dfg(NdotVAt(i, DfgSize), Axis(j, DfgSize), samples, SampleVisible);
                int at = (j * DfgSize + i) * 4;
                texels[at] = Half(scale);
                texels[at + 1] = Half(bias);
                texels[at + 2] = Half(f82);
                texels[at + 3] = Half(0);
            }
        });
        return texels;
    }

    // R halves, NdotV along x, r along y, sqrt(F0) along z
    public static ushort[] BakeDielectric(int samples)
    {
        ushort[] texels = new ushort[DielectricSize * DielectricSize * DielectricSize];
        Parallel.For(0, DielectricSize * DielectricSize, row =>
        {
            int j = row % DielectricSize, k = row / DielectricSize;
            for (int i = 0; i < DielectricSize; i++)
                texels[(k * DielectricSize + j) * DielectricSize + i] = Half(DielectricAt(i, j, k, samples, SampleVisible));
        });
        return texels;
    }

    public const int AnisotropicNdotV = 32, AnisotropicRoughness = 16, AnisotropicLevels = 8, AnisotropicAzimuths = 8;
    public const int AnisotropicDepth = AnisotropicLevels * AnisotropicAzimuths;

    public static double LambdaAnisotropic(double x, double y, double z, double ax, double ay)
        => (Math.Sqrt(1 + (ax * ax * x * x + ay * ay * y * y) / (z * z)) - 1) * 0.5;

    // The split sum and F82 term of anisotropic GGX with the spec's alphas for r and anisotropy, the view at cosine mu
    // and azimuth phi from the tangent, by Heitz's visible normal sampling
    public static (double Scale, double Bias, double F82) AnisotropicDfg(double mu, double r, double anisotropy, double phi, int samples)
    {
        double b = 1 - anisotropy;
        double ax = Math.Max(r * r * Math.Sqrt(2 / (1 + b * b)), 0.002), ay = Math.Max(b * ax, 0.002);
        double sin = Math.Sqrt(1 - mu * mu);
        double vx = sin * Math.Cos(phi), vy = sin * Math.Sin(phi), vz = mu;
        double hx = ax * vx, hy = ay * vy, hz = vz, hl = Math.Sqrt(hx * hx + hy * hy + hz * hz);
        hx /= hl; hy /= hl; hz /= hl;
        double lensq = hx * hx + hy * hy;
        double t1x = 1, t1y = 0;
        if (lensq > 0)
        {
            double inv = 1 / Math.Sqrt(lensq);
            t1x = -hy * inv;
            t1y = hx * inv;
        }
        double t2x = -hz * t1y, t2y = hz * t1x, t2z = hx * t1y - hy * t1x;
        double lv = LambdaAnisotropic(vx, vy, vz, ax, ay);

        double scale = 0, bias = 0, f82 = 0;
        for (int i = 0; i < samples; i++)
        {
            double u1 = (i + 0.5) / samples, u2 = RadicalInverse((uint)i);
            double rr = Math.Sqrt(u1), angle = 2 * Math.PI * u2;
            double p1 = rr * Math.Cos(angle), p2 = rr * Math.Sin(angle);
            double s = 0.5 * (1 + hz);
            p2 = (1 - s) * Math.Sqrt(1 - p1 * p1) + s * p2;
            double pn = Math.Sqrt(Math.Max(0, 1 - p1 * p1 - p2 * p2));
            double nx = p1 * t1x + p2 * t2x + pn * hx, ny = p1 * t1y + p2 * t2y + pn * hy, nz = p2 * t2z + pn * hz;
            nx *= ax; ny *= ay; nz = Math.Max(nz, 0);
            double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            nx /= nl; ny /= nl; nz /= nl;
            double c = vx * nx + vy * ny + vz * nz;
            double lx = 2 * c * nx - vx, ly = 2 * c * ny - vy, lz = 2 * c * nz - vz;
            if (lz <= 0 || c <= 0)
                continue;
            double w = (1 + lv) / (1 + lv + LambdaAnisotropic(lx, ly, lz, ax, ay));
            double m = 1 - c, m5 = m * m * m * m * m;
            scale += (1 - m5) * w;
            bias += m5 * w;
            f82 += c * m5 * m * w;
        }
        return (scale / samples, bias / samples, f82 / samples);
    }

    public static double AzimuthAt(int m) => Axis(m, AnisotropicAzimuths) * Math.PI / 2;

    // Anisotropy levels crowd toward 1, where alpha_b falls fastest: level t holds a = 1 - (1 - t)^2
    public static double AnisotropyAt(int k) => 1 - Math.Pow(1 - Axis(k, AnisotropicLevels), 2);

    public static double AnisotropyLevel(double a) => 1 - Math.Sqrt(1 - Math.Clamp(a, 0, 1));

    // RGBA halves, NdotV along x, r along y, then along z a block of azimuths 0 to 90 degrees per anisotropy level
    public static ushort[] BakeAnisotropic(int samples)
    {
        ushort[] texels = new ushort[AnisotropicNdotV * AnisotropicRoughness * AnisotropicDepth * 4];
        Parallel.For(0, AnisotropicRoughness * AnisotropicDepth, row =>
        {
            int j = row % AnisotropicRoughness, z = row / AnisotropicRoughness;
            int k = z / AnisotropicAzimuths, m = z % AnisotropicAzimuths;
            for (int i = 0; i < AnisotropicNdotV; i++)
            {
                (double scale, double bias, double f82) = AnisotropicDfg(NdotVAt(i, AnisotropicNdotV), Axis(j, AnisotropicRoughness), AnisotropyAt(k), AzimuthAt(m), samples);
                int at = ((z * AnisotropicRoughness + j) * AnisotropicNdotV + i) * 4;
                texels[at] = Half(scale);
                texels[at + 1] = Half(bias);
                texels[at + 2] = Half(f82);
                texels[at + 3] = Half(0);
            }
        });
        return texels;
    }

    public static ushort[] EmbeddedAnisotropic() => Embedded("AnisotropicDfg.bin", AnisotropicNdotV * AnisotropicRoughness * AnisotropicDepth * 4);

    // Filtered like the GPU: linear across NdotV, r and azimuth, then between the two anisotropy levels around it
    public static Vector3 LookupAnisotropic(ushort[] texels, double mu, double r, double anisotropy, double azimuth)
    {
        double fx = Linear(mu, AnisotropicNdotV, out int x), fy = Linear(r, AnisotropicRoughness, out int y);
        double fm = Linear(azimuth, AnisotropicAzimuths, out int m), fk = Linear(AnisotropyLevel(anisotropy), AnisotropicLevels, out int k);
        Vector3 At(int i, int j, int z)
        {
            int at = ((z * AnisotropicRoughness + j) * AnisotropicNdotV + i) * 4;
            return new Vector3(FromHalf(texels[at]), FromHalf(texels[at + 1]), FromHalf(texels[at + 2]));
        }
        Vector3 Slice(int z)
        {
            Vector3 row0 = Vector3.Lerp(At(x, y, z), At(x + 1, y, z), (float)fx);
            Vector3 row1 = Vector3.Lerp(At(x, y + 1, z), At(x + 1, y + 1, z), (float)fx);
            return Vector3.Lerp(row0, row1, (float)fy);
        }
        Vector3 Level(int level) => Vector3.Lerp(Slice(level * AnisotropicAzimuths + m), Slice(level * AnisotropicAzimuths + m + 1), (float)fm);
        return Vector3.Lerp(Level(k), Level(k + 1), (float)fk);
    }

    // The Schlick split sum of a lobe at the F0 of eta, which scales the isotropic dielectric table for anisotropy
    public static double SchlickE(Vector3 dfg, double eta)
    {
        double f0 = (eta - 1) / (eta + 1);
        return f0 * f0 * dfg.X + dfg.Y;
    }

    public static ushort Half(double value) => BitConverter.HalfToUInt16Bits((Half)value);

    public static float FromHalf(ushort bits) => (float)BitConverter.UInt16BitsToHalf(bits);

    // The baked tables as the renderer embeds them
    public static ushort[] Embedded(string file, int count)
    {
        using Stream stream = typeof(Renderer).Assembly.GetManifestResourceStream("Graphite/Data/" + file)
            ?? throw new InvalidOperationException($"The renderer embeds no Data/{file}.");
        ushort[] texels = new ushort[count];
        stream.ReadExactly(MemoryMarshal.AsBytes(texels.AsSpan()));
        Assert(stream.ReadByte() == -1, $"Data/{file} is larger than {count} halves.");
        return texels;
    }

    public static ushort[] EmbeddedDfg() => Embedded("Dfg.bin", DfgSize * DfgSize * 4);

    public static ushort[] EmbeddedDielectric() => Embedded("DielectricE.bin", DielectricSize * DielectricSize * DielectricSize);

    public static void Write(string path, ushort[] texels) => File.WriteAllBytes(path, MemoryMarshal.AsBytes(texels.AsSpan()).ToArray());

    // Filtered like the GPU: a coordinate x of 0 to 1 lands on texel x (n - 1)
    public static double Linear(double x, int n, out int i0)
    {
        double t = Math.Clamp(x, 0, 1) * (n - 1);
        i0 = Math.Min((int)Math.Floor(t), n - 2);
        return t - i0;
    }

    public static Vector3 LookupDfg(ushort[] texels, double mu, double r)
    {
        double fx = Linear(mu, DfgSize, out int x), fy = Linear(r, DfgSize, out int y);
        Vector3 At(int i, int j)
        {
            int at = (j * DfgSize + i) * 4;
            return new Vector3(FromHalf(texels[at]), FromHalf(texels[at + 1]), FromHalf(texels[at + 2]));
        }
        Vector3 row0 = Vector3.Lerp(At(x, y), At(x + 1, y), (float)fx);
        Vector3 row1 = Vector3.Lerp(At(x, y + 1), At(x + 1, y + 1), (float)fx);
        return Vector3.Lerp(row0, row1, (float)fy);
    }

    public static double LookupDielectric(ushort[] texels, double mu, double r, double eta)
    {
        eta = Math.Max(eta, 1);
        double fx = Linear(mu, DielectricSize, out int x), fy = Linear(r, DielectricSize, out int y), fz = Linear((eta - 1) / (eta + 1), DielectricSize, out int z);
        double At(int i, int j, int k) => FromHalf(texels[(k * DielectricSize + j) * DielectricSize + i]);
        double Row(int j, int k) => At(x, j, k) + (At(x + 1, j, k) - At(x, j, k)) * fx;
        double Slice(int k) => Row(y, k) + (Row(y + 1, k) - Row(y, k)) * fy;
        return Slice(z) + (Slice(z + 1) - Slice(z)) * fz;
    }

    // Energy.glsl's helpers on the CPU
    public static double F82Tint(double F0, double specularColor) => Schlick(1.0 / 7, F0) * (1 - specularColor) / (1.0 / 7 * Math.Pow(6.0 / 7, 6));

    public static double MetalFavg(double F0, double specularColor) => F0 + (1 - F0) / 21 - F82Tint(F0, specularColor) / 126;

    public static double DielectricFavg(double eta) => (Math.Max(eta, 1) - 1) / (4.08567 + 1.00071 * Math.Max(eta, 1));

    public static double TurquinFactor(double E1, double Favg) => 1 + Favg * (1 - E1) / Math.Max(E1, 1e-4);

    public static double TurquinMetal(Vector3 dfg, double F0, double specularColor, double specularWeight)
    {
        double E = (F0 * dfg.X + dfg.Y - F82Tint(F0, specularColor) * dfg.Z) * specularWeight;
        return E * TurquinFactor(dfg.X + dfg.Y, MetalFavg(F0, specularColor) * specularWeight);
    }

    public static double TurquinDielectric(Vector3 dfg, double E, double eta) => E * TurquinFactor(dfg.X + dfg.Y, DielectricFavg(eta));

    // The cosine weighted hemispherical average of a Fresnel of cos, by the midpoint rule
    public static double Favg(Func<double, double> fresnel, int steps = 100000)
    {
        double sum = 0;
        for (int i = 0; i < steps; i++)
        {
            double mu = (i + 0.5) / steps;
            sum += 2 * mu * fresnel(mu);
        }
        return sum / steps;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidDataException(message);
    }
}

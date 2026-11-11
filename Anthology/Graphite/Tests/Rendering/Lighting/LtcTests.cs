using System;
using System.Collections.Generic;
using System.IO;

using Xunit;

using D3 = Prowl.Graphite.Rendering.Tests.LtcTables.D3;

namespace Prowl.Graphite.Rendering.Tests;

// The LTC table and integrals against brute force. Set GRAPHITE_BAKE_TABLES=1 to run BakeLtc, which writes
// Rendering/Library/Data/Ltc.bin.
public sealed class LtcTests
{
    private static D3 RandomUnit(Random random)
    {
        while (true)
        {
            D3 v = new(random.NextDouble() * 2 - 1, random.NextDouble() * 2 - 1, random.NextDouble() * 2 - 1);
            double l = v.Length;
            if (l > 0.1 && l <= 1)
                return v / l;
        }
    }

    // A light of a kind in front of the point, facing it more or less, as its centre and two half extent vectors
    private static (D3 Center, D3 H1, D3 H2) RandomLight(Random random, double maxSize)
    {
        D3 center = RandomUnit(random) * (0.5 + 3 * random.NextDouble());
        if (center.Z < -0.3)
            center = new D3(center.X, center.Y, -center.Z);
        D3 h1 = RandomUnit(random) * (0.05 + maxSize * random.NextDouble());
        D3 axis = RandomUnit(random);
        D3 h2 = D3.Cross(h1, axis).Normalized * (0.05 + maxSize * random.NextDouble());
        return (center, h1, h2);
    }

    // The cosine over pi integrated over a light by brute force, a rect, disk or thin strip, one sided toward its normal
    private static double CosineTruth(int kind, D3 center, D3 h1, D3 h2, Func<D3, double> lobe)
    {
        const int steps = 200;
        double sum = 0;
        D3 normal = D3.Cross(h1, h2).Normalized;
        if (D3.Dot(normal, center) > 0)
            normal = -normal;
        for (int i = 0; i < steps; i++)
        {
            for (int j = 0; j < steps; j++)
            {
                double u = (i + 0.5) / steps * 2 - 1, w = (j + 0.5) / steps * 2 - 1;
                if (kind == 1 && u * u + w * w > 1)
                    continue;
                if (kind == 3 && !InOctagon(u, w))
                    continue;
                D3 q = center + h1 * u + h2 * w;
                double d2 = D3.Dot(q, q);
                D3 l = q / Math.Sqrt(d2);
                double area = 4 * h1.Length * h2.Length / (steps * steps);
                double facing = kind == 2 ? Math.Abs(D3.Dot(l, normal)) : Math.Max(-D3.Dot(l, normal), 0);
                sum += lobe(l) * facing / d2 * area;
            }
        }
        return sum;
    }

    [Fact]
    public void BakeLtc()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("GRAPHITE_BAKE_TABLES") == "1", "Set GRAPHITE_BAKE_TABLES=1 to rebake the LTC table.");
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string data = Path.Combine(dir.FullName, "Rendering", "Library", "Data");
            if (Directory.Exists(data))
            {
                EnergyTables.Write(Path.Combine(data, "Ltc.bin"), LtcTables.Bake());
                return;
            }
        }
        throw new DirectoryNotFoundException("No Rendering/Library/Data above the test binaries.");
    }

    [Fact]
    public void Ltc_IsWhatTheFitterBakes()
    {
        ushort[] table = LtcTables.Embedded();
        foreach ((int i, int j) in new[] { (0, 10), (20, 30), (50, 40), (63, 63) })
        {
            double alpha = LtcTables.AlphaOf(LtcTables.Axis(j));
            double[] start = [Math.Log(alpha), Math.Log(alpha), 0];
            for (int k = 0; k < i; k++)
                LtcTables.FitInverse(LtcTables.NdotVAt(k), alpha, start, out start);
            (double m00, double m02, double m20, double m22) = LtcTables.FitInverse(LtcTables.NdotVAt(i), alpha, start, out _);
            int at = (j * LtcTables.Size + i) * 4;
            Assert.Equal(EnergyTables.Half(m00), table[at]);
            Assert.Equal(EnergyTables.Half(m02), table[at + 1]);
            Assert.Equal(EnergyTables.Half(m20), table[at + 2]);
            Assert.Equal(EnergyTables.Half(m22), table[at + 3]);
        }
    }

    [Fact]
    public void Integrals_OfTheClampedCosine_MatchBruteForce()
    {
        var random = new Random(3);
        List<string> report = new();
        double worst = 0;
        for (int n = 0; n < 60; n++)
        {
            int kind = n % 3;
            (D3 center, D3 h1, D3 h2) = RandomLight(random, kind == 2 ? 2 : 1);
            if (kind == 2)
                h2 = h2.Normalized * 0.02;
            double truth = CosineTruth(kind, center, h1, h2, l => Math.Max(l.Z, 0) / Math.PI);
            double estimate = kind switch
            {
                0 => LtcTables.Polygon(OneSided(center, h1, h2)),
                1 => FacingDisk(center, h1, h2),
                _ => LtcTables.Strip(center - h1, center + h1, h2 * 2),
            };
            // A disk is the octagon of its area, a few percent off near the rim
            double error = Math.Abs(estimate - truth) / Math.Max(truth, 1e-4);
            if (truth > 1e-4)
                worst = Math.Max(worst, kind == 1 ? error / 2.5 : error);
            report.Add($"{"rect disk strip".Split(' ')[kind]} truth {truth:G4} estimate {estimate:G4}");
        }
        Assert.True(worst < 0.02, string.Join(Environment.NewLine, report));
    }

    // The circumradius over the radius of the octagon of a disk's area, as Ltc.glsl takes a disk
    private static readonly double Octagon = Math.Sqrt(Math.PI / (2 * Math.Sqrt(2)));

    // Whether a point of the unit square lies in the regular octagon with corners on its axes at 1
    private static bool InOctagon(double u, double w)
    {
        double apothem = Math.Cos(Math.PI / 8);
        for (int k = 0; k < 8; k++)
        {
            double angle = Math.PI / 8 + k * Math.PI / 4;
            if (u * Math.Cos(angle) + w * Math.Sin(angle) > apothem)
                return false;
        }
        return true;
    }

    // The lobe's share over a light in the transformed cosine's space, for an inverse matrix in the view's frame
    private static double LtcShare(int kind, LtcTables.M3 inverse, D3 center, D3 h1, D3 h2)
    {
        D3 c = inverse * center, a = inverse * h1, b = inverse * h2;
        return kind switch
        {
            0 => LtcTables.Polygon(OneSided(center, h1, h2).Length == 0 ? [] : [c - a - b, c + a - b, c + a + b, c - a + b]),
            1 => OneSided(center, h1, h2).Length == 0 ? 0 : LtcTables.Disk(c, a, b),
            _ => LtcTables.Strip(c - a, c + a, b * 2),
        };
    }

    [Fact]
    public void TheTable_MatchesTheLobe_OverDisksAroundTheReflection()
    {
        ushort[] table = LtcTables.Embedded();
        List<string> report = new();
        double worst = 0;
        foreach ((double roughness, double mu) in new[] { (0.35, 0.95), (0.35, 0.6), (0.35, 0.25), (0.6, 0.6), (0.15, 0.6) })
        {
            double alpha = LtcTables.AlphaOf(roughness);
            LtcTables.M3 inverse = LtcTables.Lookup(table, mu, roughness);
            D3 v = new(Math.Sqrt(1 - mu * mu), 0, mu);
            double albedo = CosineTruthHemisphere(v, alpha);
            // Disks facing the point, their centres on and either side of the mirror direction in its plane
            D3 r = new(-v.X, 0, v.Z);
            D3 across = new(0, 1, 0), inPlane = D3.Cross(r, across).Normalized;
            foreach (double half in new[] { 0.05, 0.15, 0.3 })
            {
                foreach (double tilt in new[] { 0.0, -0.3, 0.3 })
                {
                    D3 dir = (r + inPlane * tilt).Normalized;
                    if (dir.Z - Math.Tan(half) < 0.05)
                        continue;
                    D3 a = D3.Cross(dir, across).Normalized * Math.Tan(half), b = across * Math.Tan(half);
                    double truth = CosineTruth(3, dir, a * Octagon, b * Octagon, l => LtcTables.Brdf(v, l, alpha)) / albedo;
                    double ltc = LtcShare(1, inverse, dir, a, b);
                    worst = Math.Max(worst, Math.Abs(ltc - truth));
                    report.Add($"roughness {roughness} mu {mu} disk {half} tilt {tilt}: truth {truth:F4} ltc {ltc:F4}");
                }
            }
        }
        Assert.True(worst < 0.06, string.Join(Environment.NewLine, report));
    }

    private static double CosineTruthHemisphere(D3 v, double alpha)
    {
        const int steps = 400;
        double sum = 0;
        for (int i = 0; i < steps; i++)
        {
            for (int j = 0; j < steps; j++)
            {
                double z = (i + 0.5) / steps, phi = 2 * Math.PI * (j + 0.5) / steps, r = Math.Sqrt(1 - z * z);
                sum += LtcTables.Brdf(v, new D3(r * Math.Cos(phi), r * Math.Sin(phi), z), alpha);
            }
        }
        return sum * 2 * Math.PI / (steps * steps);
    }

    // A rect's corners, none when its back faces the point
    private static D3[] OneSided(D3 center, D3 h1, D3 h2)
    {
        D3 normal = D3.Cross(h1, h2);
        if (D3.Dot(normal, center) > 0)
            normal = -normal;
        return D3.Dot(normal, center) < 0 ? [center - h1 - h2, center + h1 - h2, center + h1 + h2, center - h1 + h2] : [];
    }

    private static double FacingDisk(D3 center, D3 h1, D3 h2) => LtcTables.Disk(center, h1, h2);
}

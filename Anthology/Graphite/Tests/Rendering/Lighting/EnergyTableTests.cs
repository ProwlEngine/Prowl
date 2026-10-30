using System;
using System.IO;
using System.Numerics;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

// The baked GGX energy tables against fresh CPU integration, and the Turquin helpers built on them. Set
// GRAPHITE_BAKE_TABLES=1 to run BakeTables, which writes Rendering/Library/Data/Dfg.bin, DielectricE.bin and
// AnisotropicDfg.bin.
public sealed class EnergyTableTests
{
    // Half rounding near 1 plus quasi Monte Carlo error of the independent sampler
    private const double TableTolerance = 2e-3;
    private const int VerifySamples = 1 << 18;

    private static readonly ushort[] s_dfg = EnergyTables.EmbeddedDfg();
    private static readonly ushort[] s_dielectric = EnergyTables.EmbeddedDielectric();

    private static readonly int[] s_dfgPoints = [7, 25, 64, 101, 127];
    private static readonly int[] s_dfgRoughness = [0, 13, 38, 77, 127];
    private static readonly int[] s_dielectricPoints = [2, 9, 20, 31];
    private static readonly int[] s_dielectricRoughness = [0, 6, 15, 31];
    private static readonly int[] s_dielectricIor = [1, 5, 10, 16, 31];

    private static Vector3 DfgTexel(int i, int j)
    {
        int at = (j * EnergyTables.DfgSize + i) * 4;
        return new Vector3(EnergyTables.FromHalf(s_dfg[at]), EnergyTables.FromHalf(s_dfg[at + 1]), EnergyTables.FromHalf(s_dfg[at + 2]));
    }

    private static double DielectricTexel(int i, int j, int k)
        => EnergyTables.FromHalf(s_dielectric[(k * EnergyTables.DielectricSize + j) * EnergyTables.DielectricSize + i]);

    private static void Near(double expected, double got, double tolerance, string what)
        => Assert.True(Math.Abs(expected - got) <= tolerance, $"{what}: expected {expected}, got {got}");

    [SkippableFact]
    public void BakeTables()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("GRAPHITE_BAKE_TABLES") == "1", "Set GRAPHITE_BAKE_TABLES=1 to rebake the energy tables.");
        string data = DataFolder();
        EnergyTables.Write(Path.Combine(data, "Dfg.bin"), EnergyTables.BakeDfg(EnergyTables.BakeSamples));
        EnergyTables.Write(Path.Combine(data, "DielectricE.bin"), EnergyTables.BakeDielectric(EnergyTables.BakeSamples));
        EnergyTables.Write(Path.Combine(data, "AnisotropicDfg.bin"), EnergyTables.BakeAnisotropic(EnergyTables.BakeSamples));
    }

    [Fact]
    public void Dfg_IsWhatTheGeneratorBakes()
    {
        foreach (int j in s_dfgRoughness)
        foreach (int i in s_dfgPoints)
        {
            var want = EnergyTables.Dfg(EnergyTables.NdotVAt(i, EnergyTables.DfgSize), EnergyTables.Axis(j, EnergyTables.DfgSize), EnergyTables.BakeSamples, EnergyTables.SampleVisible);
            Vector3 got = DfgTexel(i, j);
            Assert.Equal(EnergyTables.Half(want.Scale), EnergyTables.Half(got.X));
            Assert.Equal(EnergyTables.Half(want.Bias), EnergyTables.Half(got.Y));
            Assert.Equal(EnergyTables.Half(want.F82), EnergyTables.Half(got.Z));
        }
    }

    [Fact]
    public void Dfg_MatchesNormalSampledIntegration()
    {
        foreach (int j in s_dfgRoughness)
        foreach (int i in s_dfgPoints)
        {
            var want = EnergyTables.Dfg(EnergyTables.NdotVAt(i, EnergyTables.DfgSize), EnergyTables.Axis(j, EnergyTables.DfgSize), VerifySamples, EnergyTables.SampleNormal);
            Vector3 got = DfgTexel(i, j);
            Near(want.Scale, got.X, TableTolerance, $"scale at ({i}, {j})");
            Near(want.Bias, got.Y, TableTolerance, $"bias at ({i}, {j})");
            Near(want.F82, got.Z, TableTolerance, $"F82 at ({i}, {j})");
        }
    }

    private static readonly ushort[] s_anisotropic = EnergyTables.EmbeddedAnisotropic();

    private static Vector3 AnisotropicTexel(int i, int j, int z)
    {
        int at = ((z * EnergyTables.AnisotropicRoughness + j) * EnergyTables.AnisotropicNdotV + i) * 4;
        return new Vector3(EnergyTables.FromHalf(s_anisotropic[at]), EnergyTables.FromHalf(s_anisotropic[at + 1]), EnergyTables.FromHalf(s_anisotropic[at + 2]));
    }

    [Fact]
    public void AnisotropicDfg_IsWhatTheGeneratorBakes()
    {
        foreach (int z in new[] { 0, 7, 20, 45, 63 })
        foreach (int j in new[] { 0, 5, 15 })
        foreach (int i in new[] { 1, 12, 31 })
        {
            var want = EnergyTables.AnisotropicDfg(EnergyTables.NdotVAt(i, EnergyTables.AnisotropicNdotV), EnergyTables.Axis(j, EnergyTables.AnisotropicRoughness),
                EnergyTables.AnisotropyAt(z / EnergyTables.AnisotropicAzimuths), EnergyTables.AzimuthAt(z % EnergyTables.AnisotropicAzimuths), EnergyTables.BakeSamples);
            Vector3 got = AnisotropicTexel(i, j, z);
            Assert.Equal(EnergyTables.Half(want.Scale), EnergyTables.Half(got.X));
            Assert.Equal(EnergyTables.Half(want.Bias), EnergyTables.Half(got.Y));
            Assert.Equal(EnergyTables.Half(want.F82), EnergyTables.Half(got.Z));
        }
    }

    // Between texels on every axis the filtered table stays close to fresh integration, its coarse grid's interpolation
    public const double AnisotropicInterpolationTolerance = 0.01;

    // With no anisotropy every azimuth holds the isotropic split sum, within the coarser grid's interpolation
    [Fact]
    public void AnisotropicDfg_WithNoAnisotropy_IsTheIsotropicDfg()
    {
        foreach (double azimuth in new[] { 0, 0.3, 1 })
        foreach (double r in new[] { 0, 0.2, 0.55, 1 })
        foreach (double mu in new[] { 0.05, 0.3, 0.7, 1 })
        {
            Vector3 want = EnergyTables.LookupDfg(s_dfg, mu, r), got = EnergyTables.LookupAnisotropic(s_anisotropic, mu, r, 0, azimuth);
            Near(want.X, got.X, AnisotropicInterpolationTolerance, $"scale at mu {mu} r {r} azimuth {azimuth}");
            Near(want.Y, got.Y, AnisotropicInterpolationTolerance, $"bias at mu {mu} r {r} azimuth {azimuth}");
            Near(want.Z, got.Z, AnisotropicInterpolationTolerance, $"F82 at mu {mu} r {r} azimuth {azimuth}");
        }
    }

    [Fact]
    public void AnisotropicDfg_BetweenTexels_MatchesFreshIntegration()
    {
        foreach (double a in new[] { 0.2, 0.5, 0.75, 0.95 })
        foreach (double azimuth in new[] { 0.1, 0.45, 0.8 })
        foreach (double r in new[] { 0.12, 0.37, 0.63, 0.9 })
        foreach (double mu in new[] { 0.21, 0.5, 0.83 })
        {
            var want = EnergyTables.AnisotropicDfg(mu, r, a, azimuth * Math.PI / 2, 1 << 15);
            Vector3 got = EnergyTables.LookupAnisotropic(s_anisotropic, mu, r, a, azimuth);
            string at = $"mu {mu} r {r} a {a} azimuth {azimuth}";
            Near(want.Scale + want.Bias, got.X + got.Y, AnisotropicInterpolationTolerance, $"E1 at {at}");
            Near(want.Bias, got.Y, AnisotropicInterpolationTolerance, $"bias at {at}");
            Near(want.F82, got.Z, AnisotropicInterpolationTolerance, $"F82 at {at}");
        }
    }

    [Fact]
    public void DielectricE_IsWhatTheGeneratorBakes()
    {
        foreach (int k in s_dielectricIor)
        foreach (int j in s_dielectricRoughness)
        foreach (int i in s_dielectricPoints)
            Assert.Equal(EnergyTables.Half(EnergyTables.DielectricAt(i, j, k, EnergyTables.BakeSamples, EnergyTables.SampleVisible)), EnergyTables.Half(DielectricTexel(i, j, k)));
    }

    [Fact]
    public void DielectricE_MatchesNormalSampledIntegration()
    {
        foreach (int k in s_dielectricIor)
        foreach (int j in s_dielectricRoughness)
        foreach (int i in s_dielectricPoints)
            Near(EnergyTables.DielectricAt(i, j, k, VerifySamples, EnergyTables.SampleNormal), DielectricTexel(i, j, k), TableTolerance, $"E at ({i}, {j}, {k})");
    }

    [Fact]
    public void DielectricE_IsZeroAtEtaOne()
    {
        const int n = EnergyTables.DielectricSize;
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
            Assert.Equal(0, DielectricTexel(i, j, 0));
        Assert.Equal(0, EnergyTables.LookupDielectric(s_dielectric, 0.5, 0.5, 1.0));
        Assert.Equal(0, EnergyTables.LookupDielectric(s_dielectric, 0.5, 0.5, 0.7));
    }

    [Fact]
    public void DielectricE_OfASmoothSurfaceIsFresnel()
    {
        const int n = EnergyTables.DielectricSize;
        for (int k = 1; k < n - 1; k++)
        for (int i = 0; i < n; i++)
        {
            double want = EnergyTables.Fresnel(EnergyTables.NdotVAt(i, n), EnergyTables.EtaOfSqrtF0(EnergyTables.Axis(k, n)));
            Near(want, DielectricTexel(i, 0, k), 1e-3 * want + 1e-6, $"E at ({i}, 0, {k})");
        }
    }

    [Fact]
    public void Dfg_OfASmoothSurfaceIsSchlick()
    {
        const int n = EnergyTables.DfgSize;
        for (int i = 0; i < n; i++)
        {
            double mu = EnergyTables.NdotVAt(i, n), m5 = Math.Pow(1 - mu, 5);
            Vector3 got = DfgTexel(i, 0);
            Near(1 - m5, got.X, 1e-3, $"scale at {i}");
            Near(m5, got.Y, 1e-3, $"bias at {i}");
            Near(mu * m5 * (1 - mu), got.Z, 1e-3, $"F82 at {i}");
        }
    }

    [Fact]
    public void Tables_AreBoundedAndMonotonic()
    {
        const int n = EnergyTables.DfgSize, m = EnergyTables.DielectricSize;
        const double slack = 1e-3;
        for (int i = 0; i < n; i++)
        for (int j = 0; j < n; j++)
        {
            Vector3 t = DfgTexel(i, j);
            float E1 = t.X + t.Y;
            Assert.True(t.X >= 0 && t.Y >= 0 && t.Z >= 0 && E1 <= 1 + slack, $"({i}, {j}) holds {t}");
            // Toward grazing, height correlated Smith masking brings E1 back up to 1 as the lobe widens
            if (j > 0 && EnergyTables.Axis(i, n) >= 0.2)
                Assert.True(E1 <= DfgTexel(i, j - 1).X + DfgTexel(i, j - 1).Y + slack, $"E1 grows with roughness at ({i}, {j})");
        }

        double[] white = new double[m * m];
        for (int j = 0; j < m; j++)
        for (int i = 0; i < m; i++)
            white[j * m + i] = EnergyTables.Albedo(EnergyTables.NdotVAt(i, m), EnergyTables.Axis(j, m), _ => 1, 1 << 12, EnergyTables.SampleVisible);

        for (int k = 0; k < m; k++)
        for (int j = 0; j < m; j++)
        for (int i = 0; i < m; i++)
        {
            double E = DielectricTexel(i, j, k);
            Assert.True(E >= 0 && E <= white[j * m + i] + slack, $"E at ({i}, {j}, {k}) is {E}, white GGX {white[j * m + i]}");
            // Fresnel itself stops growing with the IOR near grazing
            if (k > 0 && EnergyTables.Axis(i, m) >= 0.2)
                Assert.True(E >= DielectricTexel(i, j, k - 1) - slack * 0.1, $"E falls with the IOR at ({i}, {j}, {k})");
        }
    }

    [Fact]
    public void MetalE_MatchesF82Integration()
    {
        (double F0, double Tint)[] metals = [(0.95, 1.0), (0.9, 0.8), (0.55, 0.6), (0.2, 0.3), (0.04, 0.0)];
        foreach ((double F0, double tint) in metals)
        foreach (double r in (double[])[0.05, 0.3, 0.6, 1.0])
        foreach (double mu in (double[])[0.1, 0.4, 0.8, 1.0])
        {
            double want = EnergyTables.Albedo(mu, r, c => EnergyTables.F82(c, F0, tint), 1 << 16, EnergyTables.SampleVisible);
            Vector3 dfg = EnergyTables.LookupDfg(s_dfg, mu, r);
            double got = F0 * dfg.X + dfg.Y - EnergyTables.F82Tint(F0, tint) * dfg.Z;
            Near(want, got, TableTolerance, $"F0 {F0} tint {tint} r {r} NdotV {mu}");
        }
    }

    [Fact]
    public void MetalFavg_IsTheHemisphericalAverageOfF82()
    {
        foreach (double F0 in (double[])[0.04, 0.5, 0.95])
        foreach (double tint in (double[])[0.0, 0.5, 1.0])
            Near(EnergyTables.Favg(c => EnergyTables.F82(c, F0, tint)), EnergyTables.MetalFavg(F0, tint), 1e-6, $"F0 {F0} tint {tint}");
    }

    // Kulla and Conty's fit stays within 0.0066 of the exact average from eta 1 to 5
    [Fact]
    public void DielectricFavg_FitsTheExactAverage()
    {
        for (double eta = 1; eta <= 5; eta += 0.05)
            Near(EnergyTables.Favg(c => EnergyTables.Fresnel(c, eta), 20000), EnergyTables.DielectricFavg(eta), 0.0066, $"eta {eta}");
    }

    [Fact]
    public void TurquinMetal_GivesAWhiteMetalAnAlbedoOfOne()
    {
        for (double r = 0; r <= 1; r += 0.05)
        for (double mu = 0.02; mu <= 1; mu += 0.07)
            Near(1, EnergyTables.TurquinMetal(EnergyTables.LookupDfg(s_dfg, mu, r), 1, 1, 1), 1e-5, $"r {r} NdotV {mu}");
    }

    [Fact]
    public void Turquin_AddsEnergyOnlyWhereGgxLosesIt()
    {
        for (double r = 0.05; r <= 1; r += 0.05)
        {
            Vector3 dfg = EnergyTables.LookupDfg(s_dfg, 0.5, r);
            double single = (0.6 * dfg.X + dfg.Y) * 0.8;
            double metal = EnergyTables.TurquinMetal(dfg, 0.6, 1, 0.8);
            Assert.True(metal >= single && metal <= 0.8 + 1e-3, $"metal at r {r}: {single} compensated to {metal}");

            double E = EnergyTables.LookupDielectric(s_dielectric, 0.5, r, 1.5);
            double dielectric = EnergyTables.TurquinDielectric(dfg, E, 1.5);
            Assert.True(dielectric >= E && dielectric <= E / (dfg.X + dfg.Y) + 1e-3, $"dielectric at r {r}: {E} compensated to {dielectric}");
        }
        Near(1, EnergyTables.TurquinFactor(EnergyTables.LookupDfg(s_dfg, 0.5, 0).X + EnergyTables.LookupDfg(s_dfg, 0.5, 0).Y, 1), 2e-3, "smooth GGX loses nothing");
    }

    private static string DataFolder()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string data = Path.Combine(dir.FullName, "Rendering", "Library", "Data");
            if (Directory.Exists(data))
                return data;
        }
        throw new DirectoryNotFoundException("No Rendering/Library/Data above the test binaries.");
    }
}

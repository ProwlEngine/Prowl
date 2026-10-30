using System;
using System.Collections.Generic;
using System.Numerics;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

// The white furnace on the CPU: the per light BSDF of the PBR closure integrated over the hemisphere, each specular lobe
// by visible normal sampling of its own GGX lobe in its own frame, diffuse by cosine sampling and fuzz uniformly. Bulk
// subsurface wraps over the whole sphere, a thin sheet transmits from the far hemisphere and thin walled transmission
// passes the uniform surroundings straight through. A white surface reflects and transmits all it receives.
public sealed class PbrFurnaceTests
{
    private const int Samples = 1 << 16;

    private static readonly float[] s_roughness = [0.05f, 0.1f, 0.25f, 0.5f, 0.75f, 1f];
    private static readonly double[] s_cosines = [0.2, 0.4, 0.6, 0.8, 1.0];

    // Heitz's visible normal sampling of anisotropic GGX for a view in the local frame, the normal along +Z
    private static Vector3 SampleVisibleNormal(Vector3 v, double ax, double ay, double u1, double u2)
    {
        Vector3 vh = Vector3.Normalize(new Vector3((float)(ax * v.X), (float)(ay * v.Y), v.Z));
        float lensq = vh.X * vh.X + vh.Y * vh.Y;
        Vector3 t1 = lensq > 0 ? new Vector3(-vh.Y, vh.X, 0) / MathF.Sqrt(lensq) : Vector3.UnitX;
        Vector3 t2 = Vector3.Cross(vh, t1);
        double r = Math.Sqrt(u1), phi = 2 * Math.PI * u2;
        double p1 = r * Math.Cos(phi), p2 = r * Math.Sin(phi);
        double s = 0.5 * (1 + vh.Z);
        p2 = (1 - s) * Math.Sqrt(1 - p1 * p1) + s * p2;
        Vector3 nh = t1 * (float)p1 + t2 * (float)p2 + vh * (float)Math.Sqrt(Math.Max(0, 1 - p1 * p1 - p2 * p2));
        return Vector3.Normalize(new Vector3((float)(ax * nh.X), (float)(ay * nh.Y), MathF.Max(0, nh.Z)));
    }

    private static Vector3 ToLocal(in GgxLobe lobe, Vector3 w) => new(Vector3.Dot(lobe.T, w), Vector3.Dot(lobe.B, w), Vector3.Dot(lobe.N, w));

    private static Vector3 ToWorld(in GgxLobe lobe, Vector3 w) => lobe.T * w.X + lobe.B * w.Y + lobe.N * w.Z;

    // A reflected direction by visible normal sampling of a lobe and the sample's weight f cos / pdf over the lobe's BRDF
    private static Vector3 SampleLobe(in GgxLobe lobe, Vector3 v, double u1, double u2, out double pdf)
    {
        Vector3 h = ToWorld(lobe, SampleVisibleNormal(ToLocal(lobe, v), lobe.AlphaT, lobe.AlphaB, u1, u2));
        Vector3 l = Vector3.Reflect(-v, h);
        double g1 = 1 / (1 + lobe.Lambda(v));
        pdf = lobe.D(h) * g1 / (4 * Vector3.Dot(lobe.N, v));
        return l;
    }

    private static Vector3 Frame(Vector3 n, double x, double y, double z)
    {
        Vector3 t = PbrReference.TangentFor(n, Vector3.UnitX), b = Vector3.Cross(n, t);
        return Vector3.Normalize(t * (float)x + b * (float)y + n * (float)z);
    }

    // A unit vector tilted from +Z by degrees toward +X, turned about Z by turn degrees
    public static Vector3 Tilted(double degrees, double turn = 0)
    {
        double a = degrees * Math.PI / 180, b = turn * Math.PI / 180;
        return Vector3.Normalize(new Vector3((float)(Math.Sin(a) * Math.Cos(b)), (float)(Math.Sin(a) * Math.Sin(b)), (float)Math.Cos(a)));
    }

    // The directional albedo toward a view at cosine mu to the base normal +Z, reflected and transmitted
    private static Vector3 Albedo(in PbrInputs inputs, double mu, Vector3? tangent = null, Vector3? coatNormal = null)
    {
        (Vector3 front, Vector3 back) = Albedos(inputs, mu, tangent, coatNormal);
        return front + back;
    }

    // What the view at cosine mu receives from the near hemisphere and from the far one, through the sheet or straight
    private static (Vector3 Front, Vector3 Back) Albedos(in PbrInputs inputs, double mu, Vector3? tangent = null, Vector3? coatNormal = null)
    {
        Vector3 n = Vector3.UnitZ;
        Vector3 v = new((float)Math.Sqrt(1 - mu * mu), 0, (float)mu);
        PbrReference closure = new(inputs, n, v, tangent, coatNormal);

        Vector3 total = Vector3.Zero, back = Vector3.Zero;
        for (int i = 0; i < Samples; i++)
        {
            double u1 = (i + 0.5) / Samples, u2 = EnergyTables.RadicalInverse((uint)i);

            Vector3 l = SampleLobe(closure.Base, v, u1, u2, out double pdf);
            if (l.Z > 0)
                total += closure.Specular(l) * (float)(l.Z / pdf);

            if (inputs.CoatWeight > 0)
            {
                l = SampleLobe(closure.Coat, v, u1, u2, out pdf);
                double cl = Vector3.Dot(closure.Coat.N, l);
                if (cl > 0)
                    total += new Vector3((float)(closure.CoatLobe(l) * cl / pdf));
            }

            double sin = Math.Sqrt(u1), angle = 2 * Math.PI * u2;
            Vector3 cosine = new((float)(sin * Math.Cos(angle)), (float)(sin * Math.Sin(angle)), (float)Math.Sqrt(1 - u1));
            total += closure.Diffuse(cosine) * MathF.PI;

            if (inputs.FuzzWeight > 0)
            {
                double z = u1, r = Math.Sqrt(1 - z * z);
                Vector3 uniform = Frame(closure.FuzzNormal, r * Math.Cos(angle), r * Math.Sin(angle), z);
                total += closure.FuzzAlbedo * (float)(closure.FuzzLobe(uniform) * 2 * Math.PI);
            }

            if (inputs.SubsurfaceWeight > 0)
            {
                double z = 2 * u1 - 1, r = Math.Sqrt(1 - z * z);
                Vector3 sphere = new((float)(r * Math.Cos(angle)), (float)(r * Math.Sin(angle)), (float)z);
                Vector3 wrapped = closure.Wrap(sphere) * (float)(4 * Math.PI);
                if (z >= 0)
                    total += wrapped;
                else
                    back += wrapped;
                back += closure.SheetBack(cosine with { Z = -cosine.Z }) * MathF.PI;
            }
        }
        return (total / Samples, back / Samples + closure.Transmittance);
    }

    private static void AssertWhite(in PbrInputs inputs, double tolerance, string what, Vector3? tangent = null, Vector3? coatNormal = null)
    {
        foreach (double mu in s_cosines)
        {
            if (coatNormal is { } cn && Vector3.Dot(cn, new Vector3((float)Math.Sqrt(1 - mu * mu), 0, (float)mu)) < 0.2f)
                continue;
            Vector3 albedo = Albedo(inputs, mu, tangent, coatNormal);
            for (int c = 0; c < 3; c++)
                Assert.True(Math.Abs(albedo[c] - 1) <= tolerance, $"{what} at NdotV {mu} channel {c}: albedo {albedo[c]}");
        }
    }

    [Fact]
    public void WhiteMetal_ReflectsEverything_AtEveryRoughness()
    {
        foreach (float r in s_roughness)
            AssertWhite(new PbrInputs(Vector3.One, Metalness: 1, Roughness: r), 0.005, $"metal r {r}");
    }

    [Fact]
    public void WhiteEon_ReflectsEverything_AtEveryDiffuseRoughness()
    {
        foreach (float rd in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
            AssertWhite(new PbrInputs(Vector3.One, SpecularWeight: 0, DiffuseRoughness: rd), 0.005, $"EON rd {rd}");
    }

    public static IEnumerable<object[]> Dielectrics()
    {
        foreach (float ior in new[] { 1f, 1.33f, 1.5f, 2f, 3f })
            foreach (float weight in new[] { 0f, 0.5f, 1f })
                yield return [ior, weight];
    }

    [Theory]
    [MemberData(nameof(Dielectrics))]
    public void WhiteDielectric_SpecularAndDiffuseTogether_ReflectEverything(float ior, float weight)
    {
        foreach (float r in s_roughness)
            AssertWhite(new PbrInputs(Vector3.One, Roughness: r, DiffuseRoughness: 0.5f, SpecularWeight: weight, Ior: ior), 0.01, $"dielectric ior {ior} weight {weight} r {r}");
    }

    [Fact]
    public void MixesOfMetalAndDielectric_ReflectEverything()
    {
        foreach (float metalness in new[] { 0.25f, 0.5f, 0.75f })
            foreach (float r in s_roughness)
                AssertWhite(new PbrInputs(Vector3.One, Metalness: metalness, Roughness: r, Ior: 1.7f), 0.01, $"metalness {metalness} r {r}");
    }

    [Fact]
    public void AnIorBelowOne_ReflectsLikeItsInverse()
    {
        Vector3 n = Vector3.UnitZ, v = Vector3.Normalize(new Vector3(0.5f, 0, 1));
        PbrReference below = new(new PbrInputs(Vector3.One, Ior: 1 / 1.5f), n, v);
        PbrReference above = new(new PbrInputs(Vector3.One, Ior: 1.5f), n, v);
        Assert.Equal(above.SpecularAlbedo.X, below.SpecularAlbedo.X, 4);
    }

    // White bases a white coat may sit on: a dielectric above and below the coat's IOR, a metal, and a mix
    private static readonly PbrInputs[] s_whiteBases =
    [
        new(Vector3.One, Roughness: 0.4f, DiffuseRoughness: 0.5f, Ior: 1.5f),
        new(Vector3.One, Roughness: 0.2f, Ior: 1.3f),
        new(Vector3.One, Metalness: 1, Roughness: 0.3f),
        new(Vector3.One, Metalness: 0.5f, Roughness: 0.6f, Ior: 2f),
    ];

    public static IEnumerable<object[]> CoatDarkenings() => [[0f], [1f]];

    [Theory]
    [MemberData(nameof(CoatDarkenings))]
    public void WhiteCoat_OverWhiteBases_ReflectsEverything(float darkening)
    {
        foreach (PbrInputs surface in s_whiteBases)
            foreach (float ior in new[] { 1.3f, 1.6f, 2.5f })
                foreach (float r in new[] { 0.05f, 0.4f, 0.8f })
                    AssertWhite(surface with { CoatWeight = 1, CoatIor = ior, CoatRoughness = r, CoatDarkening = darkening }, 0.01, $"coat ior {ior} r {r} darkening {darkening} over {surface}");
        AssertWhite(s_whiteBases[0] with { CoatWeight = 0.5f, CoatRoughness = 0.3f, CoatDarkening = darkening }, 0.01, $"half coat darkening {darkening}");
    }

    [Fact]
    public void WhiteFuzz_ReflectsEverything()
    {
        foreach (float r in new[] { 0.1f, 0.3f, 0.5f, 0.8f, 1f })
        {
            AssertWhite(s_whiteBases[0] with { FuzzWeight = 1, FuzzRoughness = r }, 0.01, $"fuzz r {r}");
            AssertWhite(s_whiteBases[2] with { FuzzWeight = 0.6f, FuzzRoughness = r, CoatWeight = 0.5f }, 0.01, $"fuzz r {r} over a half coated metal");
        }
    }

    // Anisotropy keeps the energy whichever way the tangent turns. Looser than isotropic lobes: metals take the
    // anisotropic table's coarse grid, about 1%, and dielectrics scale the isotropic table by the Schlick split sums'
    // ratio, up to about 3% at anisotropy 0.9.
    [Fact]
    public void SpecularAndCoatAnisotropy_KeepTheEnergy()
    {
        foreach (float a in new[] { 0.3f, 0.6f, 0.9f })
        {
            foreach (float r in new[] { 0.25f, 0.5f, 1f })
            {
                foreach (double turn in new[] { 0.0, 35.0, 90.0 })
                {
                    Vector3 tangent = Tilted(90, turn);
                    AssertWhite(new PbrInputs(Vector3.One, Metalness: 1, Roughness: r) { Anisotropy = a }, 0.03, $"metal r {r} anisotropy {a} turn {turn}", tangent);
                    AssertWhite(s_whiteBases[0] with { Roughness = r, Anisotropy = a }, 0.03, $"dielectric r {r} anisotropy {a} turn {turn}", tangent);
                    AssertWhite(s_whiteBases[0] with { CoatWeight = 1, CoatRoughness = r, CoatAnisotropy = a }, 0.03, $"coat r {r} anisotropy {a} turn {turn}", tangent);
                }
            }
        }
    }

    // A coat normal apart from the base's still reflects everything, each lobe over its own hemisphere, for views above both
    [Fact]
    public void ACoatNormalApartFromTheBase_KeepsTheEnergy()
    {
        foreach (double turn in new[] { 0.0, 90.0, 180.0 })
            foreach (float r in new[] { 0.05f, 0.4f })
                AssertWhite(s_whiteBases[0] with { CoatWeight = 1, CoatRoughness = r }, 0.01, $"coat r {r} normal turned {turn}", coatNormal: Tilted(15, turn));
    }

    // The film's interference moves energy between channels and keeps it in luminance
    public const double FilmBand = 0.15;

    [Fact]
    public void ThinFilm_KeepsLuminance_EachChannelWithinItsBand()
    {
        foreach (PbrInputs surface in new[] { s_whiteBases[0], s_whiteBases[2], s_whiteBases[3], s_whiteBases[0] with { CoatWeight = 1, CoatRoughness = 0.2f } })
        {
            foreach (float thickness in new[] { 0.1f, 0.3f, 0.5f, 0.8f })
            {
                foreach (float ior in new[] { 1.2f, 1.4f, 2f })
                {
                    PbrInputs filmed = surface with { FilmWeight = 1, FilmThickness = thickness, FilmIor = ior };
                    foreach (double mu in s_cosines)
                    {
                        Vector3 albedo = Albedo(filmed, mu);
                        string what = $"film {thickness} um ior {ior} over {surface} at NdotV {mu}";
                        Assert.True(Math.Abs(PbrReference.Luminance(albedo) - 1) <= 0.01, $"{what}: luminance {PbrReference.Luminance(albedo)}");
                        for (int c = 0; c < 3; c++)
                            Assert.True(Math.Abs(albedo[c] - 1) <= FilmBand, $"{what} channel {c}: {albedo[c]}");
                    }
                }
            }
        }
    }

    // A film over a white metal reflects white, whatever its thickness
    [Fact]
    public void ThinFilm_OverAWhiteMetal_StaysWhite()
    {
        foreach (float thickness in new[] { 0.2f, 0.6f })
            AssertWhite(new PbrInputs(Vector3.One, Metalness: 1, Roughness: 0.3f) { FilmWeight = 1, FilmThickness = thickness }, 0.005, $"film {thickness} um over white metal");
    }

    // White subsurface in bulk keeps the energy at any radius, the light it wraps from behind included
    [Fact]
    public void WhiteSubsurface_InBulk_ReflectsEverything()
    {
        foreach (float weight in new[] { 0.5f, 1f })
            foreach (float radius in new[] { 0f, 1f, 5f })
                foreach (float rd in new[] { 0f, 0.6f })
                    AssertWhite(new PbrInputs(Vector3.One, DiffuseRoughness: rd) { SubsurfaceWeight = weight, SubsurfaceColor = Vector3.One, SubsurfaceRadius = radius }, 0.01, $"subsurface {weight} radius {radius} rd {rd}");
        AssertWhite(s_whiteBases[0] with { SubsurfaceWeight = 1, SubsurfaceColor = Vector3.One, CoatWeight = 1, CoatRoughness = 0.3f, FuzzWeight = 0.5f }, 0.01, "subsurface under a coat and fuzz");
    }

    // A white thin sheet reflects and transmits everything, whichever way it scatters
    [Fact]
    public void WhiteThinSheet_ReflectsAndTransmitsEverything()
    {
        foreach (float g in new[] { -0.6f, 0f, 0.8f })
            foreach (float rd in new[] { 0f, 1f })
                foreach (float weight in new[] { 0.5f, 1f })
                    AssertWhite(new PbrInputs(Vector3.One, DiffuseRoughness: rd) { ThinWalled = true, SubsurfaceWeight = weight, SubsurfaceColor = Vector3.One, SubsurfaceAnisotropy = g }, 0.01, $"sheet {weight} g {g} rd {rd}");
        AssertWhite(s_whiteBases[0] with { ThinWalled = true, SubsurfaceWeight = 1, SubsurfaceColor = Vector3.One, SubsurfaceAnisotropy = 0.4f, CoatWeight = 1, CoatRoughness = 0.3f, FuzzWeight = 0.5f }, 0.01, "sheet under a coat and fuzz");
    }

    // The sheet reflects 0.5 S (1 - g) and transmits 0.5 S (1 + g), together S
    [Fact]
    public void TheSheet_SplitsSBetweenReflectionAndTransmission()
    {
        Vector3 S = new(0.9f, 0.5f, 0.2f);
        foreach (float g in new[] { -0.5f, 0f, 0.7f })
        {
            PbrInputs sheet = new(Vector3.Zero, SpecularWeight: 0, DiffuseRoughness: 0.5f) { ThinWalled = true, SubsurfaceWeight = 1, SubsurfaceColor = S, SubsurfaceAnisotropy = g };
            PbrReference closure = new(sheet, Vector3.UnitZ, Vector3.UnitZ);
            for (int c = 0; c < 3; c++)
            {
                Assert.Equal(0.5 * S[c] * (1 - g), closure.SheetReflection[c], 5);
                Assert.Equal(0.5 * S[c] * (1 + g), closure.SheetTransmission[c], 5);
                Assert.Equal(S[c], closure.SheetReflection[c] + closure.SheetTransmission[c], 5);
            }
            foreach (double mu in s_cosines)
            {
                (Vector3 front, Vector3 back) = Albedos(sheet, mu);
                for (int c = 0; c < 3; c++)
                {
                    Assert.True(Math.Abs(front[c] - 0.5 * S[c] * (1 - g)) <= 0.005, $"g {g} NdotV {mu} channel {c}: reflected {front[c]}");
                    Assert.True(Math.Abs(back[c] - 0.5 * S[c] * (1 + g)) <= 0.005, $"g {g} NdotV {mu} channel {c}: transmitted {back[c]}");
                }
            }
        }
    }

    // Thin walled transmission straight through, with and without a sheet beside it, keeps the energy
    [Fact]
    public void WhiteThinWalledTransmission_PassesEverything()
    {
        foreach (float t in new[] { 0.5f, 1f })
            foreach (float r in new[] { 0.05f, 0.4f, 1f })
            {
                AssertWhite(new PbrInputs(Vector3.One, Roughness: r) { ThinWalled = true, TransmissionWeight = t }, 0.01, $"transmission {t} r {r}");
                AssertWhite(new PbrInputs(Vector3.One, Roughness: r, Ior: 1.8f) { ThinWalled = true, TransmissionWeight = t, SubsurfaceWeight = 0.5f, SubsurfaceColor = Vector3.One, SubsurfaceAnisotropy = 0.3f }, 0.01, $"transmission {t} beside a sheet r {r}");
            }
    }

    // Metal, dielectric, subsurface and transmission mixed still keep the energy
    [Fact]
    public void MixesOfMetalDielectricAndSubsurface_ReflectEverything()
    {
        foreach (float metalness in new[] { 0.3f, 0.7f })
        {
            foreach (float r in new[] { 0.1f, 0.5f, 1f })
            {
                PbrInputs mixed = new(Vector3.One, Metalness: metalness, Roughness: r, Ior: 1.7f) { SubsurfaceWeight = 0.6f, SubsurfaceColor = Vector3.One };
                AssertWhite(mixed, 0.01, $"bulk metalness {metalness} r {r}");
                AssertWhite(mixed with { ThinWalled = true, SubsurfaceAnisotropy = -0.3f, TransmissionWeight = 0.4f }, 0.01, $"thin walled metalness {metalness} r {r}");
            }
        }
    }

    // Transmission into a volume keeps the energy: of depth 0 tinted white, deeper untinted, mixed with metal and subsurface
    [Fact]
    public void WhiteTransmission_PassesEverything()
    {
        foreach (float t in new[] { 0.5f, 1f })
            foreach (float r in new[] { 0.05f, 0.4f, 1f })
                foreach (float depth in new[] { 0f, 2f })
                {
                    AssertWhite(new PbrInputs(Vector3.One, Roughness: r) { TransmissionWeight = t, TransmissionDepth = depth }, 0.01, $"transmission {t} r {r} depth {depth}");
                    AssertWhite(new PbrInputs(Vector3.One, Metalness: 0.3f, Roughness: r, Ior: 1.7f) { TransmissionWeight = t, TransmissionDepth = depth, SubsurfaceWeight = 0.5f, SubsurfaceColor = Vector3.One }, 0.01, $"transmission {t} beside metal and subsurface r {r} depth {depth}");
                }
    }

    // A white volume scattering white, mu_a 0 after the shift, passes everything along any path, unscattered or scattered
    [Fact]
    public void AWhiteScatteringVolume_PassesEverything()
    {
        foreach (float depth in new[] { 0.1f, 1f, 5f })
            foreach (float scatter in new[] { 0.3f, 1f, 4f })
                foreach (float length in new[] { 0f, 0.5f, 3f, 50f })
                {
                    Vector3 throughput = PbrReference.VolumeThroughput(Vector3.One, new Vector3(scatter), depth, length);
                    for (int c = 0; c < 3; c++)
                        Assert.True(Math.Abs(throughput[c] - 1) <= 1e-5, $"depth {depth} scatter {scatter} length {length} channel {c}: {throughput[c]}");
                }

        Vector3 absorbing = PbrReference.VolumeThroughput(new Vector3(0.5f, 0.25f, 1), Vector3.Zero, 2, 4);
        Assert.Equal(0.25, absorbing.X, 5);
        Assert.Equal(0.0625, absorbing.Y, 5);
        Assert.Equal(1, absorbing.Z, 5);
    }
}

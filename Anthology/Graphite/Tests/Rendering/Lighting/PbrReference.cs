using System;
using System.Numerics;

namespace Prowl.Graphite.Rendering.Tests;

// The PBR model's inputs the lobes read, at the spec's defaults
internal readonly record struct PbrInputs(
    Vector3 BaseColor,
    float Metalness = 0,
    float Roughness = 0.3f,
    float DiffuseRoughness = 0,
    float BaseWeight = 1,
    float SpecularWeight = 1,
    Vector3? SpecularColor = null,
    float Ior = 1.5f)
{
    public Vector3 Tint => SpecularColor ?? Vector3.One;

    public float Anisotropy { get; init; }
    public float SubsurfaceWeight { get; init; }
    public Vector3 SubsurfaceColor { get; init; } = new(0.8f);
    public float SubsurfaceRadius { get; init; } = 1;
    public Vector3 SubsurfaceRadiusScale { get; init; } = new(1, 0.5f, 0.25f);
    public float SubsurfaceAnisotropy { get; init; }

    public bool ThinWalled { get; init; }
    public float TransmissionWeight { get; init; }
    public Vector3 TransmissionColor { get; init; } = Vector3.One;
    public float TransmissionDepth { get; init; }

    public float CoatWeight { get; init; }
    public Vector3 CoatColor { get; init; } = Vector3.One;
    public float CoatRoughness { get; init; }
    public float CoatAnisotropy { get; init; }
    public float CoatIor { get; init; } = 1.6f;
    public float CoatDarkening { get; init; } = 1;

    public float FuzzWeight { get; init; }
    public Vector3 FuzzColor { get; init; } = Vector3.One;
    public float FuzzRoughness { get; init; } = 0.5f;

    public float FilmWeight { get; init; }
    public float FilmThickness { get; init; } = 0.5f;
    public float FilmIor { get; init; } = 1.4f;
}

// A GGX lobe's frame and alphas, isotropic when both alphas match
internal readonly record struct GgxLobe(Vector3 N, Vector3 T, Vector3 B, double AlphaT, double AlphaB)
{
    public static double Lambda(Vector3 w, Vector3 n, Vector3 t, Vector3 b, double at, double ab)
    {
        double z = Vector3.Dot(n, w), x = Vector3.Dot(t, w) * at, y = Vector3.Dot(b, w) * ab;
        return z <= 0 ? double.PositiveInfinity : (-1 + Math.Sqrt(1 + (x * x + y * y) / (z * z))) / 2;
    }

    public double Lambda(Vector3 w) => Lambda(w, N, T, B, AlphaT, AlphaB);

    public double D(Vector3 h)
    {
        double x = Vector3.Dot(T, h) / AlphaT, y = Vector3.Dot(B, h) / AlphaB, z = Vector3.Dot(N, h);
        double d = x * x + y * y + z * z;
        return 1 / (Math.PI * AlphaT * AlphaB * d * d);
    }

    // Height correlated Smith G2 over 4 NdotV NdotL, the shader's expression
    public double Visibility(Vector3 v, Vector3 l)
    {
        double nv = Math.Max(Vector3.Dot(N, v), 1e-4), nl = Math.Clamp(Vector3.Dot(N, l), 0, 1);
        double tv = Vector3.Dot(T, v), bv = Vector3.Dot(B, v), tl = Vector3.Dot(T, l), bl = Vector3.Dot(B, l);
        double a = nl * Math.Sqrt(AlphaT * AlphaT * tv * tv + AlphaB * AlphaB * bv * bv + nv * nv);
        double c = nv * Math.Sqrt(AlphaT * AlphaT * tl * tl + AlphaB * AlphaB * bl * bl + nl * nl);
        return 0.5 / Math.Max(a + c, 1e-7);
    }
}

// Graphite/PBR/Closure.glsl on the CPU at the FULL tier, the same expressions and tables: EON under a GGX dielectric
// with exact Fresnel beside an F82 metal, Turquin compensated, the diffuse throughput 1 - E_spec of the view, a thin film
// in the specular Fresnel, the coat with absorption, darkening and roughening, and the fuzz on top. Subsurface as the
// wrapped bulk lobe or the thin walled sheet. Transmission always takes its share of the base here, as the shader does
// only with PBR_TRANSMISSION, thin walled at color^(1 / mu_t), a volume of depth 0 tinted, a deeper one untinted.
internal sealed class PbrReference
{
    public const float MinAlpha = 0.002f;
    public const double FuzzMinRoughness = 0.01;

    private static readonly ushort[] s_dfg = EnergyTables.EmbeddedDfg();
    private static readonly ushort[] s_dielectric = EnergyTables.EmbeddedDielectric();
    private static readonly ushort[] s_anisotropic = EnergyTables.EmbeddedAnisotropic();

    private const double FonC1 = 0.5 - 2 / (3 * Math.PI);
    private const double FonC2 = 2.0 / 3 - 28 / (15 * Math.PI);

    private readonly Vector3 _n, _v;
    private readonly double _nv, _rd, _eta, _dielectricScale;
    private readonly Vector3 _rho, _tint, _metalTint, _metalScale, _diffuseSingle, _diffuseMultiple, _dielectricFilm, _metalFilm;

    private readonly double _coatWeight, _coatScale, _coatThroughput, _coatEta, _coatInvMuO;
    private readonly Vector3 _coatLogColor, _darkening;

    private readonly double _sheetSingle, _sheetMultiple;
    private readonly Vector3 _subsurfaceAlbedo, _wrap;

    private readonly Vector3 _fuzzNormal, _fuzzAxis;
    private readonly double _fuzzA, _fuzzB, _fuzzThroughput;

    public GgxLobe Base { get; }
    public GgxLobe Coat { get; }
    public double Roughness { get; }
    public double DiffuseWeight { get; }
    public Vector3 SpecularAlbedo { get; }
    public Vector3 DiffuseAlbedo { get; }
    public Vector3 Film { get; }
    public double CoatAlbedo { get; }
    public Vector3 CoatedBase { get; }
    public Vector3 FuzzAlbedo { get; }
    public Vector3 FuzzNormal => _fuzzNormal;
    public Vector3 SheetReflection { get; }
    public Vector3 SheetTransmission { get; }

    // What of the light straight behind a thin walled surface reaches the view, through the coat and fuzz
    public Vector3 Transmittance { get; }

    public static float Alpha(float roughness) => MathF.Max(roughness * roughness, MinAlpha);

    public static (double T, double B) AnisotropicAlpha(double r, double anisotropy)
    {
        double b = 1 - Math.Clamp(anisotropy, 0, 1);
        double at = r * r * Math.Sqrt(2 / (1 + b * b));
        return (Math.Max(at, MinAlpha), Math.Max(b * at, MinAlpha));
    }

    // The view's azimuth about a lobe's normal, 0 along its tangent to 1 along its bitangent, folded into one quadrant
    public static double Azimuth(in GgxLobe lobe, Vector3 v)
        => Math.Atan2(Math.Abs(Vector3.Dot(lobe.B, v)), Math.Max(Math.Abs(Vector3.Dot(lobe.T, v)), 1e-6)) * 2 / Math.PI;

    public static Vector3 TangentFor(Vector3 n, Vector3 t)
    {
        Vector3 p = t - n * Vector3.Dot(n, t);
        if (p.LengthSquared() > 1e-8f)
            return Vector3.Normalize(p);
        return MathF.Abs(n.Y) < 0.999f ? Vector3.Normalize(Vector3.Cross(n, Vector3.UnitY)) : Vector3.Normalize(Vector3.Cross(n, Vector3.UnitX));
    }

    public PbrReference(in PbrInputs p, Vector3 n, Vector3 v, Vector3? tangent = null, Vector3? coatNormal = null, Vector3? coatTangent = null)
    {
        _n = n;
        _v = v;
        _nv = Math.Max(Vector3.Dot(n, v), 1e-4);

        double C = Math.Clamp(p.CoatWeight, 0, 1);
        double rc = Math.Clamp(p.CoatRoughness, 0, 1);
        double nc = Math.Max(p.CoatIor, 1e-4);
        double specularRoughness = Math.Clamp(p.Roughness, 0, 1);
        double rb = Lerp(specularRoughness, Math.Pow(Math.Min(1, Math.Pow(specularRoughness, 4) + 2 * Math.Pow(rc, 4)), 0.25), C);
        double baseEta = Math.Max(p.Ior, 1e-4);
        double eta = Lerp(baseEta, nc > baseEta ? nc / baseEta : baseEta / nc, C);
        double outer = Lerp(1, nc, C);

        double alpha = Alpha((float)rb);
        Roughness = Math.Sqrt(alpha);
        Vector3 t = TangentFor(n, tangent ?? Vector3.UnitX);
        (double at, double ab) = AnisotropicAlpha(rb, p.Anisotropy);
        Base = new GgxLobe(n, t, Vector3.Cross(n, t), at, ab);

        double M = Math.Clamp(p.Metalness, 0, 1);
        double weight = Math.Max(p.SpecularWeight, 0);
        bool anisotropic = p.Anisotropy != 0 || p.CoatAnisotropy != 0;
        Vector3 dfg = EnergyTables.LookupDfg(s_dfg, _nv, Roughness);
        Vector3 isotropicDfg = dfg;
        if (anisotropic)
            dfg = EnergyTables.LookupAnisotropic(s_anisotropic, _nv, Roughness, p.Anisotropy, Azimuth(Base, v));
        double E1 = dfg.X + dfg.Y;

        double f0 = (eta - 1) / (eta + 1);
        double unmodulatedF0 = f0 * f0;
        _eta = ModulatedEta(eta, weight);
        _eta = _eta < 1 ? 1 / _eta : _eta;
        double dielectricTurquin = EnergyTables.TurquinFactor(E1, EnergyTables.DielectricFavg(_eta));
        double dielectricE = EnergyTables.LookupDielectric(s_dielectric, _nv, Roughness, _eta) * dielectricTurquin
            * (anisotropic ? EnergyTables.SchlickE(dfg, _eta) / Math.Max(EnergyTables.SchlickE(isotropicDfg, _eta), 1e-6) : 1);
        _tint = p.Tint;
        _dielectricScale = (1 - M) * dielectricTurquin;

        _rho = Vector3.Clamp(p.BaseWeight * p.BaseColor, Vector3.Zero, Vector3.One);
        Vector3 metalE = Vector3.Zero, metalTint = Vector3.Zero, metalScale = Vector3.Zero;
        for (int c = 0; c < 3; c++)
        {
            double rho = _rho[c], s = _tint[c];
            metalTint[c] = (float)EnergyTables.F82Tint(rho, s);
            metalScale[c] = (float)(M * weight * EnergyTables.TurquinFactor(E1, EnergyTables.MetalFavg(rho, s) * weight));
            metalE[c] = (float)EnergyTables.TurquinMetal(dfg, rho, s, weight);
        }
        _metalTint = metalTint;
        Vector3 dielectricE3 = new((float)dielectricE);

        double filmWeight = p.FilmThickness > 0 ? Math.Clamp(p.FilmWeight, 0, 1) : 0;
        if (filmWeight > 0)
        {
            double filmCos = RefractedCosine(_nv, outer);
            Vector3 filmDielectric = ThinFilmDielectric(filmCos, outer, p.FilmIor, outer * ModulatedEta(baseEta / outer, weight), p.FilmThickness);
            Vector3 filmMetal = ThinFilmMetal(filmCos, outer, p.FilmIor, _rho, _tint, p.FilmThickness);
            Vector3 dielectricFilm = Vector3.Zero, metalFilm = Vector3.Zero;
            for (int c = 0; c < 3; c++)
            {
                double dt = EnergyTables.TurquinFactor(E1, filmDielectric[c]);
                double mt = EnergyTables.TurquinFactor(E1, filmMetal[c] * weight);
                dielectricE3[c] = (float)Lerp(dielectricE3[c], filmDielectric[c] * E1 * dt, filmWeight);
                metalE[c] = (float)Lerp(metalE[c], weight * filmMetal[c] * E1 * mt, filmWeight);
                dielectricFilm[c] = (float)((1 - M) * filmWeight * filmDielectric[c] * dt);
                metalFilm[c] = (float)(M * weight * filmWeight * filmMetal[c] * mt);
            }
            _dielectricFilm = dielectricFilm;
            _metalFilm = metalFilm;
            _dielectricScale *= 1 - filmWeight;
            metalScale *= (float)(1 - filmWeight);
            Film = Vector3.Lerp(filmDielectric, filmMetal, (float)M);
        }
        _metalScale = metalScale;
        SpecularAlbedo = (float)(1 - M) * dielectricE3 * _tint + (float)M * metalE;

        _rd = Math.Clamp(p.DiffuseRoughness, 0, 1);
        double average = FonAverage(_rd);
        DiffuseWeight = (1 - M) * (1 - Luminance(dielectricE3));
        double opaque = DiffuseWeight;
        Vector3 transmission = Vector3.Zero;
        if (p.ThinWalled || p.TransmissionWeight > 0)
        {
            double T = Math.Clamp(p.TransmissionWeight, 0, 1);
            Vector3 tint = Vector3.Clamp(p.TransmissionColor, Vector3.Zero, Vector3.One);
            if (p.ThinWalled)
                tint = Pow(tint, 1 / Math.Max(RefractedCosine(_nv, baseEta < 1 ? 1 / baseEta : baseEta), 1e-4));
            else if (p.TransmissionDepth > 0)
                tint = Vector3.One;
            transmission = (float)((1 - M) * T) * tint * (Vector3.One - dielectricE3);
            opaque *= 1 - T;
        }
        double S = Math.Clamp(p.SubsurfaceWeight, 0, 1);
        Vector3 subsurface = (float)(S * opaque) * Vector3.Clamp(p.SubsurfaceColor, Vector3.Zero, Vector3.One);
        double diffuse = (1 - S) * opaque;
        double multipleShape = Math.Max(1e-7, 1 - FonAlbedo(_nv, _rd)) / (Math.Max(1e-7, 1 - average) * Math.PI);
        _diffuseSingle = _rho * (float)(diffuse / ((1 + FonC1 * _rd) * Math.PI));
        _diffuseMultiple = MultipleAlbedo(_rho, average) * (float)(diffuse * multipleShape);
        DiffuseAlbedo = EonAlbedo(_rho, _rd, _nv) * (float)diffuse;
        if (S > 0 && p.ThinWalled)
        {
            double g = Math.Clamp(p.SubsurfaceAnisotropy, -1, 1);
            _sheetSingle = 1 / ((1 + FonC1 * _rd) * Math.PI);
            _sheetMultiple = multipleShape;
            SheetReflection = subsurface * (float)(0.5 * (1 - g));
            SheetTransmission = subsurface * (float)(0.5 * (1 + g));
            DiffuseAlbedo += SheetReflection;
        }
        else if (S > 0)
        {
            Vector3 reach = (float)Math.Max(p.SubsurfaceRadius, 0) * Vector3.Max(p.SubsurfaceRadiusScale, Vector3.Zero);
            _subsurfaceAlbedo = subsurface;
            _wrap = reach / (Vector3.One + reach);
            DiffuseAlbedo += subsurface;
        }

        Vector3 cn = Vector3.Normalize(coatNormal ?? n);
        double coatNv = Math.Max(Vector3.Dot(cn, v), 1e-4);
        double coatAlpha = Alpha((float)rc);
        double coatRoughness = Math.Sqrt(coatAlpha);
        Vector3 ct = TangentFor(cn, coatTangent ?? t);
        (double cat, double cab) = AnisotropicAlpha(rc, p.CoatAnisotropy);
        Coat = new GgxLobe(cn, ct, Vector3.Cross(cn, ct), cat, cab);
        _coatWeight = C;
        _coatEta = nc < 1 ? 1 / nc : nc;
        Vector3 coatDfg = EnergyTables.LookupDfg(s_dfg, coatNv, coatRoughness);
        double coatAnisotropicScale = 1;
        if (anisotropic)
        {
            Vector3 coatIsotropic = coatDfg;
            coatDfg = EnergyTables.LookupAnisotropic(s_anisotropic, coatNv, coatRoughness, p.CoatAnisotropy, Azimuth(Coat, v));
            coatAnisotropicScale = EnergyTables.SchlickE(coatDfg, _coatEta) / Math.Max(EnergyTables.SchlickE(coatIsotropic, _coatEta), 1e-6);
        }
        double coatTurquin = EnergyTables.TurquinFactor(coatDfg.X + coatDfg.Y, EnergyTables.DielectricFavg(_coatEta));
        double coatE = EnergyTables.LookupDielectric(s_dielectric, coatNv, coatRoughness, _coatEta) * coatAnisotropicScale * coatTurquin;
        _coatScale = C * coatTurquin;
        CoatAlbedo = C * coatE;
        _coatThroughput = 1 - coatE;
        _coatLogColor = new Vector3(MathF.Log2(Math.Clamp(p.CoatColor.X, 1e-6f, 1)), MathF.Log2(Math.Clamp(p.CoatColor.Y, 1e-6f, 1)), MathF.Log2(Math.Clamp(p.CoatColor.Z, 1e-6f, 1)));
        _coatInvMuO = 1 / Math.Max(RefractedCosine(coatNv, _coatEta), 1e-4);

        double coatF = FresnelDielectric(coatNv, nc);
        double Kr = 1 - (1 - FresnelAverage(nc)) / (nc * nc);
        double rDielectric = Lerp(1, specularRoughness, Math.Clamp(weight * unmodulatedF0, 0, 1));
        double K = Lerp(coatF, Kr, Lerp(rDielectric, specularRoughness, M));
        Vector3 Eb = Vector3.Lerp(Vector3.Lerp(_rho, Vector3.Clamp(p.SubsurfaceColor, Vector3.Zero, Vector3.One), Math.Clamp(p.SubsurfaceWeight, 0, 1)), (float)weight * _rho, (float)M);
        Vector3 darkening = Vector3.Zero;
        for (int c = 0; c < 3; c++)
            darkening[c] = (float)Lerp(1, (1 - K) / Math.Max(1 - Eb[c] * K, 1e-4), C * Math.Clamp(p.CoatDarkening, 0, 1));
        _darkening = darkening;
        CoatedBase = Absorbed(_coatInvMuO);

        double fuzzWeight = Math.Clamp(p.FuzzWeight, 0, 1);
        double fuzzRoughness = Math.Clamp(p.FuzzRoughness, FuzzMinRoughness, 1);
        _fuzzNormal = C > 0 ? Vector3.Normalize(Vector3.Lerp(n, cn, (float)C)) : n;
        double fuzzNv = Math.Clamp(Vector3.Dot(_fuzzNormal, v), 1e-4, 1);
        double fuzzE = FuzzAlbedoFit(fuzzNv, fuzzRoughness);
        Vector3 x = v - _fuzzNormal * (float)fuzzNv;
        _fuzzAxis = x.LengthSquared() > 1e-10f ? Vector3.Normalize(x) : TangentFor(_fuzzNormal, Vector3.UnitX);
        (_fuzzA, _fuzzB) = FuzzLtc(fuzzNv, fuzzRoughness);
        FuzzAlbedo = (float)(fuzzWeight * fuzzE) * Vector3.Clamp(p.FuzzColor, Vector3.Zero, Vector3.One);
        _fuzzThroughput = 1 - fuzzWeight * fuzzE;
        Transmittance = transmission * CoatedBase * (float)_fuzzThroughput;
    }

    // The base's share through the coat for a mean of the inverse refracted cosines, darkening included
    private Vector3 Absorbed(double invMu)
    {
        Vector3 a = Vector3.Zero;
        for (int c = 0; c < 3; c++)
            a[c] = (float)(Lerp(1, _coatThroughput * Math.Pow(2, _coatLogColor[c] * invMu), _coatWeight) * _darkening[c]);
        return a;
    }

    private Vector3 CoatedBaseToward(Vector3 l)
    {
        double invMuI = 1 / Math.Max(RefractedCosine(Math.Clamp(Vector3.Dot(Coat.N, l), 0, 1), _coatEta), 1e-4);
        return Absorbed(0.5 * (invMuI + _coatInvMuO)) * (float)_fuzzThroughput;
    }

    public static double Lerp(double a, double b, double t) => a + (b - a) * t;

    public static double Luminance(Vector3 c) => 0.2126 * c.X + 0.7152 * c.Y + 0.0722 * c.Z;

    public static double RefractedCosine(double mu, double eta) => Math.Sqrt(Math.Max(1 - (1 - mu * mu) / (eta * eta), 0));

    // Graphite/PBR/Transmission.glsl's volume: what reaches the far side along length, unscattered plus scattered, of a white background
    public static Vector3 VolumeThroughput(Vector3 color, Vector3 scatter, double depth, double length)
    {
        Vector3 muA = Vector3.Zero, muS = Vector3.Zero;
        for (int c = 0; c < 3; c++)
        {
            muS[c] = (float)(Math.Max(scatter[c], 0) / depth);
            muA[c] = (float)(-Math.Log(Math.Clamp(color[c], 1e-6, 1)) / depth) - muS[c];
        }
        float lowest = MathF.Min(muA.X, MathF.Min(muA.Y, muA.Z));
        if (lowest < 0)
            muA -= new Vector3(lowest);
        Vector3 result = Vector3.Zero;
        for (int c = 0; c < 3; c++)
        {
            double extinction = muA[c] + muS[c];
            double direct = Math.Exp(-extinction * length);
            result[c] = (float)(direct + (1 - direct) * muS[c] / Math.Max(extinction, 1e-8));
        }
        return result;
    }

    private static Vector3 Pow(Vector3 v, double e) => new((float)Math.Pow(v.X, e), (float)Math.Pow(v.Y, e), (float)Math.Pow(v.Z, e));

    public static double ModulatedEta(double eta, double weight)
    {
        double r = (eta - 1) / (eta + 1);
        double eps = Math.Sign(eta - 1) * Math.Sqrt(Math.Clamp(weight * r * r, 0, 0.99999));
        return (1 + eps) / (1 - eps);
    }

    public static double FresnelDielectric(double c, double eta)
    {
        c = Math.Clamp(c, 0, 1);
        double g2 = eta * eta - 1 + c * c;
        if (g2 <= 0)
            return 1;
        double g = Math.Sqrt(g2);
        double a = (g - c) / (g + c);
        double b = (c * (g + c) - 1) / (c * (g - c) + 1);
        return 0.5 * a * a * (1 + b * b);
    }

    public static (double P, double S) FresnelPolarized(double cosTheta, double eta)
    {
        double cos2 = Math.Clamp(cosTheta * cosTheta, 0, 1), sin2 = 1 - cos2;
        double t0 = Math.Max(eta * eta - sin2, 0), t1 = t0 + cos2, t2 = 2 * Math.Sqrt(t0) * Math.Sqrt(cos2);
        double rs = (t1 - t2) / Math.Max(t1 + t2, 1e-9);
        double t3 = cos2 * t0 + sin2 * sin2, t4 = t2 * sin2;
        return (rs * (t3 - t4) / Math.Max(t3 + t4, 1e-9), rs);
    }

    // d'Eon's fit to the cosine weighted average of dielectric Fresnel, mirrored below 1
    public static double FresnelAverage(double eta)
    {
        double e = eta < 1 ? 1 / eta : eta;
        double average = Math.Log((10893 * e - 1438.2) / (-774.4 * e * e + 10212 * e + 1));
        return eta < 1 ? 1 - eta * eta * (1 - average) : average;
    }

    public static double FonAlbedo(double mu, double r)
    {
        double m = 1 - Math.Clamp(mu, 0, 1);
        double gOverPi = m * (0.0571085289 + m * (0.491881867 + m * (-0.332181442 + m * 0.0714429953)));
        return (1 + r * gOverPi) / (1 + FonC1 * r);
    }

    public static double FonAverage(double r) => (1 + FonC2 * r) / (1 + FonC1 * r);

    public static Vector3 MultipleAlbedo(Vector3 rho, double average)
        => rho * rho * (float)average / (Vector3.One - rho * (float)(1 - average));

    public static Vector3 EonAlbedo(Vector3 rho, double r, double mu)
    {
        double e = FonAlbedo(mu, r);
        return rho * (float)e + MultipleAlbedo(rho, FonAverage(r)) * (float)(1 - e);
    }

    public static double FuzzAlbedoFit(double x, double y)
    {
        double s = y * (0.0206607 + 1.58491 * y) / (0.0379424 + y * (1.32227 + y));
        double m = y * (-0.193854 + y * (-1.14885 + y * (1.7932 - 0.95943 * y * y))) / (0.046391 + y);
        double o = y * (0.000654023 + (-0.0207818 + 0.119681 * y) * y) / (1.26264 + y * (-1.92021 + y));
        double z = (x - m) / s;
        return Math.Clamp(Math.Exp(-0.5 * z * z) / (s * Math.Sqrt(2 * Math.PI)) + o, 0, 1);
    }

    public static (double A, double B) FuzzLtc(double x, double y)
        => ((2.58126 * x + 0.813703 * y) * y / (1 + 0.310327 * x * x + 2.60994 * x * y),
            Math.Sqrt(1 - x) * (y - 1) * y * y * y / (0.0000254053 + 1.71228 * x - 1.71506 * x * y + 1.34174 * y * y));

    private static Vector3 FilmSensitivity(double opd, Vector3 shift)
    {
        double phase = 2 * Math.PI * opd;
        double[] val = [5.4856e-13, 4.4201e-13, 5.2481e-13], pos = [1.6810e+06, 1.7953e+06, 2.2084e+06], var = [4.3278e+09, 9.3046e+09, 6.6121e+09];
        Vector3 xyz = Vector3.Zero;
        for (int c = 0; c < 3; c++)
            xyz[c] = (float)(val[c] * Math.Sqrt(2 * Math.PI * var[c]) * Math.Cos(pos[c] * phase + shift[c]) * Math.Exp(-var[c] * phase * phase));
        xyz.X += (float)(9.7470e-14 * Math.Sqrt(2 * Math.PI * 4.5282e+09) * Math.Cos(2.2399e+06 * phase + shift.X) * Math.Exp(-4.5282e+09 * phase * phase));
        return xyz / 1.0685e-7f;
    }

    private static Vector3 FilmPolarization(double r12, Vector3 r23, Vector3 phi23, double phi21, double opd)
    {
        double t121 = 1 - r12;
        Vector3 i = Vector3.Zero, rs = Vector3.Zero, cm = Vector3.Zero, r123 = Vector3.Zero;
        for (int c = 0; c < 3; c++)
        {
            rs[c] = (float)(t121 * t121 * r23[c] / Math.Max(1 - r12 * r23[c], 1e-7));
            i[c] = (float)(r12 + rs[c]);
            r123[c] = (float)Math.Sqrt(Math.Max(r12 * r23[c], 0));
            cm[c] = (float)(rs[c] - t121);
        }
        for (int m = 1; m <= 2; m++)
        {
            cm *= r123;
            i += cm * 2 * FilmSensitivity(m * opd, m * (phi23 + new Vector3((float)phi21)));
        }
        return i;
    }

    private static (double CosT, double Eta2) FilmAngle(double cosTheta, double eta1, double filmIor)
    {
        double eta2 = Math.Max(filmIor, eta1), ratio = eta1 / eta2;
        return (Math.Sqrt(Math.Max(1 - (1 - cosTheta * cosTheta) * ratio * ratio, 0)), eta2);
    }

    private static Vector3 FilmResolve(double cosTheta, double eta1, double eta2, double cosThetaT, double thickness, Vector3 r23p, Vector3 r23s, Vector3 phi23p, Vector3 phi23s)
    {
        (double r12p, double r12s) = cosThetaT <= 0 ? (1, 1) : FresnelPolarized(cosTheta, eta2 / eta1);
        double cosB = Math.Cos(Math.Atan(eta2 / eta1));
        double opd = 2 * eta2 * cosThetaT * thickness * 1e-6;
        Vector3 xyz = 0.5f * (FilmPolarization(r12p, r23p, phi23p, cosTheta < cosB ? 0 : Math.PI, opd) + FilmPolarization(r12s, r23s, phi23s, Math.PI, opd));
        Vector3 rgb = new(
            2.3706743f * xyz.X - 0.9000405f * xyz.Y - 0.4706338f * xyz.Z,
            -0.5138850f * xyz.X + 1.4253036f * xyz.Y + 0.0885814f * xyz.Z,
            0.0052982f * xyz.X - 0.0146949f * xyz.Y + 1.0093968f * xyz.Z);
        return Vector3.Clamp(rgb, Vector3.Zero, Vector3.One);
    }

    public static Vector3 ThinFilmDielectric(double cosTheta, double eta1, double filmIor, double eta3, double thickness)
    {
        (double cosT, double eta2) = FilmAngle(cosTheta, eta1, filmIor);
        (double r23p, double r23s) = FresnelPolarized(cosT, eta3 / eta2);
        double sin2 = 1 - cosT * cosT;
        double A = eta3 * eta3 - eta2 * eta2 * sin2;
        double U = Math.Sqrt(Math.Max(0.5 * (A + Math.Abs(A)), 0)), V = Math.Sqrt(Math.Max(0.5 * (Math.Abs(A) - A), 0));
        double phiS = Math.Atan2(2 * eta2 * V * cosT, U * U + V * V - eta2 * eta2 * cosT * cosT);
        double phiP = Math.Atan2(-2 * eta2 * eta3 * eta3 * cosT * V, Math.Pow(eta3, 4) * cosT * cosT - eta2 * eta2 * (U * U + V * V));
        return FilmResolve(cosTheta, eta1, eta2, cosT, thickness, new((float)r23p), new((float)r23s), new((float)phiP), new((float)phiS));
    }

    public static Vector3 ThinFilmMetal(double cosTheta, double eta1, double filmIor, Vector3 F0, Vector3 tint, double thickness)
    {
        (double cosT, double eta2) = FilmAngle(cosTheta, eta1, filmIor);
        Vector3 r23 = Vector3.Zero, phi = Vector3.Zero;
        double m = 1 - Math.Clamp(cosT, 0, 1), m5 = Math.Pow(m, 5);
        for (int c = 0; c < 3; c++)
        {
            r23[c] = (float)Math.Clamp(F0[c] + (1 - F0[c]) * m5 - EnergyTables.F82Tint(F0[c], tint[c]) * cosT * m5 * m, 0, 1);
            double s = Math.Sqrt(Math.Clamp(F0[c], 0.01, 0.99));
            phi[c] = eta1 * (1 + s) / (1 - s) < eta2 ? MathF.PI : 0;
        }
        return FilmResolve(cosTheta, eta1, eta2, cosT, thickness, r23, r23, phi, phi);
    }

    private Vector3 EonLobe(Vector3 single, Vector3 multiple, Vector3 l)
    {
        double muI = Math.Clamp(Vector3.Dot(_n, l), 0, 1);
        double s = Vector3.Dot(l, _v) - muI * _nv;
        double sOverT = s > 0 ? s / Math.Max(Math.Max(muI, _nv), 1e-6) : s;
        return single * (float)(1 + _rd * sOverT) + multiple * (float)Math.Max(1e-7, 1 - FonAlbedo(muI, _rd));
    }

    private Vector3 SheetLobe(Vector3 l) => EonLobe(new Vector3((float)_sheetSingle), new Vector3((float)_sheetMultiple), l);

    // The base diffuse BRDF toward l, the sheet's reflection included, its throughputs, the coat and fuzz above applied,
    // without the cosine
    public Vector3 Diffuse(Vector3 l)
        => (EonLobe(_diffuseSingle, _diffuseMultiple, l) + SheetReflection * SheetLobe(l)) * CoatedBaseToward(l);

    // The bulk subsurface lobe lit from l at unit facing irradiance, its wrapped cosine included, the coat and fuzz above applied
    public Vector3 Wrap(Vector3 l)
    {
        double nl = Vector3.Dot(_n, l);
        Vector3 f = Vector3.Zero;
        for (int c = 0; c < 3; c++)
            f[c] = (float)(_subsurfaceAlbedo[c] / Math.PI * Math.Max(nl + _wrap[c], 0) / ((1 + _wrap[c]) * (1 + _wrap[c])));
        return f * CoatedBaseToward(l);
    }

    // The sheet's transmission of light from l on the far side toward the view, mirrored through the sheet, through the
    // coat and fuzz on the view's side, without the cosine
    public Vector3 SheetBack(Vector3 l)
        => SheetTransmission * SheetLobe(Vector3.Reflect(l, _n)) * CoatedBase * (float)_fuzzThroughput;

    // The base specular BRDF toward l, Turquin compensated, the coat and fuzz above applied, without the cosine
    public Vector3 Specular(Vector3 l)
    {
        Vector3 h = Vector3.Normalize(l + _v);
        double lh = Math.Clamp(Vector3.Dot(l, h), 0, 1);
        double dv = Base.D(h) * Base.Visibility(_v, l);
        double m = 1 - lh, m5 = m * m * m * m * m;
        Vector3 f = _tint * ((float)(_dielectricScale * FresnelDielectric(lh, _eta)) * Vector3.One + _dielectricFilm);
        for (int c = 0; c < 3; c++)
            f[c] += (float)(_metalScale[c] * Math.Clamp(_rho[c] + (1 - _rho[c]) * m5 - _metalTint[c] * lh * m5 * m, 0, 1) + _metalFilm[c]);
        return f * (float)dv * CoatedBaseToward(l);
    }

    // The coat lobe toward l, the fuzz above applied, without the cosine
    public double CoatLobe(Vector3 l)
    {
        Vector3 h = Vector3.Normalize(l + _v);
        double lh = Math.Clamp(Vector3.Dot(l, h), 0, 1);
        return _coatScale * FresnelDielectric(lh, _coatEta) * Coat.D(h) * Coat.Visibility(_v, l) * _fuzzThroughput;
    }

    // The fuzz lobe's density toward l over solid angle, the cosine included
    public double FuzzLobe(Vector3 l)
    {
        Vector3 y = Vector3.Cross(_fuzzNormal, _fuzzAxis);
        double wx = Vector3.Dot(l, _fuzzAxis), wy = Vector3.Dot(l, y), wz = Vector3.Dot(l, _fuzzNormal);
        double ox = _fuzzA * wx + _fuzzB * wz, oy = _fuzzA * wy;
        double k = _fuzzA / (ox * ox + oy * oy + wz * wz);
        return Math.Max(wz, 0) / Math.PI * k * k;
    }

    // What a light from l of unit radiance and unit facing irradiance reflects and the sheet transmits, each lobe with its own cosine
    public Vector3 Shade(Vector3 l)
    {
        double n = Vector3.Dot(_n, l), nl = Math.Max(n, 0), cl = Math.Max(Vector3.Dot(Coat.N, l), 0);
        return (Diffuse(l) + Specular(l)) * (float)nl + Wrap(l) + SheetBack(l) * (float)Math.Max(-n, 0)
            + new Vector3((float)(CoatLobe(l) * cl)) + FuzzAlbedo * (float)FuzzLobe(l);
    }

    // The whole BRDF toward l, without the base normal's cosine
    public Vector3 Evaluate(Vector3 l)
    {
        double nl = Vector3.Dot(_n, l);
        return nl > 0 ? Shade(l) / (float)nl : Vector3.Zero;
    }
}

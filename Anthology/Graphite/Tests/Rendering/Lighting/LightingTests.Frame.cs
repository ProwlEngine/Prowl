using System;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

using Xunit;

using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace Prowl.Graphite.Rendering.Tests;

// A model with no surface of its own walks the lights with a Graphite_LightFrame, its coat lobe on a normal and alpha of
// its own and the light reaching its back, each checked against the CPU.
public sealed partial class LightingTests
{
    private const string FrameSource = """
        Shader "Test/Frame"
        {
            Properties
            {
                _CoatNormal("Coat normal", Vector) = (0, 0, -1, 0)
                _BaseAlpha("Base alpha", Float) = 0.3
                _CoatAlpha("Coat alpha", Float) = 0.1
                _Mode("Mode", Float) = 0
            }

            Pass
            {
                Tags { "LightMode" = "Forward" }
                Cull Off
                GLSLPROGRAM
                Vertex
                {
                    #include "Graphite/Vertex.glsl"
                    layout(location = 0) out vec3 vWorld;
                    void main()
                    {
                        vWorld = Graphite_ObjectToRelative(vertexPosition);
                        gl_Position = Graphite_RelativeToClip(vWorld);
                    }
                }
                Fragment
                {
                    #define GRAPHITE_LIGHT_COAT
                    #define GRAPHITE_LIGHT_BACK
                    #include "Graphite/Lighting.glsl"
                    #include "Graphite/Passes.glsl"
                    uniform vec4 _CoatNormal;
                    uniform float _BaseAlpha;
                    uniform float _CoatAlpha;
                    uniform float _Mode;
                    layout(location = 0) in vec3 vWorld;
                    layout(location = 0) out vec4 outColor;

                    // Mode 0 gives each lobe's factor times the radiance, mode 1 the coat's direction where it is lit
                    vec4 Terms(Graphite_LightContribution c)
                    {
                        if (_Mode > 0.5)
                            return c.coat > 0.0 ? vec4(c.coatL, 1.0) : vec4(0.0);
                        return vec4(c.diffuse, c.specular, c.coat, c.diffuseBack) * c.radiance.r;
                    }

                    void main()
                    {
                        Graphite_LightFrame f;
                        f.position = vWorld;
                        f.view = Graphite_ViewDir(vWorld);
                        f.geomNormal = vec3(0.0, 0.0, -1.0);
                        f.baseNormal = vec3(0.0, 0.0, -1.0);
                        f.coatNormal = normalize(_CoatNormal.xyz);
                        f.baseAlpha = _BaseAlpha;
                        f.coatAlpha = _CoatAlpha;

                        vec4 total = vec4(0.0);
                        Graphite_LightContribution c;
                        for (uint i = 0u; i < Graphite_DirectionalCount(); i++)
                        {
                            if (Graphite_ContributeDirectional(Graphite_GetDirectional(i), i, f, c))
                                total += Terms(c);
                        }
                        Graphite_LightIterator it = Graphite_BeginLights(vWorld);
                        LightSample L;
                        while (Graphite_NextLight(it, L))
                        {
                            if (Graphite_ContributeLocal(L, f, false, c))
                                total += Terms(c);
                        }
                        outColor = total;
                        Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), vec3(0.0), 1.0);
                    }
                }
                ENDGLSL
            }
        }
        """;

    private static readonly Vector3 s_coatNormal = Vector3.Normalize(new Vector3(0.3f, -0.2f, -1));
    private const float BaseAlpha = 0.3f, CoatAlpha = 0.08f;

    private Material FrameModel(float mode)
    {
        Material material = _renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(FrameSource)));
        material.SetFloat4("_CoatNormal", new Float4(s_coatNormal.X, s_coatNormal.Y, s_coatNormal.Z, 0));
        material.SetFloat("_BaseAlpha", BaseAlpha);
        material.SetFloat("_CoatAlpha", CoatAlpha);
        material.SetFloat("_Mode", mode);
        return material;
    }

    // Each lobe's factor per unit radiance: base diffuse and specular, coat specular, and diffuse on the flipped normal
    private static FrameLobes FrameTerms(in LightDesc light, Vector3 p)
    {
        var baseLobe = Reference.Lobe(light, p, s_normal, BaseAlpha);
        var coat = Reference.Lobe(light, p, s_coatNormal, CoatAlpha);
        float back = Reference.Lobe(light, p, -s_normal, BaseAlpha).Diffuse;
        float radiance = light.Intensity * light.Color.X;
        return new(baseLobe.Diffuse * radiance, baseLobe.Specular * radiance, coat.Specular * radiance, back * radiance, coat.Ls, coat.Specular);
    }

    private readonly record struct FrameLobes(float Diffuse, float Specular, float Coat, float Back, Vector3 CoatL, float CoatFactor);

    private void AssertFrameLights(LightDesc light, bool behind)
    {
        AddQuad(FrameModel(0));
        _world.AddLight(light);
        Float4[] pixels = Frame();
        LightDesc seen = Relative(light);
        float most = 0, mostBase = 0, mostBack = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            FrameLobes want = FrameTerms(seen, PixelPosition(i % Size, i / Size));
            Float4 got = pixels[i];
            (float W, float G, string Name)[] channels = [(want.Diffuse, got.X, "diffuse"), (want.Specular, got.Y, "specular"), (want.Coat, got.Z, "coat"), (want.Back, got.W, "back")];
            foreach ((float w, float g, string name) in channels)
                Assert.True(MathF.Abs(w - g) <= 2e-4f + 0.01f * MathF.Abs(w), $"pixel {i} {name}: expected {w}, got {g}");
            most = MathF.Max(most, MathF.Max(got.X, got.Z));
            mostBase = MathF.Max(mostBase, MathF.Max(got.X, got.Y));
            mostBack = MathF.Max(mostBack, got.W);
        }
        Assert.True(behind ? mostBack > 0.01f : most > 0.01f, $"lit {most}, back {mostBack}");
        // The tilted coat may still catch a grazing light from behind, the base never
        if (behind)
            Assert.True(mostBase == 0, $"a light behind lit the base by {mostBase}");
    }

    private LightDesc FrameLight(LightType type, float sourceRadius, Float2 size, bool behind) => new()
    {
        Type = type,
        Position = _origin + new Double3(0.6, -0.4, behind ? 11.5 : 8.5),
        Direction = new Float3(0.2f, 0.1f, behind ? -1 : 1),
        Up = new Float3(0, 1, 0),
        Range = 9,
        Intensity = 0.64f,
        SourceRadius = sourceRadius,
        Size = size,
        InnerAngle = 25,
        OuterAngle = 40,
        Static = true,
    };

    [Theory]
    [InlineData(LightType.Point, 0f, 0f, 0f, false)]
    [InlineData(LightType.Point, 0f, 0f, 0f, true)]
    [InlineData(LightType.Point, 0.6f, 0f, 0f, false)]
    [InlineData(LightType.Point, 0.6f, 0f, 0f, true)]
    [InlineData(LightType.Spot, 0.4f, 0f, 0f, false)]
    [InlineData(LightType.Spot, 0.4f, 0f, 0f, true)]
    [InlineData(LightType.Tube, 0.15f, 2.5f, 0f, false)]
    [InlineData(LightType.Tube, 0.15f, 2.5f, 0f, true)]
    [InlineData(LightType.Rect, 0f, 2f, 1f, false)]
    [InlineData(LightType.Rect, 0f, 2f, 1f, true)]
    [InlineData(LightType.Disk, 0f, 0.8f, 0f, false)]
    [InlineData(LightType.Disk, 0f, 0.8f, 0f, true)]
    public void TheLightFrame_LightsEachLobeAndTheBack_FromEveryLocalLight(LightType type, float sourceRadius, float sizeX, float sizeY, bool behind)
        => AssertFrameLights(FrameLight(type, sourceRadius, new Float2(sizeX, sizeY), behind), behind);

    [Theory]
    [InlineData(0f, false)]
    [InlineData(3f, false)]
    [InlineData(3f, true)]
    public void TheLightFrame_LightsEachLobeAndTheBack_FromDirectionalLights(float angularRadius, bool behind)
        => AssertFrameLights(new LightDesc { Type = LightType.Directional, Direction = new Float3(0.3f, -0.2f, behind ? -1 : 1), Intensity = 0.05f, AngularRadius = angularRadius }, behind);

    [Theory]
    [InlineData(LightType.Point, 0.6f, 0f, 0f)]
    [InlineData(LightType.Tube, 0.15f, 2.5f, 0f)]
    [InlineData(LightType.Rect, 0f, 2f, 1f)]
    [InlineData(LightType.Disk, 0f, 0.8f, 0f)]
    [InlineData(LightType.Directional, 0f, 0f, 0f)]
    public void TheCoat_TakesItsOwnRepresentativePoint(LightType type, float sourceRadius, float sizeX, float sizeY)
    {
        AddQuad(FrameModel(1));
        LightDesc light = type == LightType.Directional
            ? new LightDesc { Type = LightType.Directional, Direction = new Float3(0.3f, -0.2f, 1), Intensity = 0.05f, AngularRadius = 3 }
            : FrameLight(type, sourceRadius, new Float2(sizeX, sizeY), false);
        _world.AddLight(light);
        Float4[] pixels = Frame();
        LightDesc seen = Relative(light);
        int checkedPixels = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            FrameLobes want = FrameTerms(seen, PixelPosition(i % Size, i / Size));
            Vector3 baseL = Reference.Lobe(seen, PixelPosition(i % Size, i / Size), s_normal, BaseAlpha).Ls;
            if (want.CoatFactor < 1e-3f || Vector3.Distance(want.CoatL, baseL) < 1e-2f)
                continue;
            Float4 got = pixels[i];
            Assert.Equal(1f, got.W);
            Assert.True(Vector3.Distance(want.CoatL, new Vector3(got.X, got.Y, got.Z)) < 2e-3f, $"pixel {i}: expected {want.CoatL}, got {got}");
            checkedPixels++;
        }
        Assert.True(checkedPixels > 0, "the coat never took a point apart from the base's");
    }

    private static partial class Reference
    {
        // What a light gives a lobe of normal n and GGX alpha at p per unit radiance, as Graphite_ContributeLocal and
        // Graphite_ContributeDirectional give it: diffuse irradiance, the lobe's representative direction and specular factor
        public static (float Diffuse, Vector3 Ls, float Specular) Lobe(in LightDesc light, Vector3 p, Vector3 n, float alpha)
        {
            Vector3 v = Vector3.Normalize(-p);
            Vector3 r = Vector3.Reflect(-v, n);
            Vector3 dir = Vector3.Normalize(V(light.Direction));
            if (light.Type == LightType.Directional)
            {
                Vector3 l = -dir;
                float nl = MathF.Max(Vector3.Dot(n, l), 0);
                if (light.AngularRadius <= 0)
                    return (nl, l, nl);
                float angle = light.AngularRadius * MathF.PI / 180;
                float sin = MathF.Sin(angle), cos = MathF.Cos(angle);
                float rl = Vector3.Dot(r, l);
                Vector3 across = r - l * rl;
                Vector3 disk = rl >= cos ? r : across.LengthSquared() > 1e-8f ? Vector3.Normalize(l * cos + Vector3.Normalize(across) * sin) : l;
                return (nl, disk, MathF.Max(Vector3.Dot(n, disk), 0) * AreaEnergy(alpha, 0.5f * sin, 0.5f * sin));
            }

            Vector3 toLight = new Vector3((float)light.Position.X, (float)light.Position.Y, (float)light.Position.Z) - p;
            float dist2 = toLight.LengthSquared();
            if (dist2 > light.Range * light.Range)
                return (0, Vector3.Zero, 0);
            float dist = MathF.Sqrt(dist2);
            Vector3 lc = toLight / MathF.Max(dist, 1e-6f);
            float window = Window(dist2, light.Range);
            Vector3 up = Vector3.Normalize(V(light.Up) - dir * Vector3.Dot(V(light.Up), dir));
            Vector3 ls;

            if (light.Type is LightType.Point or LightType.Spot)
            {
                float cone = 1;
                if (light.Type == LightType.Spot)
                {
                    float outer = MathF.Cos(light.OuterAngle * MathF.PI / 180), inner = MathF.Max(MathF.Cos(light.InnerAngle * MathF.PI / 180), outer + 1e-4f);
                    float axisCos = Vector3.Dot(dir, -lc);
                    if (axisCos <= outer)
                        return (0, Vector3.Zero, 0);
                    float t = Math.Clamp((axisCos - outer) / (inner - outer), 0, 1);
                    cone = t * t * (3 - 2 * t);
                }
                float rad = light.SourceRadius;
                float attenuation = window / MathF.Max(dist2, 0.01f) * cone;
                if (rad <= 1e-4f)
                {
                    float nl = MathF.Max(Vector3.Dot(n, lc), 0) * attenuation;
                    return (nl, lc, nl);
                }
                float diffuse = SphereIrradiance(Vector3.Dot(n, lc), MathF.Min(rad * rad / dist2, 1)) / (MathF.PI * rad * rad) * window * cone;
                ls = Vector3.Normalize(SpherePoint(toLight, rad, r));
                return (diffuse, ls, MathF.Max(Vector3.Dot(n, ls), 0) * attenuation * AreaEnergy(alpha, rad / (2 * dist), rad / (2 * dist)));
            }

            if (light.Type == LightType.Tube)
            {
                float radius = light.SourceRadius, length = light.Size.X;
                Vector3 axis = dir * (0.5f * length);
                Vector3 a = toLight - axis, b = toLight + axis, ab = b - a;
                float caps = SphereIrradiance(Vector3.Dot(n, lc), MathF.Min(radius * radius / dist2, 1));
                float diffuse = (TubeIrradiance(a, b, radius, n) + caps) * window;
                float rab = Vector3.Dot(r, ab);
                float tr = Math.Clamp((Vector3.Dot(r, a) * rab - Vector3.Dot(a, ab)) / MathF.Max(ab.LengthSquared() - rab * rab, 1e-8f), 0, 1);
                Vector3 onLine = a + ab * tr;
                ls = Vector3.Normalize(SpherePoint(onLine, radius, r));
                float lineDist = MathF.Max(onLine.Length(), 1e-4f);
                float along = Vector3.Dot(onLine, dir) / lineDist;
                float projected = 2 * radius * length * MathF.Sqrt(MathF.Max(1 - along * along, 0)) + MathF.PI * radius * radius;
                return (diffuse, ls, MathF.Max(Vector3.Dot(n, ls), 0) * projected * window / MathF.Max(onLine.LengthSquared(), 0.01f)
                    * AreaEnergy(alpha, radius / (2 * lineDist), length / (MathF.PI * lineDist)));
            }

            float front = Vector3.Dot(-lc, dir);
            if (front <= 0)
                return (0, Vector3.Zero, 0);
            Vector3 right = Vector3.Cross(up, dir);
            bool isDisk = light.Type == LightType.Disk;
            Vector2 half = isDisk ? new Vector2(light.Size.X) : 0.5f * new Vector2(light.Size.X, light.Size.Y);
            float area = isDisk ? MathF.PI * half.X * half.X : light.Size.X * light.Size.Y;
            float irradiance;
            if (isDisk)
            {
                float r2 = half.X * half.X;
                irradiance = SphereIrradiance(Vector3.Dot(n, lc), r2 / (r2 + dist2)) * front * window;
            }
            else
            {
                Vector3 hx = right * half.X, hy = up * half.Y;
                irradiance = QuadIrradiance([toLight - hx - hy, toLight + hx - hy, toLight + hx + hy, toLight - hx + hy], n) * window;
            }
            Vector3 q = PlanePoint(toLight, dir, right, up, half, isDisk, r);
            float qDist2 = MathF.Max(q.LengthSquared(), 1e-8f), qDist = MathF.Sqrt(qDist2);
            ls = q / qDist;
            float qFront = MathF.Max(Vector3.Dot(-ls, dir), 0);
            Vector2 extent = isDisk ? new Vector2(half.X / (2 * qDist)) : new Vector2(light.Size.X, light.Size.Y) / (2 * MathF.Sqrt(MathF.PI) * qDist);
            return (irradiance, ls, MathF.Max(Vector3.Dot(n, ls), 0) * area * qFront * window / MathF.Max(qDist2, 0.01f) * AreaEnergy(alpha, extent.X, extent.Y));
        }
    }
}

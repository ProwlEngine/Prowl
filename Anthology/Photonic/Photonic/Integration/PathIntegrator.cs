using Prowl.Vector;
using Prowl.Photonic.Raytracing;
using Prowl.Photonic.Sampling;
using Prowl.Photonic.Scene.Lights;

namespace Prowl.Photonic.Integration;

/// <summary>
/// CPU path tracer. Given a surface point with a normal and a material, returns the radiance
/// arriving on that point from all lights (direct) plus indirect diffuse bounces.
/// </summary>
/// <remarks>
/// Direct: next-event estimation. For every light, sample one shadow ray; multiply by the
/// material's diffuse albedo x dot(n, l).
///
/// Indirect: a small number of cosine-weighted bounces. With cosine sampling, the PDF is
/// <c>cos / π</c> and the integrand <c>brdf * cos / pdf = albedo</c>. So each bounce just
/// multiplies the carried throughput by the surface's diffuse albedo. Cheap and converges fast
/// on diffuse scenes.
///
/// Material lookups are precomputed: callers pass <c>resolvedMats[blasIndex][groupIndex]</c> so
/// the inner loop avoids the per-bounce <c>FindMaterial</c> dictionary hit.
/// </remarks>
internal sealed class PathIntegrator
{
    private readonly BakeScene _scene;
    private readonly Blas _blas;            // single merged world-space BLAS
    private readonly BakeMaterial?[] _mats; // resolved material per merged group
    private readonly BakeOptions _options;
    private readonly bool _cullEnabled;

    // Prowl renders with WindingOrder.CW (front = clockwise) and culls back faces. With cull=Back the
    // Blas keep-positive-determinant flag reduces to the front-face winding, which for CW front is
    // false. Backface culling is hardcoded to this convention so the bake matches Prowl's rasterizer.
    private const bool ProwlKeepPositiveDet = false;

    public PathIntegrator(BakeScene scene, Blas blas, BakeMaterial?[] mats, BakeOptions options)
    {
        _scene = scene; _blas = blas; _mats = mats; _options = options;
        _cullEnabled = options.DoBackfaceCull;
    }

    /// <summary>
    /// Direct + indirect irradiance at a surface point. The lightmap stores <i>incoming</i> light;
    /// the runtime shader multiplies by the surface's own diffuse texture at draw time. The
    /// texel's own <paramref name="albedo"/> parameter is therefore <b>not</b> applied here: only
    /// hit-surface albedos (one per bounce) are applied inside <see cref="TracePath"/>. This avoids
    /// double-modulation (<c>albedo^2 x light</c> at runtime) and keeps the lightmap free of the
    /// texel's diffuse-texture noise.
    /// </summary>
    /// <remarks>
    /// <para><b>Direct</b>: <see cref="SampleAllLights"/> is deterministic with the current set of
    /// light types (point / directional / spot: no area lights yet, so no soft shadows), so the
    /// answer is the same regardless of <paramref name="directSamples"/>.</para>
    /// <para><b>Indirect</b>: cosine-weighted hemisphere sampling via the precomputed
    /// <see cref="CosineHemisphereLUT"/>; throughput starts at <c>1</c> and picks up hit albedos
    /// only at bounce points.</para>
    /// </remarks>
    public Float3 Integrate(Float3 position, Float3 normal, Float3 albedo, Float3 emissive,
                            int directSamples, int indirectSamples, int bounces, ref Sampler rng)
    {
        _ = directSamples;
        _ = albedo; // not applied at the texel level; the runtime shader does that.
        Float3 result = emissive;

        // Direct lighting at the texel itself: gated by IncludeDirectLighting. When disabled we
        // still propagate direct light through bounces (NEE in TracePath), so the result is
        // indirect-only at the texel but full at bounce hits.
        if (_scene.Lights.Count > 0 && _options.IncludeDirectLighting)
        {
            result += SampleAllLights(position, normal, bakedDirectOnly: true);
        }

        if (bounces > 0 && indirectSamples > 0)
        {
            Float3 indirectSum = Float3.Zero;
            for (int s = 0; s < indirectSamples; s++)
                indirectSum += TracePath(position, normal, Float3.One, bounces, ref rng);
            result += indirectSum * (1f / indirectSamples);
        }

        return result;
    }

    private Float3 TracePath(Float3 position, Float3 normal, Float3 throughput, int bouncesLeft, ref Sampler rng)
    {
        Float3 radiance = Float3.Zero;
        Float3 ro = position;
        Float3 n = normal;
        Float3 t = throughput;

        const float ThroughputEpsilon = 1e-4f;
        bool useLUT = _options.UseHemisphereLUT;
        for (int bounce = 0; bounce < bouncesLeft; bounce++)
        {
            Float3 rd;
            if (useLUT)
            {
                var tsd = CosineHemisphereLUT.Get(rng.NextU32());
                Hemisphere.BuildOrthonormalBasis(n, out var T, out var B);
                rd = Float3.Normalize(T * tsd.X + B * tsd.Y + n * tsd.Z);
            }
            else
            {
                float u1 = rng.NextFloat(), u2 = rng.NextFloat();
                rd = Hemisphere.SampleCosine(n, u1, u2);
            }
            var hit = ClosestHitWorld(ro + n * _options.RayBias, rd, _options.MaxRayDistance);
            if (!hit.Hit)
            {
                radiance += t * SampleEnvironment(rd);
                break;
            }

            ResolveHitSurface(hit, out Float3 hitAlbedo, out Float3 hitEmissive);

            if (_scene.Lights.Count > 0)
                radiance += t * hitAlbedo * SampleAllLights(hit.Position, hit.Normal);
            radiance += t * hitEmissive;

            t = t * hitAlbedo;

            // throughput cutoff: stop when continuing can't materially add more energy
            if (t.X + t.Y + t.Z < ThroughputEpsilon) break;

            // optional russian roulette
            if (_options.RussianRoulette > 0 && bounce >= 1)
            {
                float p = System.Math.Max(t.X, System.Math.Max(t.Y, t.Z));
                p = System.Math.Min(1f, System.Math.Max(_options.RussianRoulette, p));
                if (rng.NextFloat() > p) break;
                t = t * (1f / p);
            }

            ro = hit.Position;
            n = hit.Normal;
        }
        return radiance;
    }

    /// <summary>
    /// Resolve the diffuse albedo and emissive of a ray hit (material colour x diffuse texel, or
    /// white when <see cref="BakeOptions.IgnoreAlbedo"/> is set). Reads straight off the merged mesh.
    /// </summary>
    private void ResolveHitSurface(HitInfo hit, out Float3 albedo, out Float3 emissive)
    {
        var triRef = _blas.Triangles[hit.TriangleIndex];
        var mat = _mats[triRef.MaterialGroupIndex];
        emissive = mat is not null ? mat.Emissive : Float3.Zero;
        if (_options.IgnoreAlbedo)
        {
            albedo = Float3.One;
            return;
        }
        albedo = mat is not null ? mat.DiffuseColor : new Float3(0.7f);
        if (mat is not null && mat.DiffuseTexture is not null
            && _blas.Mesh.UVLayers.TryGetValue(mat.DiffuseUVLayer, out var uvLayer))
        {
            var uvA = uvLayer[triRef.I0]; var uvB = uvLayer[triRef.I1]; var uvC = uvLayer[triRef.I2];
            float w = 1f - (float)hit.U - (float)hit.V;
            Float2 uv = uvA * w + uvB * (float)hit.U + uvC * (float)hit.V;
            // Nearest sample on the bounce path: variance averages out across indirect samples.
            albedo = albedo * mat.DiffuseTexture.SampleNearestRGB(uv.X, uv.Y);
        }
    }

    /// <summary>
    /// Deterministic direct lighting at a surface point: one shadow ray per light, no albedo
    /// applied. Caller multiplies by their own albedo. Used by continuous mode to cache the
    /// direct term once per texel.
    /// </summary>
    public Float3 ComputeDirectLighting(Float3 position, Float3 normal)
        => SampleAllLights(position, normal);

    /// <summary>
    /// Direct lighting from baked-direct lights only (<see cref="Scene.Lights.Light.BakeDirect"/>).
    /// This is what the lightmap stores at the texel; "mixed" lights are excluded here and applied
    /// in realtime at runtime. Bounced/indirect energy from all lights is still baked elsewhere.
    /// </summary>
    public Float3 ComputeBakedDirectLighting(Float3 position, Float3 normal)
        => SampleAllLights(position, normal, bakedDirectOnly: true);

    /// <summary>Environment radiance for a ray that hit nothing: the optional HDR environment
    /// callback if set, otherwise the flat <see cref="BakeOptions.SkyColor"/>.</summary>
    private Float3 SampleEnvironment(Float3 dir)
        => _options.Environment is not null ? _options.Environment(dir) : _options.SkyColor;

    /// <summary>
    /// Project the radiance arriving at a probe <paramref name="position"/> from every direction
    /// into 9-coefficient RGB spherical harmonics. <paramref name="samples"/> uniform-sphere rays
    /// are traced; each ray's radiance is the hit surface's outgoing radiance (emissive +
    /// albedo·(baked-direct + indirect bounces)), matching the lightmap's shading convention, or the
    /// environment on a miss. Used by <see cref="LightmapBaker.BakeProbes"/> to light dynamic objects.
    /// </summary>
    public Sh9Rgb IntegrateProbe(Float3 position, int samples, int bounces, ref Sampler rng)
    {
        Sh9Rgb sh = default;
        if (samples <= 0) return sh;

        const float FourPi = 4f * (float)System.Math.PI;
        for (int s = 0; s < samples; s++)
        {
            // Uniform direction over the full sphere (pdf = 1/4π).
            float u1 = rng.NextFloat(), u2 = rng.NextFloat();
            float z = 1f - 2f * u1;
            float r = (float)System.Math.Sqrt(System.Math.Max(0f, 1f - z * z));
            float phi = 2f * (float)System.Math.PI * u2;
            Float3 dir = new Float3(r * (float)System.Math.Cos(phi), r * (float)System.Math.Sin(phi), z);

            Float3 li = ProbeIncomingRadiance(position, dir, bounces, ref rng);
            sh.Accumulate(dir, li, FourPi);
        }
        return sh.Scaled(1f / samples);
    }

    /// <summary>Radiance arriving at a probe along <paramref name="dir"/>: environment on a miss,
    /// otherwise the hit surface's outgoing radiance under the lightmap shading convention.</summary>
    private Float3 ProbeIncomingRadiance(Float3 origin, Float3 dir, int bounces, ref Sampler rng)
    {
        var hit = ClosestHitWorld(origin, dir, _options.MaxRayDistance);
        if (!hit.Hit) return SampleEnvironment(dir);

        ResolveHitSurface(hit, out Float3 albedo, out Float3 emissive);
        Float3 lo = emissive;
        if (_scene.Lights.Count > 0)
            lo += albedo * SampleAllLights(hit.Position, hit.Normal, bakedDirectOnly: true);
        if (bounces > 0)
            lo += albedo * TracePath(hit.Position, hit.Normal, Float3.One, bounces, ref rng);
        return lo;
    }

    /// <summary>
    /// Indirect contribution at a surface point: <b>sum</b> of N path traces, each cosine-sampled
    /// and bounced up to <paramref name="bounces"/> times. The caller divides by the running total
    /// sample count when reading the average.
    ///
    /// The throughput starts at <c>(1, 1, 1)</c>: the texel's own albedo is intentionally NOT
    /// folded in here, since the lightmap stores irradiance and the runtime shader applies the
    /// surface's diffuse texture at draw time. <paramref name="albedo"/> is accepted for API
    /// compatibility but ignored.
    /// </summary>
    public Float3 IntegrateIndirect(Float3 position, Float3 normal, Float3 albedo,
                                    int samples, int bounces, ref Sampler rng)
    {
        _ = albedo;
        if (samples <= 0 || bounces <= 0) return Float3.Zero;
        Float3 sum = Float3.Zero;
        for (int s = 0; s < samples; s++)
            sum += TracePath(position, normal, Float3.One, bounces, ref rng);
        return sum;
    }

    /// <summary>
    /// Record the shadow rays the integrator would cast at <paramref name="position"/> for each
    /// scene light. Used by the demo's ray-visualisation tool. Doesn't accumulate radiance.
    /// </summary>
    public void RecordDirectShadowRays(Float3 position, Float3 normal, System.Collections.Generic.List<DebugSegment> segs)
    {
        for (int i = 0; i < _scene.Lights.Count; i++)
        {
            var l = _scene.Lights[i];
            if (!l.Sample(position, out var toLight, out var Li, out float maxDist, _scene.DefaultAttenuation)) continue;
            if (Li.X + Li.Y + Li.Z <= 0) continue;
            float ndotl = Float3.Dot(normal, toLight);
            if (ndotl <= 0) continue;

            var ro = position + normal * _options.RayBias;
            bool blocked = false;
            if (l.CastsShadows)
                blocked = _blas.AnyHit(ro, toLight, _options.RayBias, maxDist - 2 * _options.RayBias, _cullEnabled, ProwlKeepPositiveDet);

            float drawLen = float.IsPositiveInfinity(maxDist) ? 50f : maxDist;
            segs.Add(new DebugSegment
            {
                Start = ro,
                End = ro + toLight * drawLen,
                BounceIndex = 0,
                IsShadow = true,
                Hit = blocked,
            });
        }
    }

    /// <summary>
    /// Trace one path identical to <see cref="TracePath"/>, but append each bounce segment (and
    /// each per-bounce shadow ray) to <paramref name="segs"/>. The radiance is computed and returned
    /// but the caller usually discards it: this method is for visualisation only.
    /// </summary>
    public Float3 TracePathRecorded(Float3 position, Float3 normal, Float3 albedo, int bouncesLeft,
                                     ref Sampler rng, System.Collections.Generic.List<DebugSegment> segs)
    {
        Float3 radiance = Float3.Zero;
        Float3 ro = position;
        Float3 n = normal;
        Float3 t = albedo;
        bool useLUT = _options.UseHemisphereLUT;

        for (int bounce = 0; bounce < bouncesLeft; bounce++)
        {
            Float3 rd;
            if (useLUT)
            {
                var tsd = CosineHemisphereLUT.Get(rng.NextU32());
                Hemisphere.BuildOrthonormalBasis(n, out var T, out var B);
                rd = Float3.Normalize(T * tsd.X + B * tsd.Y + n * tsd.Z);
            }
            else
            {
                float u1 = rng.NextFloat(), u2 = rng.NextFloat();
                rd = Hemisphere.SampleCosine(n, u1, u2);
            }

            var rayOrigin = ro + n * _options.RayBias;
            var hit = ClosestHitWorld(rayOrigin, rd, _options.MaxRayDistance);

            // Record this bounce segment regardless of hit.
            segs.Add(new DebugSegment
            {
                Start = rayOrigin,
                End = hit.Hit ? hit.Position : rayOrigin + rd * System.Math.Min(50f, _options.MaxRayDistance),
                BounceIndex = bounce,
                IsShadow = false,
                Hit = hit.Hit,
            });

            if (!hit.Hit) { radiance += t * SampleEnvironment(rd); break; }

            var triRef = _blas.Triangles[hit.TriangleIndex];
            var mat = _mats[triRef.MaterialGroupIndex];
            Float3 hitAlbedo;
            if (_options.IgnoreAlbedo)
            {
                hitAlbedo = Float3.One;
            }
            else
            {
                hitAlbedo = mat is not null ? mat.DiffuseColor : new Float3(0.7f);
                if (mat is not null && mat.DiffuseTexture is not null
                    && _blas.Mesh.UVLayers.TryGetValue(mat.DiffuseUVLayer, out var uvLayer))
                {
                    var uvA = uvLayer[triRef.I0]; var uvB = uvLayer[triRef.I1]; var uvC = uvLayer[triRef.I2];
                    float w = 1f - hit.U - hit.V;
                    Float2 uv = uvA * w + uvB * hit.U + uvC * hit.V;
                    hitAlbedo = hitAlbedo * mat.DiffuseTexture.SampleNearestRGB(uv.X, uv.Y);
                }
            }

            // Record the shadow rays at this bounce hit too: those are the NEE samples.
            for (int i = 0; i < _scene.Lights.Count; i++)
            {
                var l = _scene.Lights[i];
                if (!l.Sample(hit.Position, out var toLight, out var Li, out float maxDist, _scene.DefaultAttenuation)) continue;
                if (Li.X + Li.Y + Li.Z <= 0) continue;
                float ndotl = Float3.Dot(hit.Normal, toLight);
                if (ndotl <= 0) continue;
                var shadowOrigin = hit.Position + hit.Normal * _options.RayBias;
                bool blocked = false;
                if (l.CastsShadows) blocked = _blas.AnyHit(shadowOrigin, toLight, _options.RayBias, maxDist - 2 * _options.RayBias, _cullEnabled, ProwlKeepPositiveDet);
                float drawLen = float.IsPositiveInfinity(maxDist) ? 50f : maxDist;
                segs.Add(new DebugSegment
                {
                    Start = shadowOrigin,
                    End = shadowOrigin + toLight * drawLen,
                    BounceIndex = bounce + 1,
                    IsShadow = true,
                    Hit = blocked,
                });
            }

            t = t * hitAlbedo;
            if (t.X + t.Y + t.Z < 1e-4f) break;
            ro = hit.Position;
            n = hit.Normal;
        }
        return radiance;
    }

    /// <summary>
    /// Direct lighting at a surface point via next-event estimation. When
    /// <paramref name="bakedDirectOnly"/> is true, only lights with <see cref="Scene.Lights.Light.BakeDirect"/>
    /// contribute — used for the lightmap texel itself, so "mixed" lights (BakeDirect=false) leave
    /// their direct term to the runtime shader. Bounce hits call this with <c>false</c> so every
    /// light's bounced (indirect) energy is still baked.
    /// </summary>
    private Float3 SampleAllLights(Float3 position, Float3 normal, bool bakedDirectOnly = false)
    {
        Float3 sum = Float3.Zero;
        for (int i = 0; i < _scene.Lights.Count; i++)
        {
            var l = _scene.Lights[i];
            if (bakedDirectOnly && !l.BakeDirect) continue;
            if (!l.Sample(position, out var toLight, out var Li, out float maxDist, _scene.DefaultAttenuation)) continue;
            if (Li.X + Li.Y + Li.Z <= 0) continue;
            float ndotl = Float3.Dot(normal, toLight);
            if (ndotl <= 0) continue;

            if (l.CastsShadows)
            {
                var ro = position + normal * _options.RayBias;
                if (_blas.AnyHit(ro, toLight, _options.RayBias, maxDist - 2 * _options.RayBias, _cullEnabled, ProwlKeepPositiveDet))
                    continue;
            }
            sum += Li * ndotl;
        }
        return sum;
    }

    public HitInfo ClosestHitWorld(Float3 ro, Float3 rd, float maxT)
    {
        var hit = new HitInfo { Distance = maxT };
        if (_blas.ClosestHit(ro, rd, _options.RayBias, maxT, out float tt, out float uu, out float vv, out int triIdx, _cullEnabled, ProwlKeepPositiveDet))
        {
            hit.Hit = true;
            hit.Distance = tt;
            hit.U = uu; hit.V = vv;
            hit.TriangleIndex = triIdx;

            // The merged BLAS already holds world-space positions + normals.
            var tri = _blas.Triangles[triIdx];
            var positions = _blas.Mesh.Positions;
            var normals = _blas.Mesh.Normals;
            float w = 1f - uu - vv;
            hit.Position = positions[tri.I0] * w + positions[tri.I1] * uu + positions[tri.I2] * vv;
            hit.Normal = Float3.Normalize(normals[tri.I0] * w + normals[tri.I1] * uu + normals[tri.I2] * vv);
        }
        return hit;
    }
}

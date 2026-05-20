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
    private readonly Tlas _tlas;
    private readonly BakeScene _scene;
    private readonly BakeInstance[] _instances;
    private readonly Blas[] _blas;
    private readonly int[] _instanceToBlas;
    private readonly BakeMaterial?[][] _resolvedMats;
    private readonly BakeOptions _options;

    public PathIntegrator(Tlas tlas, BakeScene scene, BakeInstance[] instances, Blas[] blas, int[] instanceToBlas,
                          BakeMaterial?[][] resolvedMats, BakeOptions options)
    {
        _tlas = tlas; _scene = scene; _instances = instances; _blas = blas;
        _instanceToBlas = instanceToBlas; _resolvedMats = resolvedMats; _options = options;
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
                            int directSamples, int indirectSamples, int bounces, ref Sampler rng,
                            float jitterWorldRadius = 0f)
    {
        _ = directSamples;
        _ = albedo; // not applied at the texel level; the runtime shader does that.
        Float3 result = emissive;

        // Direct lighting at the texel itself: gated by IncludeDirectLighting. When disabled we
        // still propagate direct light through bounces (NEE in TracePath), so the result is
        // indirect-only at the texel but full at bounce hits.
        if (_scene.Lights.Count > 0 && _options.IncludeDirectLighting)
        {
            result += SampleAllLights(position, normal);
        }

        if (bounces > 0 && indirectSamples > 0)
        {
            Float3 indirectSum = Float3.Zero;
            float jitterRadius = (_options.JitterRayOrigin && jitterWorldRadius > 0)
                ? jitterWorldRadius * _options.JitterStrength : 0f;
            Float3 T = default, B = default;
            if (jitterRadius > 0) Hemisphere.BuildOrthonormalBasis(normal, out T, out B);
            for (int s = 0; s < indirectSamples; s++)
            {
                Float3 origin = position;
                if (jitterRadius > 0)
                {
                    float ju = rng.NextFloat() - 0.5f;
                    float jv = rng.NextFloat() - 0.5f;
                    origin = position + T * (ju * jitterRadius) + B * (jv * jitterRadius);
                }
                indirectSum += TracePath(origin, normal, Float3.One, bounces, ref rng);
            }
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
                radiance += t * _options.SkyColor;
                break;
            }

            int blasIdx = _instanceToBlas[hit.InstanceIndex];
            var blas = _blas[blasIdx];
            var triRef = blas.Triangles[hit.TriangleIndex];
            var mat = _resolvedMats[blasIdx][triRef.MaterialGroupIndex];
            Float3 hitAlbedo;
            Float3 hitEmissive = mat is not null ? mat.Emissive : Float3.Zero;
            if (_options.IgnoreAlbedo)
            {
                hitAlbedo = Float3.One;
            }
            else
            {
                hitAlbedo = mat is not null ? mat.DiffuseColor : new Float3(0.7f);
                if (mat is not null && mat.DiffuseTexture is not null
                    && _instances[hit.InstanceIndex].Mesh.UVLayers.TryGetValue(mat.DiffuseUVLayer, out var uvLayer))
                {
                    var uvA = uvLayer[triRef.I0]; var uvB = uvLayer[triRef.I1]; var uvC = uvLayer[triRef.I2];
                    float w = 1f - hit.U - hit.V;
                    Float2 uv = uvA * w + uvB * hit.U + uvC * hit.V;
                    // Nearest sample on the bounce path: variance averages out across indirect samples.
                    hitAlbedo = hitAlbedo * mat.DiffuseTexture.SampleNearestRGB(uv.X, uv.Y);
                }
            }

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
    /// Deterministic direct lighting at a surface point: one shadow ray per light, no albedo
    /// applied. Caller multiplies by their own albedo. Used by continuous mode to cache the
    /// direct term once per texel.
    /// </summary>
    public Float3 ComputeDirectLighting(Float3 position, Float3 normal)
        => SampleAllLights(position, normal);

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
                                    int samples, int bounces, ref Sampler rng,
                                    float jitterWorldRadius = 0f)
    {
        _ = albedo;
        if (samples <= 0 || bounces <= 0) return Float3.Zero;
        Float3 sum = Float3.Zero;
        float jitterRadius = (_options.JitterRayOrigin && jitterWorldRadius > 0)
            ? jitterWorldRadius * _options.JitterStrength : 0f;
        Float3 T = default, B = default;
        if (jitterRadius > 0) Hemisphere.BuildOrthonormalBasis(normal, out T, out B);
        for (int s = 0; s < samples; s++)
        {
            Float3 origin = position;
            if (jitterRadius > 0)
            {
                float ju = rng.NextFloat() - 0.5f;
                float jv = rng.NextFloat() - 0.5f;
                origin = position + T * (ju * jitterRadius) + B * (jv * jitterRadius);
            }
            sum += TracePath(origin, normal, Float3.One, bounces, ref rng);
        }
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
                blocked = _tlas.AnyHit(ro, toLight, _options.RayBias, maxDist - 2 * _options.RayBias);

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
    /// each per-bounce shadow ray) to <paramref name="segs"/>. <paramref name="jitterWorldRadius"/>
    /// drives the same ray-origin jitter as <see cref="IntegrateIndirect"/> uses, so the recorded
    /// rays really match what the integrator does. The radiance is computed and returned but the
    /// caller usually discards it: this method is for visualisation only.
    /// </summary>
    public Float3 TracePathRecorded(Float3 position, Float3 normal, Float3 albedo, int bouncesLeft,
                                     ref Sampler rng, System.Collections.Generic.List<DebugSegment> segs,
                                     float jitterWorldRadius = 0f)
    {
        Float3 radiance = Float3.Zero;
        // Match IntegrateIndirect: jitter the ray-start position within the texel footprint.
        Float3 jitteredPos = position;
        float jitterRadius = (_options.JitterRayOrigin && jitterWorldRadius > 0)
            ? jitterWorldRadius * _options.JitterStrength : 0f;
        if (jitterRadius > 0)
        {
            Hemisphere.BuildOrthonormalBasis(normal, out var Tj, out var Bj);
            float ju = rng.NextFloat() - 0.5f;
            float jv = rng.NextFloat() - 0.5f;
            jitteredPos = position + Tj * (ju * jitterRadius) + Bj * (jv * jitterRadius);

            // A magenta marker line from texel centre to the jittered origin, so the visualiser
            // shows exactly where the jitter pushed the start point.
            segs.Add(new DebugSegment
            {
                Start = position,
                End = jitteredPos,
                BounceIndex = -1,
                IsShadow = false,
                Hit = true,
            });
        }
        Float3 ro = jitteredPos;
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

            if (!hit.Hit) { radiance += t * _options.SkyColor; break; }

            int blasIdx = _instanceToBlas[hit.InstanceIndex];
            var triRef = _blas[blasIdx].Triangles[hit.TriangleIndex];
            var mat = _resolvedMats[blasIdx][triRef.MaterialGroupIndex];
            Float3 hitAlbedo;
            if (_options.IgnoreAlbedo)
            {
                hitAlbedo = Float3.One;
            }
            else
            {
                hitAlbedo = mat is not null ? mat.DiffuseColor : new Float3(0.7f);
                if (mat is not null && mat.DiffuseTexture is not null
                    && _instances[hit.InstanceIndex].Mesh.UVLayers.TryGetValue(mat.DiffuseUVLayer, out var uvLayer))
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
                if (l.CastsShadows) blocked = _tlas.AnyHit(shadowOrigin, toLight, _options.RayBias, maxDist - 2 * _options.RayBias);
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

    private Float3 SampleAllLights(Float3 position, Float3 normal)
    {
        Float3 sum = Float3.Zero;
        for (int i = 0; i < _scene.Lights.Count; i++)
        {
            var l = _scene.Lights[i];
            if (!l.Sample(position, out var toLight, out var Li, out float maxDist, _scene.DefaultAttenuation)) continue;
            if (Li.X + Li.Y + Li.Z <= 0) continue;
            float ndotl = Float3.Dot(normal, toLight);
            if (ndotl <= 0) continue;

            if (l.CastsShadows)
            {
                var ro = position + normal * _options.RayBias;
                if (_tlas.AnyHit(ro, toLight, _options.RayBias, maxDist - 2 * _options.RayBias))
                    continue;
            }
            sum += Li * ndotl;
        }
        return sum;
    }

    public HitInfo ClosestHitWorld(Float3 ro, Float3 rd, float maxT)
    {
        if (_tlas.ClosestHit(ro, rd, _options.RayBias, maxT, out var hit))
        {
            var inst = _instances[hit.InstanceIndex];
            var blas = _blas[_instanceToBlas[hit.InstanceIndex]];
            var tri = blas.Triangles[hit.TriangleIndex];
            var positions = inst.Mesh.Positions;
            var normals = inst.Mesh.Normals;
            float w = 1f - hit.U - hit.V;
            var pLocal = positions[tri.I0] * w + positions[tri.I1] * hit.U + positions[tri.I2] * hit.V;
            var nLocal = normals[tri.I0]   * w + normals[tri.I1]   * hit.U + normals[tri.I2]   * hit.V;
            hit.Position = Tlas.Transform(inst.WorldTransform, pLocal, 1f);
            hit.Normal = Float3.Normalize(Tlas.Transform(inst.WorldTransform, nLocal, 0f));
        }
        return hit;
    }
}

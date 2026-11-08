// The ocean's surface, drawn by OceanSurface's draw feature through the Ocean pass after the opaques, before the
// atmosphere, so clouds and fog see its depth. It is drawn as rings of tiles around the viewer, each ring twice the
// size of the one inside it. Vertices snap to twice their ring's grid so the waves never swim, and fold onto the next
// ring's grid toward the ring's outer edge, their waves filtered toward the next ring's as well, so rings meet without
// cracks. The waves move them, and the planet's curve drops them by d^2 / 2R with their distance d from the viewer.
// Per ring, from OceanSurface: custom0 the ring's centre minus the viewer in xz and its morph phase, custom1 the
// centre within the largest wave tile, the tile size in metres and quads per tile, and color x what is added to the
// morph (the innermost ring fading out as the viewer climbs) and y the morph's black point.
// The water: a GGX dielectric over a body that absorbs the refracted scene per channel along the path to the floor,
// which scatters the sun and sky back in its scatter colour, and glows where crests pinch toward a low sun behind them.
// Water under the floor stays drawn, flat and without foam, so where the floor capture is coarser than the scene it
// still looks like water and geometry hides the rest.
// Waves shrink in shallow water and foam gathers along the shore, from the floor's depth OceanFeature keeps in slices
// around its viewer, color zw being this ring's viewer minus that one.
// A pixel is seen from below where a back face shows, unless the camera is over 2 m above sea level, or always when
// it is over 2 m under, which keeps self folding waves from flipping. From below the water shows the world above through Snell's window, the rest of the sky reflecting the water's own
// scatter colour, total internal reflection, and the underwater pass fogs it by its distance. The
// OceanMask pass draws the same surface for that pass, +1 where its front faces show and -1 where its back faces do.
// Where waves fold over, too steep, the fold shades as foam with its normal kept upward, never as a dark underside.
// Whitecaps come from a foam simulation in world space on the depth's slices, built where the summed waves fold
// and fading behind them, so they sit on the crests and linger. Past the slices the rule at its steady level from
// the visible surface's Jacobian stands in. They show through the foam texture, more of it as the foam thickens, lit with a
// normal bent by the texture's own slopes.
Shader "Graphite/Ocean"
{
    Properties
    {
        _OceanDisplacement("Displacement", Texture2DArray) = "black" {}
        _OceanSlopes("Slopes", Texture2DArray) = "black" {}
        _OceanFoam("Foam", Texture2DArray) = "black" {}
        _OceanDepth("Depth", Texture2DArray) = "white" {}
        _OceanLevels("Levels", Texture2DArray) = "black" {}
        _OceanFlow("Flow", Texture2DArray) = "black" {}
        _OceanClip("Clip", Texture2DArray) = "black" {}
        _OceanWhitecaps("Whitecaps", Texture2DArray) = "black" {}
        _OceanDepthParams("Depth Params", Vector) = (0, 0, 64, 256)
        _OceanShore("Shore", Vector) = (0.95, 0.65, 2, 7)
        _OceanRipples("Ripples", Texture2DArray) = "black" {}
        _OceanState("State", Texture2D) = "black" {}
        _OceanRippleParams("Ripple Params", Vector) = (3, 0.3, 3, 0)
        _OceanFoamTexture("Foam Texture", Texture2D) = "white" {}
        _OceanLightAbsorption("Light Absorption", Vector) = (0.09, 0.066, 0.09, 0)
        _OceanWhitecapRule("Whitecap Rule", Vector) = (0.55, 1, 0.8, 0)
        _OceanCaustics("Caustics", Texture2D) = "black" {}
        _CausticsTextureScale("Caustics Scale", Float) = 5 [Range(0, 25)]
        _CausticsTextureAverage("Caustics Grey Point", Float) = 0.07 [Range(0, 1)]
        _CausticsStrength("Caustics Strength", Float) = 3.2 [Range(0, 10)]
        _CausticsFocalDepth("Caustics Focal Depth", Float) = 2 [Range(0, 250)]
        _CausticsDepthOfField("Caustics Depth of Field", Float) = 0.33 [Range(0.01, 1000)]
        _CausticsDistortionStrength("Caustics Distortion", Float) = 0.1 [Range(0, 0.25)]
        _CausticsDistortionScale("Caustics Distortion Scale", Float) = 35 [Range(0.01, 50)]
        _OceanNormals("Wave Normals", Texture2D) = "bump" {}
        _NormalsScale("Normals Scale", Float) = 40 [Range(0.01, 200)]
        _NormalsStrength("Normals Strength", Float) = 0.36 [Range(0, 2)]
        _ScreenReflections("Screen Reflections", Float) = 1 [Range(0, 1)]
        _OceanSizes("Cascade Sizes", Vector) = (1024, 256, 64, 16)
        _OceanFade("Cascade Fade", Vector) = (1000000, 6000, 1500, 400)
        _PlanetRadius("Planet Radius", Float) = 6371000
        _Roughness("Roughness", Float) = 0.02 [Range(0, 1)]
        _FarRoughness("Far Roughness", Float) = 0.12 [Range(0, 1)]
        _FarRoughnessDistance("Far Roughness Distance", Float) = 2000
        _Absorption("Absorption", Color) = (0.3, 0.22, 0.3, 1)
        _ScatterColor("Scatter Color", Color) = (0.02, 0.09, 0.12, 1)
        _SubsurfaceColor("Subsurface Color", Color) = (0.1, 0.5, 0.45, 1)
        _SubsurfaceSun("Subsurface Sun", Float) = 2.0
        _SubsurfaceFalloff("Subsurface Falloff", Float) = 5.0
        _RefractionStrength("Refraction Strength", Float) = 0.1
        _FoamColor("Foam Color", Color) = (1, 1, 1, 1)
        _FoamScale("Foam Scale", Float) = 3
        _FoamFeather("Foam Feather", Float) = 0.4
        _FoamLightScale("Foam Light Scale", Float) = 1.35 [Range(0, 2)]
        _FoamNormalStrength("Foam Normal Strength", Float) = 3.5 [Range(0, 30)]
        _FoamSpecularFalloff("Foam Specular Falloff", Float) = 293 [Range(1, 512)]
        _FoamSpecularBoost("Foam Specular Boost", Float) = 0.15 [Range(0, 16)]
        _ShorelineFoamMinDepth("Shoreline Foam Min Depth", Float) = 0.27
    }

    Pass
    {
        Name "Ocean"
        Tags { "LightMode" = "Ocean" "SceneColor" = "Read" "SceneDepth" = "Read" }
        Cull Off
        GLSLPROGRAM
        Shared
        {
            #pragma variant global PBR_TIER FULL LITE
        }
        Vertex
        {
            #include "Graphite/Ocean/OceanVertex.glsl"

            layout(location = 0) out vec3 vRelativePos;
            layout(location = 1) out vec2 vWave;
            layout(location = 2) out vec2 vBed;
            layout(location = 3) flat out float vSeaAbove;
            layout(location = 4) out vec2 vLanded;
            layout(location = 5) out float vLodAlpha;
            layout(location = 6) flat out float vRingScale;

            void main()
            {
                float depth;
                Ocean_SurfaceVertex(vRelativePos, vWave, vBed, vLanded, depth, vLodAlpha);
                vRingScale = Graphite_Object().custom1.z;
                vSeaAbove = Graphite_ObjectToRelative(vec3(0.0)).y + Ocean_WaterLevel(Graphite_Object().color.zw);
                gl_Position = Graphite_RelativeToClip(vRelativePos);
            }
        }
        Fragment
        {
            #define PBR_TRANSMISSION
            #include "Graphite/PBR/Closure.glsl"
            #include "Graphite/SceneDepth.glsl"
            #include "Graphite/Fog.glsl"
            #include "Graphite/Passes.glsl"
            #include "Graphite/Ocean/Ocean.glsl"
            #include "Graphite/Ocean/OceanCaustics.glsl"

            uniform float _PlanetRadius;
            uniform float _Roughness;
            uniform float _FarRoughness;
            uniform float _FarRoughnessDistance;
            uniform vec4 _Absorption;
            uniform vec4 _ScatterColor;
            uniform vec4 _SubsurfaceColor;
            uniform float _SubsurfaceSun;
            uniform float _SubsurfaceFalloff;
            uniform float _RefractionStrength;
            uniform float _ShorelineFoamMinDepth;
            uniform vec4 _FoamColor;
            uniform float _FoamScale;
            uniform float _FoamFeather;
            uniform float _FoamLightScale;
            uniform float _FoamNormalStrength;
            uniform float _FoamSpecularFalloff;
            uniform float _FoamSpecularBoost;
            uniform sampler2D _OceanFoamTexture;
            uniform vec4 _OceanLightAbsorption;
            uniform vec4 _OceanWhitecapRule; // the whitecap coverage, strength and fade, OceanFeature's, for water past the simulation
            uniform float _NormalsScale;
            uniform float _NormalsStrength;
            uniform float _ScreenReflections;

            layout(location = 0) in vec3 vRelativePos;
            layout(location = 1) in vec2 vWave;
            layout(location = 2) in vec2 vBed;
            layout(location = 3) flat in float vSeaAbove;
            layout(location = 4) in vec2 vLanded;
            layout(location = 5) in float vLodAlpha;
            layout(location = 6) flat in float vRingScale;
            layout(location = 0) out vec4 fragColor;

            // A point relative to the camera in units of a span, times a factor, kept small far from the origin
            vec2 WorldUV(vec2 relative, float factor, float span)
            {
                Graphite_ViewData view = Graphite_View();
                return fract(view.cameraHigh.xz * factor / span) + (view.cameraLow.xz + relative) * factor / span;
            }

            // A point relative to the camera in units of a span, turned by an angle, kept small far from the origin
            vec2 TurnedUV(vec2 relative, float span, float angle)
            {
                Graphite_ViewData view = Graphite_View();
                mat2 turn = mat2(cos(angle), sin(angle), -sin(angle), cos(angle));
                return fract(turn * view.cameraHigh.xz / span) + turn * (view.cameraLow.xz + relative) / span;
            }

            float FoamHash(vec2 cell)
            {
                cell = mod(cell, 64.0);
                return fract(sin(dot(cell, vec2(127.1, 311.7))) * 43758.5453);
            }

            // Smooth value noise repeating every 64 spans
            float FoamNoise(vec2 relative, float span)
            {
                Graphite_ViewData view = Graphite_View();
                vec2 at = mod(view.cameraHigh.xz / span, 64.0) + (view.cameraLow.xz + relative) / span;
                vec2 cell = floor(at), f = at - cell;
                f = f * f * (3.0 - 2.0 * f);
                return mix(mix(FoamHash(cell), FoamHash(cell + vec2(1.0, 0.0)), f.x), mix(FoamHash(cell + vec2(0.0, 1.0)), FoamHash(cell + vec2(1.0, 1.0)), f.x), f.y);
            }

            // How the foam texture's taps mix at an undisplaced point
            vec2 FoamMix(vec2 relative)
            {
                float span = max(_FoamScale, 1e-3);
                return smoothstep(0.25, 0.75, vec2(FoamNoise(relative, span * 4.0), FoamNoise(relative + 1000.0, span * 6.0)));
            }

            // The white foam texture at an undisplaced point a fixed size in metres, three taps at unrelated spans and
            // angles drifting apart over time, so the texture's tiling does not show
            float WhiteFoam(vec2 relative, float time, vec2 weights)
            {
                float span = max(_FoamScale, 1e-3);
                float a = texture(_OceanFoamTexture, TurnedUV(relative + vec2(0.1, 0.1) * time, span, 0.6)).r;
                float b = texture(_OceanFoamTexture, TurnedUV(relative + vec2(-0.07, 0.05) * time, span * 1.37, 2.1)).r;
                float c = texture(_OceanFoamTexture, TurnedUV(relative + vec2(0.04, -0.09) * time, span * 2.19, 4.0)).r;
                return mix(mix(a, b, weights.x), c, 0.25 + 0.5 * weights.y);
            }

            // Two taps of the wave normal map scrolling nearly opposite ways at a speed in m/s, at one span in metres
            vec2 NormalTaps(vec2 relative, float span, float speed, float time)
            {
                Graphite_ViewData view = Graphite_View();
                vec2 at = fract(view.cameraHigh.xz / span) + (view.cameraLow.xz + relative) / span;
                vec2 a = texture(_OceanNormals, at + vec2(0.94, 0.34) * speed * time / span).rg * 2.0 - 1.0;
                vec2 b = texture(_OceanNormals, at + vec2(-0.85, -0.53) * speed * time / span).rg * 2.0 - 1.0;
                return a + b;
            }

            // Normal map detail, the pair at the ring's scale blended into the pair at twice it by the ring's morph, from
            // an undisplaced point relative to the camera, scrolling at fitted speeds from a texel a 96th of the scale
            vec2 NormalDetail(vec2 relative, float scale, float blend)
            {
                float span = max(_NormalsScale, 0.01) * scale / 100.0;
                float time = Graphite_View().time.x;
                float texel = scale / 96.0;
                float near = pow(log(1.0 + 2.0 * texel) * 1.875, 1.4), far = pow(log(1.0 + 4.0 * texel) * 1.875, 1.4);
                return _NormalsStrength * mix(NormalTaps(relative, span, near, time), NormalTaps(relative, span * 2.0, far, time), blend);
            }

            // Where a camera relative point lands on the screen, x across and y down, and its distance from the camera
            vec3 ScreenOf(vec3 relative)
            {
                vec4 clip = Graphite_RelativeToClip(relative);
                vec2 ndc = clip.xy / max(clip.w, 1e-6);
                return vec3(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5, length(relative));
            }

            // The opaque scene the water reflects along a ray, marched against the scene's depth in steps growing by a
            // quarter, and how much to trust it: none off the screen, toward the camera or where the ray hits nothing,
            // fading at the screen's edges. The scene copy holds no water, so the water never reflects itself.
            vec4 ReflectScene(vec3 origin, vec3 direction)
            {
                float travel = 0.5, stride = 0.5;
                for (int i = 0; i < 24; i++)
                {
                    vec3 at = ScreenOf(origin + direction * travel);
                    if (any(lessThan(at.xy, vec2(0.0))) || any(greaterThan(at.xy, vec2(1.0))))
                        return vec4(0.0);
                    if (Graphite_FetchSceneDepth(at.xy) > 0.0)
                    {
                        float scene = length(Graphite_SceneRelativePosition(at.xy));
                        if (scene < at.z && at.z - scene < max(stride * 2.0, 1.0))
                        {
                            vec2 edge = min(at.xy, 1.0 - at.xy);
                            return vec4(Graphite_SampleSceneColor(at.xy, 0.0), smoothstep(0.0, 0.08, min(edge.x, edge.y)));
                        }
                    }
                    stride *= 1.25;
                    travel += stride;
                }
                return vec4(0.0);
            }

            // The distance along the pixel's ray to the opaque scene at a screen uv, far where there is none
            float SceneDistance(vec2 uv, vec3 ray)
            {
                return Graphite_FetchSceneDepth(uv) > 0.0 ? dot(Graphite_SceneRelativePosition(uv), ray) : 1e6;
            }

            void main()
            {
                if (Ocean_Clipped(vLanded))
                    discard;
                float far = length(vRelativePos);
                float shoreDepth = Ocean_WaterDepth(vBed);
                vec4 depths = Ocean_CascadeDepths(vBed);
                Ocean_Fields fields = Ocean_FlowFields(vWave, Ocean_Flow(vBed), dFdx(vWave), dFdy(vWave), far, depths);
                vec4 slopes = fields.slopes;
                slopes.xy += Ocean_Ripples(vBed).zw;
                float twist = fields.twist;
                float jacobian = (1.0 + slopes.z) * (1.0 + slopes.w) - twist * twist;

                vec2 curve = _PlanetRadius > 0.0 ? vRelativePos.xz / _PlanetRadius : vec2(0.0);
                vec3 tangentX = vec3(1.0 + slopes.z, slopes.x, twist);
                vec3 tangentZ = vec3(twist, slopes.y, 1.0 + slopes.w);
                vec3 surface = cross(tangentZ, tangentX);
                surface.y = max(abs(surface.y), 0.05);
                vec2 detail = vec2(0.0);
#ifndef PBR_TIER_LITE
                detail = NormalDetail(vRelativePos.xz + vBed - vLanded, vRingScale, vLodAlpha);
#endif
                vec3 normal = normalize(vec3(surface.x / surface.y + curve.x + detail.x, 1.0, surface.z / surface.y + curve.y + detail.y));
                vec3 geomNormal = normalize(vec3(curve.x, 1.0, curve.y));
                vec3 view = Graphite_ViewDir(vRelativePos);
                bool below = vSeaAbove > 2.0 || (vSeaAbove > -2.0 && !gl_FrontFacing);
                if (below)
                {
                    normal = -normal;
                    geomNormal = -geomNormal;
                }

                PBR_Params p = PBR_DefaultParams(normal, normalize(cross(normal, vec3(0.0, 0.0, 1.0))));
                p.baseColor = vec3(0.0);
                p.specularIor = 1.333;
                p.specularRoughness = mix(_Roughness, _FarRoughness, clamp(far / max(_FarRoughnessDistance, 1.0), 0.0, 1.0));
                p.transmissionWeight = 1.0;
                PBR_Closure c = PBR_Prepare(p, vRelativePos, view, geomNormal, 0.0);

                // The lights as PBR_ShadeLights gives them, keeping the sun's shadowed light for the water's body
                Graphite_LightFrame frame = PBR_LightFrame(c);
                Graphite_LightContribution light;
                vec3 direct = vec3(0.0), sun = vec3(0.0), toSun = vec3(0.0, 1.0, 0.0);
                for (uint i = 0u; i < Graphite_DirectionalCount(); i++)
                {
                    Graphite_DirectionalLight d = Graphite_GetDirectional(i);
                    if (!Graphite_ContributeDirectional(d, i, frame, light))
                        continue;
                    direct += Graphite_BRDF(c, light);
                    if (i == 0u)
                    {
                        sun = light.radiance;
                        toSun = d.direction;
                    }
                }
                Graphite_LightIterator it = Graphite_BeginLights(c.position);
                Graphite_LightSample local;
                while (Graphite_NextLight(it, local))
                {
                    if (Graphite_ContributeLocal(local, frame, false, light))
                        direct += Graphite_BRDF(c, light);
                }
                Graphite_ProbeSet probes = PBR_FindProbes(c);
                PBR_Indirect indirect = PBR_GatherIndirect(c, vec2(0.0), probes);
#ifndef PBR_TIER_LITE
                if (!below && _ScreenReflections > 0.5)
                {
                    vec3 bounce = reflect(-view, normal);
                    vec4 reflected = ReflectScene(vRelativePos, bounce);
                    indirect.specular = mix(indirect.specular, reflected.rgb, reflected.a * smoothstep(0.9, 0.6, dot(bounce, view)));
                }
#endif
                vec3 ambient = Graphite_AmbientBRDF(c, indirect);

                vec3 sky = Graphite_IndirectDiffuse(vec3(0.0, 1.0, 0.0), vec2(0.0));

                vec3 scatter = _ScatterColor.rgb * (sky + sun * max(toSun.y, 0.0)) / GRAPHITE_PI;
                float pinch = clamp(0.6 - 0.12 * jacobian, 0.0, 1.0);
                float towardSun = pow(max(dot(toSun, -view), 0.0), _SubsurfaceFalloff);
                scatter += _SubsurfaceColor.rgb * sun * (_SubsurfaceSun * towardSun * pinch * (1.0 - view.y * view.y)) / GRAPHITE_PI;

                vec2 uv = Graphite_ScreenUV();
                vec3 ray = Graphite_PixelRay();
                float front = dot(vRelativePos, ray);
                float behind = max(SceneDistance(uv, ray) - front, 0.0);
                float contact = behind;
                vec2 refracted = uv + normal.xz * (_RefractionStrength * min(behind, 2.0) / max(front, 1.0));
                float refractedBehind = SceneDistance(refracted, ray) - front;
                if (refractedBehind < 0.0)
                    refracted = uv;
                else
                    behind = refractedBehind;
                vec3 transmittance = exp(-_Absorption.rgb * behind);
                float whitecaps = Ocean_Whitecaps(vBed);
                if (whitecaps < 0.0)
                    whitecaps = 4.0 * _OceanWhitecapRule.y * (_OceanWhitecapRule.x - jacobian);
                whitecaps = clamp(whitecaps, 0.0, 1.0);
                float amount = max(whitecaps, Ocean_ShoreFoam(shoreDepth));
                amount *= clamp(contact / max(_ShorelineFoamMinDepth, 1e-3), 0.0, 1.0);
                vec2 undisplaced = vRelativePos.xz + vBed - vLanded;
                float time = Graphite_View().time.x;

                vec3 seen = Graphite_SampleSceneColor(refracted, 0.0);
                if (!below && Graphite_FetchSceneDepth(refracted) > 0.0)
                {
                    vec3 floorPoint = Graphite_SceneRelativePosition(refracted);
                    float waterAbove = vRelativePos.y - floorPoint.y;
                    if (waterAbove > 0.0)
                    {
#ifndef PBR_TIER_LITE
                        if (Graphite_DirectionalCount() > 0u)
                        {
                            float shade = Graphite_MainDirectionalShadowFactor(floorPoint, vec3(0.0, 1.0, 0.0));
                            seen *= mix(vec3(1.0), Ocean_Caustics(Graphite_View(), floorPoint, waterAbove, toSun), shade);
                        }
#endif
                        seen *= exp(-_OceanLightAbsorption.rgb * waterAbove / max(toSun.y, 0.2));
                    }
                }
                vec3 body = seen * transmittance + scatter * (1.0 - transmittance);

                vec3 color = direct + ambient + PBR_Transmittance(c) * body;
                if (below)
                {
                    float cosOut = clamp(dot(normal, view), 0.0, 1.0);
                    float sinIn = 1.333 * sqrt(1.0 - cosOut * cosOut);
                    float reflected = sinIn >= 1.0 ? 1.0 : 0.02 + 0.98 * pow(1.0 - sqrt(1.0 - sinIn * sinIn), 5.0);
                    color = Graphite_SampleSceneColor(refracted, 0.0) * (1.0 - reflected) + scatter * reflected;
                }
                // Without foam its texture never reaches the threshold, so its taps are skipped
                if (amount > 0.0)
                {
                    float threshold = clamp(1.0 - amount, 0.0, 1.0), feather = max(_FoamFeather, 1e-3);
                    vec2 weights = FoamMix(undisplaced);
                    float white = WhiteFoam(undisplaced, time, weights);
                    float foam = smoothstep(threshold, threshold + feather, white);
                    vec3 foamNormal = normal;
#ifndef PBR_TIER_LITE
                    float nudge = max(0.25 * far / 512.0, 0.01) * max(_FoamScale, 1e-3);
                    float foamX = smoothstep(threshold, threshold + feather, WhiteFoam(undisplaced + vec2(nudge, 0.0), time, weights));
                    float foamZ = smoothstep(threshold, threshold + feather, WhiteFoam(undisplaced + vec2(0.0, nudge), time, weights));
                    foamNormal = normalize(normal + _FoamNormalStrength * vec3(foam - foamX, 0.0, foam - foamZ));
#endif
                    vec3 foamLit = _FoamColor.rgb * (sky + _FoamLightScale * sun * max(dot(foamNormal, toSun), 0.0) / GRAPHITE_PI);
                    foamLit += pow(max(dot(reflect(-view, foamNormal), toSun), 0.0), _FoamSpecularFalloff) * _FoamSpecularBoost * sun / GRAPHITE_PI;
                    color = mix(color, foamLit, foam * _FoamColor.a);
                }

                color = mix(Graphite_SampleSceneColor(uv, 0.0), color, clamp(behind / 0.2, 0.0, 1.0));
                fragColor = vec4(Graphite_SaturateHalf(Graphite_ApplyFog(color, vRelativePos)), 1.0);
                Graphite_WriteMotion(graphite_MotionCurrent, graphite_MotionPrevious);
                Graphite_WriteReactive(1.0);
            }
        }
        ENDGLSL
    }

    Pass
    {
        Name "OceanMask"
        Tags { "LightMode" = "OceanMask" }
        Cull Off
        GLSLPROGRAM
        Vertex
        {
            #include "Graphite/Ocean/OceanVertex.glsl"

            layout(location = 0) flat out float vSeaAbove;
            layout(location = 1) out vec2 vLanded;

            void main()
            {
                vec3 relative;
                vec2 wave, bed;
                float depth;
                float lodAlpha;
                Ocean_SurfaceVertex(relative, wave, bed, vLanded, depth, lodAlpha);
                vSeaAbove = Graphite_ObjectToRelative(vec3(0.0)).y + Ocean_WaterLevel(Graphite_Object().color.zw);
                gl_Position = Graphite_RelativeToClip(relative);
            }
        }
        Fragment
        {
            #include "Graphite/Ocean/Ocean.glsl"

            layout(location = 0) flat in float vSeaAbove;
            layout(location = 1) in vec2 vLanded;
            layout(location = 0) out vec4 fragColor;
            void main()
            {
                bool below = vSeaAbove > 2.0 || (vSeaAbove > -2.0 && !gl_FrontFacing);
                if (Ocean_Clipped(vLanded))
                    below = vSeaAbove > 0.0;
                fragColor = vec4(below ? -1.0 : 1.0);
            }
        }
        ENDGLSL
    }
}

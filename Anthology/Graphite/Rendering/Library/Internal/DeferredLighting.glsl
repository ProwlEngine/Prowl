// The lighting pass of a deferred view: every pixel of the GBuffer lit as the forward pass lights a surface, by the
// light function of its shading model, onto the scene's colour and, with its top specular lobe, the scene's material
// target, which screen space reflections read. A surface drawn in front by the prepass left the GBuffer no surface at
// its pixel, which is skipped. Pixels of an unlit model pass their emission through. Included last in the program ShadingModels writes, after
// Graphite/Internal/DeferredLightingHead.glsl, the models' code, graphite_DeferredUnpack, Graphite_BRDF,
// Graphite_AmbientBRDF and graphite_DeferredUnlit.
layout(location = 1) out vec4 outMaterial;

// A displaced view's offsets: how far away, in pixels, the pixel each pixel now shows came from, which the GBuffer's
// own targets are read at while the scene's depth and normal are read in place. Graphite_GBufferDisplaced is 1 on
// such a view. See DisplacementFeature.
uniform GRAPHITE_SCREEN_TEXTURE Graphite_GBufferOffset;
uniform float Graphite_GBufferDisplaced;

// The view's screen space reflections, in a how far the trace is trusted, while Graphite_HasReflections is 1
uniform GRAPHITE_SCREEN_TEXTURE Graphite_Reflections;
uniform float Graphite_HasReflections;

void main()
{
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    ivec2 source = pixel;
    if (Graphite_GBufferDisplaced > 0.5)
        source += ivec2(round(Graphite_Fetch(Graphite_GBufferOffset, pixel, 0).rg));
    vec4 surface = Graphite_GBufferSurfaceAt(source);
    if (Graphite_GBufferModelOf(surface) == GRAPHITE_MODEL_NONE)
        discard;

    float depth = Graphite_GBufferDepthAt(pixel);
    Graphite_ViewData view = Graphite_View();
    vec3 relative = Graphite_RelativeFromDepth(view, gl_FragCoord.xy * view.screen.zw, depth);
#ifdef GRAPHITE_CONTACT_SHADOWS
    graphite_FindSunContact(relative, Graphite_OctDecode(Graphite_Fetch(Graphite_GBufferNormal, pixel, 0).rg), (uint(surface.a * 255.0 + 0.5) & 128u) != 0u);
#endif
    Graphite_GBuffer g = Graphite_ReadGBuffer(source, surface);
    if (source != pixel)
        g.normal = Graphite_OctDecode(Graphite_Fetch(Graphite_GBufferNormal, pixel, 0).rg);
    uint model = g.model;
    graphite_DeferredModel = model;
    graphite_DeferredCustom = g.custom;
    graphite_DeferredShadows = g.receivesShadows;
    graphite_DeferredLayers = g.lightLayers;

    vec3 color = g.emission;
    vec3 reflectance = vec3(0.0);
    float roughness = 1.0;
    if (!graphite_DeferredUnlit(model))
    {
        PBR_Params p = PBR_FromGBuffer(g);
        graphite_DeferredUnpack(model, g.custom, p);
        Graphite_LightIterator lights = Graphite_BeginLights(relative);
        Graphite_ShadowLights(lights, relative, g.normal);
        PBR_Closure c = PBR_Prepare(p, relative, Graphite_ViewDir(relative), g.normal, 0.0);
        vec3 direct = PBR_ShadeLights(c, lights);
        Graphite_ProbeSet probes = PBR_FindProbes(c);
        PBR_Indirect indirect = PBR_GatherIndirectBaked(c, g.baked, probes);
        color += direct + Graphite_AmbientBRDF(c, indirect);
        vec3 topNormal;
        reflectance = PBR_TopReflectance(c, topNormal, roughness);

        // The ambient shaded the top lobe with reflectance times the probes, its sky dimmed by the screen occlusion,
        // so the trace takes the probes' place by its confidence
        if (Graphite_HasReflections > 0.5)
        {
            vec4 traced = Graphite_Fetch(Graphite_Reflections, pixel, 0);
            float confidence = traced.a * traced.a;
            if (confidence > 0.0 && any(greaterThan(reflectance, vec3(0.0))))
            {
                vec3 held = vec3(0.0);
                if (Graphite_ReflectionProbesReady())
                {
                    vec3 V = Graphite_ViewDir(relative);
                    float alpha = roughness * roughness;
                    Graphite_ProbeSet top = probes;
                    top.sky *= Graphite_SkyReflectionOcclusion(clamp(dot(topNormal, V), 0.0, 1.0), 1.0, Graphite_ScreenOcclusion(), alpha);
                    held = Graphite_ProbeRadiance(top, topNormal, V, alpha);
                }
                color = max(color + reflectance * (traced.rgb - held) * confidence, vec3(0.0));
            }
        }
    }

    outColor = vec4(Graphite_SaturateHalf(Graphite_ApplyFog(color, relative)), 1.0);
    outMaterial = vec4(reflectance, roughness);
}

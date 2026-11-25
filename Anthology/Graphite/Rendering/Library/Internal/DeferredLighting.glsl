// The lighting pass of a deferred view: every pixel of the GBuffer lit as the forward pass lights a surface, by the
// light function of its shading model, onto the scene with its top specular lobe, which screen space reflections read.
// Its depth tests against the scene's, so a surface drawn in front by the prepass keeps its pixel. Pixels of an unlit
// model pass their emission through. Included last in the program ShadingModels writes, after
// Graphite/Internal/DeferredLightingHead.glsl, the models' code, graphite_DeferredUnpack, Graphite_BRDF,
// Graphite_AmbientBRDF and graphite_DeferredUnlit.
layout(location = 4) out vec4 outMaterial;

void main()
{
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    uint model = Graphite_GBufferModelAt(pixel);
    if (model == GRAPHITE_MODEL_NONE)
        discard;

    float depth = Graphite_GBufferDepthAt(pixel);
    Graphite_GBuffer g = Graphite_ReadGBuffer(pixel);
    graphite_DeferredModel = model;
    graphite_DeferredCustom = g.custom;
    Graphite_ViewData view = Graphite_View();
    vec3 relative = Graphite_RelativeFromDepth(view, gl_FragCoord.xy * view.screen.zw, depth);
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
    }

    outColor = vec4(Graphite_SaturateHalf(Graphite_ApplyFog(color, relative)), 1.0);
    outMaterial = vec4(reflectance, roughness);
    gl_FragDepth = depth;
}

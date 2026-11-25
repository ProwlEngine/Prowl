// The world's decals over a deferred view's GBuffer: its base colour, metalness, normal, roughness and occlusion with
// every decal covering the pixel applied, as Graphite/Decals.glsl applies them in a forward pass, into targets of their
// own which the view's lighting then reads in place of the GBuffer's. Unlit pixels and pixels without a surface pass
// through. A pixel's layers are the three its GBuffer keeps, with every higher one taken as set.
#define GRAPHITE_GBUFFER_READ
#define GRAPHITE_SURFACE_LAYERS graphite_DecalLayers

uint graphite_DecalLayers = 0xFFFFFFFFu;

#include "Graphite/Decals.glsl"
#include "Graphite/GBuffer.glsl"

layout(location = 1) out vec2 outNormal;
layout(location = 2) out vec4 outSurface;

void main()
{
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    vec4 base = Graphite_Fetch(Graphite_GBufferBase, pixel, 0);
    vec4 surface = Graphite_Fetch(Graphite_GBufferSurface, pixel, 0);
    vec2 packed = Graphite_Fetch(Graphite_GBufferNormal, pixel, 0).rg;

    Graphite_GBuffer g;
    Graphite_UnpackModel(surface.a, g);
    if (g.model != GRAPHITE_MODEL_NONE && g.model != GRAPHITE_MODEL_UNLIT)
    {
        Graphite_ViewData view = Graphite_View();
        vec3 relative = Graphite_RelativeFromDepth(view, gl_FragCoord.xy * view.screen.zw, Graphite_GBufferDepthAt(pixel));
        vec3 normal = Graphite_OctDecode(packed);
        vec3 shaded = normal;
        graphite_DecalLayers = g.lightLayers | 0xFFFFFFF8u;
        Graphite_ApplyDecals(relative, normal, base.rgb, shaded, surface.r, base.a, surface.b);
        packed = Graphite_OctEncode(normalize(shaded));
    }

    outColor = clamp(base, 0.0, 1.0);
    outNormal = packed;
    outSurface = vec4(clamp(surface.rgb, 0.0, 1.0), surface.a);
}

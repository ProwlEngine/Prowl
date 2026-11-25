// One part of a deferred view's GBuffer as the image, see Graphite/GBuffer.glsl. Colours are written display encoded.
// Baked light and emission, which are pre-exposed, take a simple tonemap.
#define GRAPHITE_GBUFFER_READ
#include "Graphite/Internal/PostView.glsl"
#include "Graphite/GBuffer.glsl"

uniform int _Mode;

vec3 ModelColor(uint model)
{
    const vec3 colors[8] = vec3[](vec3(0.35, 0.55, 0.9), vec3(0.6), vec3(0.9, 0.35, 0.3), vec3(0.9, 0.75, 0.25),
        vec3(0.4, 0.8, 0.4), vec3(0.75, 0.4, 0.85), vec3(0.3, 0.8, 0.8), vec3(0.9, 0.55, 0.25));
    return colors[model & 7u] * (model > 7u ? 0.5 : 1.0);
}

vec3 Encode(vec3 linear) { return pow(clamp(linear, 0.0, 1.0), vec3(1.0 / 2.2)); }

void main()
{
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    float depth = Graphite_GBufferDepthAt(pixel);
    vec4 surface = Graphite_GBufferSurfaceAt(pixel);
    if (Graphite_GBufferModelOf(surface) == GRAPHITE_MODEL_NONE)
    {
        outColor = vec4(0.0, 0.0, 0.0, 1.0);
        return;
    }
    Graphite_GBuffer g = Graphite_ReadGBuffer(pixel, surface);
    vec3 color;
    if (_Mode == 0)
        color = Encode(g.baseColor);
    else if (_Mode == 1)
        color = vec3(g.metalness);
    else if (_Mode == 2)
        color = g.normal * 0.5 + 0.5;
    else if (_Mode == 3)
        color = vec3(g.roughness);
    else if (_Mode == 4)
        color = vec3(g.specular);
    else if (_Mode == 5)
        color = vec3(g.occlusion);
    else if (_Mode == 6)
        color = ModelColor(g.model);
    else if (_Mode == 7)
        color = g.custom.rgb;
    else if (_Mode == 8)
        color = Encode(g.baked / (1.0 + g.baked));
    else if (_Mode == 9)
        color = Encode(g.emission / (1.0 + g.emission));
    else if (_Mode == 10)
        color = vec3(abs(Graphite_GBufferMotionAt(pixel)) * 50.0, 0.0);
    else
        color = vec3(pow(depth, 0.25));
    outColor = vec4(color, 1.0);
}

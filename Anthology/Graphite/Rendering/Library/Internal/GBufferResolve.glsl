// A deferred view's GBuffer depth, motion and normal onto its scene, where what the prepass drew is not in front, so
// the features between the GBuffer pass and lighting read the scene's depth, motion and normals. See Graphite/GBuffer.glsl.
uniform GRAPHITE_SCREEN_TEXTURE Graphite_GBufferNormal;
uniform GRAPHITE_SCREEN_TEXTURE Graphite_GBufferSurface;
uniform GRAPHITE_SCREEN_TEXTURE Graphite_GBufferMotion;
uniform GRAPHITE_SCREEN_TEXTURE Graphite_GBufferDepth;

layout(location = 1) out vec2 outMotion;
layout(location = 3) out vec2 outNormal;

void main()
{
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    if ((uint(Graphite_Fetch(Graphite_GBufferSurface, pixel, 0).a * 255.0 + 0.5) & 15u) == 0u)
        discard;
    float depth = Graphite_Fetch(Graphite_GBufferDepth, pixel, 0).r;
    outColor = vec4(0.0);
    outMotion = Graphite_Fetch(Graphite_GBufferMotion, pixel, 0).rg;
    outNormal = Graphite_Fetch(Graphite_GBufferNormal, pixel, 0).rg;
    gl_FragDepth = depth;
}

#pragma variant HIZ_PACK
// Builds the view's nearest depth pyramid for Graphite/HiZ.glsl: each level from the one below, every texel the
// nearest reversed depth of its 2 x 2, then HIZ_PACK copies a level into its place in the one texture readers sample.

#include "Graphite/Internal/PostView.glsl"

uniform GRAPHITE_SCREEN_TEXTURE _Source;

#if defined(HIZ_PACK)
uniform int _HiZOffset;

void main() { outColor = Graphite_Fetch(_Source, ivec2(gl_FragCoord.xy) - ivec2(_HiZOffset, 0), 0); }
#else
void main()
{
    ivec2 texel = ivec2(gl_FragCoord.xy);
    ivec2 size = Graphite_Size(_Source, 0);
    float nearest = 0.0;
    for (int i = 0; i < 4; i++)
        nearest = max(nearest, Graphite_Fetch(_Source, min(texel * 2 + ivec2(i & 1, i >> 1), size - 1), 0).r);
    outColor = vec4(nearest);
}
#endif

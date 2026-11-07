// The volumetric fog over the sky: pixels with nothing drawn take the fog grid's whole column, the rest stay as they are.
#include "Graphite/Internal/PostView.glsl"
#include "Graphite/VolumetricFog.glsl"

uniform GRAPHITE_SCREEN_TEXTURE _FogSceneDepth;

void main()
{
    vec4 source = Graphite_SampleSource(vUV);
    ivec2 size = Graphite_Size(_FogSceneDepth, 0);
    if (Graphite_Fetch(_FogSceneDepth, clamp(ivec2(vUV * vec2(size)), ivec2(0), size - 1), 0).r > 0.0)
    {
        outColor = source;
        return;
    }
    Graphite_ViewData view = Graphite_PostView();
    vec3 ray = normalize(Graphite_RelativeFromDepth(view, vUV, 1.0));
    vec4 fog = Graphite_VolumetricFog(ray * graphite_World.volumetricGrid.y * 2.0);
    outColor = vec4(source.rgb * fog.a + fog.rgb, source.a);
}

// The volumetric fog over the drawn scene: every pixel takes the fog between the camera and the opaque surface it
// shows, pixels with none the fog grid's whole column. Transparent surfaces are fogged as what lies behind them.
#include "Graphite/Internal/PostView.glsl"
#include "Graphite/VolumetricFog.glsl"

uniform GRAPHITE_SCREEN_TEXTURE _FogSceneDepth;

void main()
{
    vec4 source = Graphite_SampleSource(vUV);
    ivec2 size = Graphite_Size(_FogSceneDepth, 0);
    float depth = Graphite_Fetch(_FogSceneDepth, clamp(ivec2(vUV * vec2(size)), ivec2(0), size - 1), 0).r;
    Graphite_ViewData view = Graphite_PostView();
    vec3 place = depth > 0.0 ? Graphite_RelativeFromDepth(view, vUV, depth)
        : normalize(Graphite_RelativeFromDepth(view, vUV, 1.0)) * (graphite_World.volumetricGrid.y * 2.0);
    vec4 fog = Graphite_VolumetricFog(place);
    outColor = vec4(source.rgb * fog.a + fog.rgb, source.a);
}

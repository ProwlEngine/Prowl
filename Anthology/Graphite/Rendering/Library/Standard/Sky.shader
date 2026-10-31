// The "Sky" surface model: a shader with Surface "Sky" draws a sky from its code, defining vec3 Sky(vec3 direction),
// the radiance in nits seen along a unit world direction. The model pre-exposes it by Graphite_Exposure() and clamps it
// to the largest half float. Sky passes are tagged "LightMode" = "Sky", see Graphite/Sky.glsl.
Shader "Graphite/Sky"
{
    Pass
    {
        Name "Sky"
        Tags { "LightMode" = "Sky" }
        GLSLPROGRAM
        Vertex
        {
            #include "Graphite/Sky.glsl"
            void main() { }
        }
        Fragment
        {
            #include "Graphite/Sky.glsl"
            #pragma surface
            #ifndef GRAPHITE_SURFACE_FUNCTION
            vec3 Sky(vec3 direction) { return vec3(0.0); }
            #endif
            layout(location = 0) out vec4 graphite_SkyColor;
            void main() { graphite_SkyColor = vec4(Graphite_SaturateHalf(Sky(Graphite_SkyDirection()) * Graphite_Exposure()), 1.0); }
        }
        ENDGLSL
    }
}

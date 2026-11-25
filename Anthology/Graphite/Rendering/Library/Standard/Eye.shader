// An eyeball on the Lit surface, lit by Graphite/PBR/Eye.glsl. The iris is the disc of iris_radius around the middle of
// UV0, as an eye mapped from the front has it, and its normal dishes inward by iris_concavity. A Surface function built
// on this reads how much of the pixel is iris from p.custom.z, to colour the iris, the pupil and the white.
ShadingModel "Eye"
{
    Properties
    {
        iris_radius("Iris Radius", Float) = 0.25 [Range(0, 0.5)] [Header("Eye")]
        iris_concavity("Iris Concavity", Float) = 0.6 [Range(-1, 1)]
    }

    Base "Lit"

    Pack
    {
        GLSLPROGRAM
        uniform float iris_radius;
        uniform float iris_concavity;

        void Model_Surface(inout PBR_Params p, Graphite_SurfaceData i)
        {
            vec2 across = (i.uv - 0.5) / max(iris_radius, 1e-4);
            float iris = 1.0 - smoothstep(0.9, 1.0, length(across));
            vec3 dished = normalize(i.geometryNormal - (i.tangent * across.x + i.bitangent * across.y) * iris_concavity);
            p.custom = vec4(Graphite_OctEncode(dished) * 0.5 + 0.5, iris, 0.0);
        }

        vec4 Model_Pack(PBR_Params p) { return p.custom; }
        ENDGLSL
    }

    Lighting
    {
        GLSLPROGRAM
        #include "Graphite/PBR/Eye.glsl"

        vec3 Model_Light(PBR_Closure c, Graphite_LightContribution l, vec4 custom)
        {
            return PBR_EyeLight(c, l, Graphite_OctDecode(custom.xy * 2.0 - 1.0), custom.z);
        }
        ENDGLSL
    }
}

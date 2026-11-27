// Fuzz over the Lit surface, for fabric and velvet, lit by Graphite/PBR/Lobes.glsl's fuzz from its colour times its
// weight, read back as the brightest channel being the weight, and its roughness.
ShadingModel "Cloth"
{
    Properties
    {
        fuzz_weight("Fuzz Weight", Float) = 0.0 [Range(0, 1)] [Header("Fuzz")]
        fuzz_color("Fuzz Color", Color) = (1.0, 1.0, 1.0, 1.0)
        fuzz_roughness("Fuzz Roughness", Float) = 0.5 [Range(0, 1)]
        fuzz_weight_map("Fuzz Weight Map", Texture2D) = "white" {} [Map]
        fuzz_color_map("Fuzz Color Map", Texture2D) = "white" {} [Map]
        fuzz_roughness_map("Fuzz Roughness Map", Texture2D) = "white" {} [Map]
    }

    Base "Lit"
    Define "PBR_FUZZ_INPUTS"

    Pack
    {
        GLSLPROGRAM
        vec4 Model_Pack(PBR_Params p) { return vec4(p.fuzzColor * p.fuzzWeight, p.fuzzRoughness); }
        ENDGLSL
    }

    Lighting
    {
        GLSLPROGRAM
        #include "Graphite/PBR/Lobes.glsl"

        vec3 Model_Light(PBR_Closure c, Graphite_LightContribution l, vec4 custom)
        {
            float weight = max(custom.r, max(custom.g, custom.b));
            return PBR_FuzzLight(c, l, custom.rgb / max(weight, 1e-4), weight, custom.a);
        }

        vec3 Model_Ambient(PBR_Closure c, PBR_Indirect i, vec4 custom)
        {
            float weight = max(custom.r, max(custom.g, custom.b));
            return PBR_FuzzAmbient(c, i, custom.rgb / max(weight, 1e-4), weight, custom.a);
        }
        ENDGLSL
    }
}

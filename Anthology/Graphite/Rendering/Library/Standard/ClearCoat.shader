// A clear coat over the Lit surface, for car paint, lacquer and wet surfaces, lit by Graphite/PBR/Lobes.glsl's coat from
// its weight, roughness, index of refraction and darkening. coat_color and the coat's own normal are not used.
ShadingModel "ClearCoat"
{
    Properties
    {
        coat_weight("Coat Weight", Float) = 0.0 [Range(0, 1)] [Header("Coat")]
        coat_color("Coat Color", Color) = (1.0, 1.0, 1.0, 1.0)
        coat_roughness("Coat Roughness", Float) = 0.0 [Range(0, 1)]
        coat_ior("Coat IOR", Float) = 1.6 [Range(1, 3)]
        coat_darkening("Coat Darkening", Float) = 1.0 [Range(0, 1)]
        coat_normal_scale("Coat Normal Scale", Float) = 1.0
        coat_weight_map("Coat Weight Map", Texture2D) = "white" {} [Map]
        coat_color_map("Coat Color Map", Texture2D) = "white" {} [Map]
        coat_roughness_map("Coat Roughness Map", Texture2D) = "white" {} [Map]
        coat_darkening_map("Coat Darkening Map", Texture2D) = "white" {} [Map]
        geometry_coat_normal_map("Geometry Coat Normal Map", Texture2D) = "normal" {} [Map] [Normal]
    }

    Base "Lit"
    Define "PBR_COAT_INPUTS"

    Pack
    {
        GLSLPROGRAM
        vec4 Model_Pack(PBR_Params p) { return vec4(p.coatWeight, p.coatRoughness, (p.coatIor - 1.0) * 0.5, p.coatDarkening); }

        // The base under a coat is rougher, and meets the coat's index of refraction in place of air's
        void Model_Params(inout PBR_Params p)
        {
            float C = clamp(p.coatWeight, 0.0, 1.0);
            float rb = clamp(p.specularRoughness, 0.0, 1.0);
            float rc = clamp(p.coatRoughness, 0.0, 1.0);
            p.specularRoughness = mix(rb, pow(min(1.0, rb * rb * rb * rb + 2.0 * rc * rc * rc * rc), 0.25), C);
            float base = max(p.specularIor, 1e-4);
            float nc = max(p.coatIor, 1e-4);
            p.specularIor = mix(base, nc > base ? nc / base : base / nc, C);
        }
        ENDGLSL
    }

    Lighting
    {
        GLSLPROGRAM
        #include "Graphite/PBR/Lobes.glsl"

        vec3 Model_Light(PBR_Closure c, Graphite_LightContribution l, vec4 custom)
        {
            return PBR_CoatLight(c, l, PBR_CoatOver(c, custom.x, custom.y, 1.0 + custom.z * 2.0, custom.w));
        }

        vec3 Model_Ambient(PBR_Closure c, PBR_Indirect i, vec4 custom)
        {
            return PBR_CoatAmbient(c, i, PBR_CoatOver(c, custom.x, custom.y, 1.0 + custom.z * 2.0, custom.w));
        }
        ENDGLSL
    }
}

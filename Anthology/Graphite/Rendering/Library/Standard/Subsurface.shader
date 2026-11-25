// Subsurface scattering in the bulk under the Lit surface, for wax, skin and marble, lit by Graphite/PBR/Lobes.glsl's
// subsurface from its colour and weight. The radius is not used, light wraps as far as the default one makes it. A thin
// walled sheet lit from behind is the ThinSubsurface model.
ShadingModel "Subsurface"
{
    Properties
    {
        subsurface_weight("Subsurface Weight", Float) = 0.0 [Range(0, 1)] [Header("Subsurface")]
        subsurface_color("Subsurface Color", Color) = (0.8, 0.8, 0.8, 1.0)
        subsurface_radius("Subsurface Radius", Float) = 1.0
        subsurface_radius_scale("Subsurface Radius Scale", Color) = (1.0, 0.5, 0.25, 1.0)
        subsurface_scatter_anisotropy("Subsurface Scatter Anisotropy", Float) = 0.0 [Range(-1, 1)]
        subsurface_weight_map("Subsurface Weight Map", Texture2D) = "white" {} [Map]
        subsurface_color_map("Subsurface Color Map", Texture2D) = "white" {} [Map]
        subsurface_radius_map("Subsurface Radius Map", Texture2D) = "white" {} [Map]
    }

    Base "Lit"
    Define "PBR_SUBSURFACE_INPUTS"
    LightWraps

    Pack
    {
        GLSLPROGRAM
        vec4 Model_Pack(PBR_Params p) { return vec4(p.subsurfaceColor, p.subsurfaceWeight); }
        ENDGLSL
    }

    Lighting
    {
        GLSLPROGRAM
        #include "Graphite/PBR/Lobes.glsl"

        vec3 Model_Light(PBR_Closure c, Graphite_LightContribution l, vec4 custom) { return PBR_SubsurfaceLight(c, l, custom.rgb, custom.a); }

        vec3 Model_Ambient(PBR_Closure c, PBR_Indirect i, vec4 custom) { return PBR_SubsurfaceAmbient(c, i, custom.rgb, custom.a); }
        ENDGLSL
    }
}

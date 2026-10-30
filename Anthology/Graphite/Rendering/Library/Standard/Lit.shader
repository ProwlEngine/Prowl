// Graphite's physically based surface, OpenPBR Surface v1.1.1: every spec parameter under its spec name, engine
// extensions under plain names. Each texturable input X has a map X_map, whose channel, UV set, scale, rotation and offset
// the material sets; the input is X times the map wherever the map is set. tangent_rotation turns geometry_tangent
// counter clockwise in radians after its map, as KHR_materials_anisotropy's anisotropyRotation. See Graphite/PBR/Closure.glsl.
// The coat, fuzz, thin film, subsurface and anisotropy are material axes on the forward and debug passes, each on while
// its weight (either anisotropy for PBR_ANISOTROPY) is not zero. A Surface function raising one from zero needs its
// material to enable the axis with Material.EnableKeyword. PBR_THIN_WALLED follows geometry_thin_walled, and while it is
// on the material draws both sides whatever its SurfaceCull, back faces shaded with the normal flipped.
// PBR_TRANSMISSION follows transmission_weight. While it is on the material draws as transparent whatever its surface,
// through the Transmission pass in place of Forward, which reads the opaque colour and depth behind it and, for volumes,
// the back depth the BackDepth pass draws. LITE views read none of them. See Graphite/PBR/Transmission.glsl.
// Transparent, transmissive and thin walled subsurface casters dither their shadow by the grey share of light they let
// through, see Graphite/PBR/Shadow.glsl. Materials with CastShadows off cast none.
// Also the "Lit" surface model: a shader with Surface "Lit" takes these passes and properties, its code in place of each
// "#pragma surface", defining void Surface(inout PBR_Params p, Graphite_SurfaceData i).
// A Surface block's Vertex code goes in place of each "#pragma surface_vertex".
// A Surface block's Lighting code supplies Graphite_BRDF or Graphite_AmbientBRDF to the passes marked "#pragma surface_lighting".
// Transparent surfaces write premultiplied colour, tagged "Output" = "Premultiplied" so every SurfaceBlend applies coverage once.
Shader "Graphite/Lit"
{
    Properties
    {
        base_weight("Base Weight", Float) = 1.0 [Range(0, 1)] [Header("Base")]
        base_color("Base Color", Color) = (0.8, 0.8, 0.8, 1.0)
        base_metalness("Base Metalness", Float) = 0.0 [Range(0, 1)]
        base_diffuse_roughness("Base Diffuse Roughness", Float) = 0.0 [Range(0, 1)]
        specular_weight("Specular Weight", Float) = 1.0 [Range(0, 1)] [Header("Specular")]
        specular_color("Specular Color", Color) = (1.0, 1.0, 1.0, 1.0)
        specular_roughness("Specular Roughness", Float) = 0.3 [Range(0, 1)]
        specular_roughness_anisotropy("Specular Roughness Anisotropy", Float) = 0.0 [Range(0, 1)] [Toggle(PBR_ANISOTROPY)]
        specular_ior("Specular IOR", Float) = 1.5 [Range(1, 3)]
        transmission_weight("Transmission Weight", Float) = 0.0 [Range(0, 1)] [Header("Transmission")] [Toggle(PBR_TRANSMISSION)] [Transmissive]
        transmission_color("Transmission Color", Color) = (1.0, 1.0, 1.0, 1.0)
        transmission_depth("Transmission Depth", Float) = 0.0
        transmission_scatter("Transmission Scatter", Color) = (0.0, 0.0, 0.0, 1.0)
        transmission_scatter_anisotropy("Transmission Scatter Anisotropy", Float) = 0.0 [Range(-1, 1)]
        transmission_dispersion_scale("Transmission Dispersion Scale", Float) = 0.0 [Range(0, 1)]
        transmission_dispersion_abbe_number("Transmission Dispersion Abbe Number", Float) = 20.0 [Range(9, 91)]
        subsurface_weight("Subsurface Weight", Float) = 0.0 [Range(0, 1)] [Header("Subsurface")] [Toggle(PBR_SUBSURFACE)]
        subsurface_color("Subsurface Color", Color) = (0.8, 0.8, 0.8, 1.0)
        subsurface_radius("Subsurface Radius", Float) = 1.0
        subsurface_radius_scale("Subsurface Radius Scale", Color) = (1.0, 0.5, 0.25, 1.0)
        subsurface_scatter_anisotropy("Subsurface Scatter Anisotropy", Float) = 0.0 [Range(-1, 1)]
        coat_weight("Coat Weight", Float) = 0.0 [Range(0, 1)] [Header("Coat")] [Toggle(PBR_COAT)]
        coat_color("Coat Color", Color) = (1.0, 1.0, 1.0, 1.0)
        coat_roughness("Coat Roughness", Float) = 0.0 [Range(0, 1)]
        coat_roughness_anisotropy("Coat Roughness Anisotropy", Float) = 0.0 [Range(0, 1)] [Toggle(PBR_ANISOTROPY)]
        coat_ior("Coat IOR", Float) = 1.6 [Range(1, 3)]
        coat_darkening("Coat Darkening", Float) = 1.0 [Range(0, 1)]
        fuzz_weight("Fuzz Weight", Float) = 0.0 [Range(0, 1)] [Header("Fuzz")] [Toggle(PBR_FUZZ)]
        fuzz_color("Fuzz Color", Color) = (1.0, 1.0, 1.0, 1.0)
        fuzz_roughness("Fuzz Roughness", Float) = 0.5 [Range(0, 1)]
        emission_luminance("Emission Luminance", Float) = 0.0 [Header("Emission")]
        emission_color("Emission Color", Color) = (1.0, 1.0, 1.0, 1.0) [HDR]
        thin_film_weight("Thin Film Weight", Float) = 0.0 [Range(0, 1)] [Header("Thin Film")] [Toggle(PBR_THIN_FILM)]
        thin_film_thickness("Thin Film Thickness", Float) = 0.5
        thin_film_ior("Thin Film IOR", Float) = 1.4 [Range(1, 3)]
        geometry_opacity("Geometry Opacity", Float) = 1.0 [Range(0, 1)] [Header("Geometry")]
        geometry_thin_walled("Geometry Thin Walled", Integer) = 0 [Enum(Off, On)] [Toggle(PBR_THIN_WALLED)] [TwoSided]
        ambient_occlusion("Ambient Occlusion", Float) = 1.0 [Range(0, 1)]
        normal_scale("Normal Scale", Float) = 1.0
        coat_normal_scale("Coat Normal Scale", Float) = 1.0
        alpha_cutoff("Alpha Cutoff", Float) = 0.5 [Range(0, 1)] [ShowIf(_SURFACE, CUTOUT)]
        tangent_rotation("Tangent Rotation", Float) = 0.0

        base_weight_map("Base Weight Map", Texture2D) = "white" {} [Map] [Header("Maps")]
        base_color_map("Base Color Map", Texture2D) = "white" {} [Map]
        base_metalness_map("Base Metalness Map", Texture2D) = "white" {} [Map]
        base_diffuse_roughness_map("Base Diffuse Roughness Map", Texture2D) = "white" {} [Map]
        specular_weight_map("Specular Weight Map", Texture2D) = "white" {} [Map]
        specular_color_map("Specular Color Map", Texture2D) = "white" {} [Map]
        specular_roughness_map("Specular Roughness Map", Texture2D) = "white" {} [Map]
        specular_roughness_anisotropy_map("Specular Roughness Anisotropy Map", Texture2D) = "white" {} [Map]
        transmission_weight_map("Transmission Weight Map", Texture2D) = "white" {} [Map]
        transmission_color_map("Transmission Color Map", Texture2D) = "white" {} [Map]
        subsurface_weight_map("Subsurface Weight Map", Texture2D) = "white" {} [Map]
        subsurface_color_map("Subsurface Color Map", Texture2D) = "white" {} [Map]
        subsurface_radius_map("Subsurface Radius Map", Texture2D) = "white" {} [Map]
        coat_weight_map("Coat Weight Map", Texture2D) = "white" {} [Map]
        coat_color_map("Coat Color Map", Texture2D) = "white" {} [Map]
        coat_roughness_map("Coat Roughness Map", Texture2D) = "white" {} [Map]
        coat_roughness_anisotropy_map("Coat Roughness Anisotropy Map", Texture2D) = "white" {} [Map]
        coat_darkening_map("Coat Darkening Map", Texture2D) = "white" {} [Map]
        fuzz_weight_map("Fuzz Weight Map", Texture2D) = "white" {} [Map]
        fuzz_color_map("Fuzz Color Map", Texture2D) = "white" {} [Map]
        fuzz_roughness_map("Fuzz Roughness Map", Texture2D) = "white" {} [Map]
        emission_luminance_map("Emission Luminance Map", Texture2D) = "white" {} [Map]
        emission_color_map("Emission Color Map", Texture2D) = "white" {} [Map]
        thin_film_weight_map("Thin Film Weight Map", Texture2D) = "white" {} [Map]
        thin_film_thickness_map("Thin Film Thickness Map", Texture2D) = "white" {} [Map]
        geometry_opacity_map("Geometry Opacity Map", Texture2D) = "white" {} [Map]
        ambient_occlusion_map("Ambient Occlusion Map", Texture2D) = "white" {} [Map]
        geometry_normal_map("Geometry Normal Map", Texture2D) = "normal" {} [Map] [Normal]
        geometry_coat_normal_map("Geometry Coat Normal Map", Texture2D) = "normal" {} [Map] [Normal]
        geometry_tangent_map("Geometry Tangent Map", Texture2D) = "normal" {} [Map]
        map_mask("Map Mask", Integer) = 0 [MapMask] [HideInInspector]
    }

    Pass
    {
        Name "Forward"
        Tags { "LightMode" = "Forward" "Output" = "Premultiplied" "UnusedWhen" = "PBR_TIER LITE PBR_THIN_FILM PBR_ANISOTROPY" }
        GLSLPROGRAM
        Shared
        {
            #define GRAPHITE_STANDARD_FORWARD
            #pragma variant global PBR_TIER FULL LITE
            #pragma variant PBR_COAT
            #pragma variant PBR_FUZZ
            #pragma variant PBR_THIN_FILM
            #pragma variant PBR_ANISOTROPY
            #pragma variant PBR_SUBSURFACE
            #pragma variant PBR_THIN_WALLED
            #pragma surface_lighting
            #include "Graphite/Standard/Standard.glsl"
        }
        Vertex
        {
            #pragma surface_vertex
            #include "Graphite/Standard/StandardVertex.glsl"
            void main() { Graphite_StandardVertex(); }
        }
        Fragment
        {
            #pragma surface
            #include "Graphite/Standard/StandardFragment.glsl"
            void main() { Graphite_StandardFragment(); }
        }
        ENDGLSL
    }

    Pass
    {
        Name "Transmission"
        Tags { "LightMode" = "Transmission" "Output" = "Premultiplied" "SceneColor" = "Read" "SceneDepth" = "Read" "BackDepth" = "Read" "ReadsWhen" = "PBR_TIER FULL" "UnusedWhen" = "PBR_TIER LITE PBR_THIN_FILM PBR_ANISOTROPY" }
        GLSLPROGRAM
        Shared
        {
            #define GRAPHITE_STANDARD_FORWARD
            #define PBR_TRANSMISSION
            #pragma variant global PBR_TIER FULL LITE
            #pragma variant PBR_COAT
            #pragma variant PBR_FUZZ
            #pragma variant PBR_THIN_FILM
            #pragma variant PBR_ANISOTROPY
            #pragma variant PBR_SUBSURFACE
            #pragma variant PBR_THIN_WALLED
            #pragma surface_lighting
            #include "Graphite/Standard/Standard.glsl"
        }
        Vertex
        {
            #pragma surface_vertex
            #include "Graphite/Standard/StandardVertex.glsl"
            void main() { Graphite_StandardVertex(); }
        }
        Fragment
        {
            #pragma surface
            #include "Graphite/Standard/StandardFragment.glsl"
            void main() { Graphite_StandardFragment(); }
        }
        ENDGLSL
    }

    Pass
    {
        Name "BackDepth"
        Tags { "LightMode" = "BackDepth" }
        GLSLPROGRAM
        Shared
        {
            #define GRAPHITE_STANDARD_DEPTH
            #define GRAPHITE_STANDARD_BACK_DEPTH
            #include "Graphite/Standard/Standard.glsl"
        }
        Vertex
        {
            #pragma surface_vertex
            #include "Graphite/Standard/StandardVertex.glsl"
            void main() { Graphite_StandardVertex(); }
        }
        Fragment
        {
            #pragma surface
            #include "Graphite/Standard/StandardFragment.glsl"
            void main() { Graphite_StandardFragment(); }
        }
        ENDGLSL
    }

    Pass
    {
        Name "Prepass"
        Tags { "LightMode" = "Prepass" }
        GLSLPROGRAM
        Shared
        {
            #define GRAPHITE_STANDARD_DEPTH
            #include "Graphite/Standard/Standard.glsl"
        }
        Vertex
        {
            #pragma surface_vertex
            #include "Graphite/Standard/StandardVertex.glsl"
            void main() { Graphite_StandardVertex(); }
        }
        Fragment
        {
            #pragma surface
            #include "Graphite/Standard/StandardFragment.glsl"
            void main() { Graphite_StandardFragment(); }
        }
        ENDGLSL
    }

    Pass
    {
        Name "ShadowCaster"
        Tags { "LightMode" = "ShadowCaster" "UnusedWhen" = "PBR_THIN_WALLED false PBR_SUBSURFACE" }
        GLSLPROGRAM
        Shared
        {
            #define GRAPHITE_STANDARD_DEPTH
            #define GRAPHITE_STANDARD_SHADOW
            #pragma variant PBR_SUBSURFACE
            #pragma variant PBR_THIN_WALLED
            #pragma variant PBR_TRANSMISSION
            #include "Graphite/Standard/Standard.glsl"
        }
        Vertex
        {
            #pragma surface_vertex
            #include "Graphite/Standard/StandardVertex.glsl"
            void main() { Graphite_StandardVertex(); }
        }
        Fragment
        {
            #pragma surface
            #include "Graphite/Standard/StandardFragment.glsl"
            void main() { Graphite_StandardFragment(); }
        }
        ENDGLSL
    }

    Pass
    {
        Name "Debug"
        Tags { "LightMode" = "Debug" "Output" = "Premultiplied" "UnusedWhen" = "PBR_TIER LITE PBR_THIN_FILM PBR_ANISOTROPY" }
        GLSLPROGRAM
        Shared
        {
            #define GRAPHITE_STANDARD_FORWARD
            #define GRAPHITE_STANDARD_DEBUG
            #pragma variant global PBR_TIER FULL LITE
            #pragma variant PBR_COAT
            #pragma variant PBR_FUZZ
            #pragma variant PBR_THIN_FILM
            #pragma variant PBR_ANISOTROPY
            #pragma variant PBR_SUBSURFACE
            #pragma variant PBR_THIN_WALLED
            #pragma variant PBR_TRANSMISSION
            #pragma variant global GRAPHITE_DEBUG UNLIT ALBEDO NORMALS ROUGHNESS METALLIC AO EMISSION LIGHTING CASCADES LIGHTMAP_DENSITY DIFFUSE SPECULAR SPECULAR_ALBEDO THROUGHPUT DIRECT INDIRECT FURNACE COAT FUZZ THIN_FILM DARKENING SUBSURFACE BACK TRANSMISSION THICKNESS
            #pragma surface_lighting
            #include "Graphite/Standard/Standard.glsl"
        }
        Vertex
        {
            #pragma surface_vertex
            #include "Graphite/Standard/StandardVertex.glsl"
            void main() { Graphite_StandardVertex(); }
        }
        Fragment
        {
            #pragma surface
            #include "Graphite/Standard/StandardFragment.glsl"
            void main() { Graphite_StandardFragment(); }
        }
        ENDGLSL
    }

    Fallback "Graphite/Fallback"
}

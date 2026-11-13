// The "Foliage" surface model: Graphite/Lit's material for grass, leaves and bushes. A shader with Surface "Foliage" takes
// these passes and properties, defining void Surface(inout PBR_Params p, Graphite_SurfaceData i) as for "Lit" and
// optionally a Vertex block's SurfaceVertex, which bends with the world's wind through Graphite_WindSway. Its light adds
// a translucency lobe, light from behind passing through tinted by the diffuse albedo and translucency_color, and its
// frame leans toward up by foliage_normal_lean so cards and canopies light softly. Every pass draws both sides unless
// the material's surface settings say otherwise. It takes no Lighting block. See Graphite/Foliage/Foliage.glsl.
Shader "Graphite/Foliage"
{
    Properties
    {
        base_weight("Base Weight", Float) = 1.0 [Range(0, 1)] [Header("Base")]
        base_color("Base Color", Color) = (0.8, 0.8, 0.8, 1.0)
        base_metalness("Base Metalness", Float) = 0.0 [Range(0, 1)]
        base_diffuse_roughness("Base Diffuse Roughness", Float) = 0.0 [Range(0, 1)]
        specular_weight("Specular Weight", Float) = 0.3 [Range(0, 1)] [Header("Specular")]
        specular_color("Specular Color", Color) = (1.0, 1.0, 1.0, 1.0)
        specular_roughness("Specular Roughness", Float) = 0.6 [Range(0, 1)]
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
        ambient_occlusion_map_strength("Ambient Occlusion Map Strength", Float) = 1.0 [Range(0, 1)]
        normal_scale("Normal Scale", Float) = 1.0
        coat_normal_scale("Coat Normal Scale", Float) = 1.0
        height_scale("Height Scale", Float) = 0.0 [Range(0, 0.2)] [Toggle(PBR_PARALLAX)]
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
        geometry_height_map("Geometry Height Map", Texture2D) = "white" {} [Map]
        translucency("Translucency", Float) = 0.6 [Range(0, 2)] [Header("Foliage")]
        translucency_color("Translucency Color", Color) = (1.0, 1.0, 0.8, 1.0)
        foliage_normal_lean("Normal Lean", Float) = 0.4 [Range(0, 1)]
    }

    Pass
    {
        Name "Forward"
        Tags { "LightMode" = "Forward" "Output" = "Premultiplied" }
        Cull Off
        GLSLPROGRAM
        Shared
        {
            #define GRAPHITE_STANDARD_FORWARD
            #define GRAPHITE_CUSTOM_BRDF
            #define GRAPHITE_STANDARD_INPUTS Foliage_Inputs
            #pragma variant global PBR_TIER FULL LITE
            #pragma variant PBR_SUBSURFACE
            #pragma variant PBR_THIN_WALLED
            #include "Graphite/Standard/PBRSurface.glsl"
            #include "Graphite/Wind.glsl"
        }
        Vertex
        {
            #pragma surface_vertex
            #include "Graphite/Standard/StandardVertex.glsl"
            void main() { Graphite_StandardVertex(); }
        }
        Fragment
        {
            #include "Graphite/Foliage/Foliage.glsl"
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
        Cull Off
        GLSLPROGRAM
        Shared
        {
            #define GRAPHITE_STANDARD_DEPTH
            #define GRAPHITE_STANDARD_INPUTS Foliage_Inputs
            #pragma variant global GRAPHITE_PREPASS DEPTH MOTION NORMALS
            #pragma variant global GRAPHITE_LOD_FADE
            #include "Graphite/Standard/PBRSurface.glsl"
            #include "Graphite/Wind.glsl"
        }
        Vertex
        {
            #pragma surface_vertex
            #include "Graphite/Standard/StandardVertex.glsl"
            void main() { Graphite_StandardVertex(); }
        }
        Fragment
        {
            #include "Graphite/Foliage/Foliage.glsl"
            #pragma surface
            #include "Graphite/Standard/StandardFragment.glsl"
            void main() { Graphite_StandardFragment(); }
        }
        ENDGLSL
    }

    Pass
    {
        Name "ShadowCaster"
        Tags { "LightMode" = "ShadowCaster" }
        Cull Off
        GLSLPROGRAM
        Shared
        {
            #define GRAPHITE_STANDARD_DEPTH
            #define GRAPHITE_STANDARD_SHADOW
            #define GRAPHITE_STANDARD_INPUTS Foliage_Inputs
            #include "Graphite/Standard/PBRSurface.glsl"
            #include "Graphite/Wind.glsl"
        }
        Vertex
        {
            #pragma surface_vertex
            #include "Graphite/Standard/StandardVertex.glsl"
            void main() { Graphite_StandardVertex(); }
        }
        Fragment
        {
            #include "Graphite/Foliage/Foliage.glsl"
            #pragma surface
            #include "Graphite/Standard/StandardFragment.glsl"
            void main() { Graphite_StandardFragment(); }
        }
        ENDGLSL
    }

    Pass
    {
        Name "Voxelize"
        Tags { "LightMode" = "Voxelize" }
        Cull Off
        GLSLPROGRAM
        Shared
        {
            #define GRAPHITE_STANDARD_VOXEL
            #define GRAPHITE_STANDARD_INPUTS Foliage_Inputs
            #include "Graphite/Standard/PBRSurface.glsl"
            #include "Graphite/Wind.glsl"
        }
        Vertex
        {
            #pragma surface_vertex
            #include "Graphite/Standard/StandardVertex.glsl"
            void main() { Graphite_StandardVertex(); }
        }
        Fragment
        {
            #include "Graphite/Foliage/Foliage.glsl"
            #pragma surface
            #include "Graphite/Standard/StandardFragment.glsl"
            void main() { Graphite_StandardFragment(); }
        }
        ENDGLSL
    }

    Pass
    {
        Name "Impostor"
        Tags { "LightMode" = "Impostor" }
        Cull Off
        GLSLPROGRAM
        Shared
        {
            #define GRAPHITE_STANDARD_IMPOSTOR
            #define GRAPHITE_STANDARD_INPUTS Foliage_Inputs
            #include "Graphite/Standard/PBRSurface.glsl"
            #include "Graphite/Wind.glsl"
        }
        Vertex
        {
            #pragma surface_vertex
            #include "Graphite/Standard/StandardVertex.glsl"
            void main() { Graphite_StandardVertex(); }
        }
        Fragment
        {
            #include "Graphite/Foliage/Foliage.glsl"
            #pragma surface
            #include "Graphite/Standard/StandardFragment.glsl"
            void main() { Graphite_StandardFragment(); }
        }
        ENDGLSL
    }

    Pass
    {
        Name "Debug"
        Tags { "LightMode" = "Debug" "Output" = "Premultiplied" }
        Cull Off
        GLSLPROGRAM
        Shared
        {
            #define GRAPHITE_STANDARD_FORWARD
            #define GRAPHITE_STANDARD_DEBUG
            #define GRAPHITE_CUSTOM_BRDF
            #define GRAPHITE_STANDARD_INPUTS Foliage_Inputs
            #pragma variant global PBR_TIER FULL LITE
            #pragma variant PBR_SUBSURFACE
            #pragma variant PBR_THIN_WALLED
            #pragma variant global GRAPHITE_DEBUG UNLIT ALBEDO NORMALS ROUGHNESS METALLIC AO EMISSION LIGHTING CASCADES LIGHTMAP_DENSITY DIFFUSE SPECULAR SPECULAR_ALBEDO THROUGHPUT DIRECT INDIRECT FURNACE COAT FUZZ THIN_FILM DARKENING SUBSURFACE BACK TRANSMISSION THICKNESS
            #include "Graphite/Standard/PBRSurface.glsl"
            #include "Graphite/Wind.glsl"
        }
        Vertex
        {
            #pragma surface_vertex
            #include "Graphite/Standard/StandardVertex.glsl"
            void main() { Graphite_StandardVertex(); }
        }
        Fragment
        {
            #include "Graphite/Foliage/Foliage.glsl"
            #pragma surface
            #include "Graphite/Standard/StandardFragment.glsl"
            void main() { Graphite_StandardFragment(); }
        }
        ENDGLSL
    }

    Fallback "Graphite/Fallback"
}

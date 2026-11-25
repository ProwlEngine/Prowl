// Graphite's physically based surface, OpenPBR Surface v1.1.1: every spec parameter under its spec name, engine
// extensions under plain names. Each texturable input X has a map X_map, whose channel, UV set, scale, rotation and offset
// the material sets; the input is X times the map wherever the map is set. tangent_rotation turns geometry_tangent
// counter clockwise in radians after its map, as KHR_materials_anisotropy's anisotropyRotation. See Graphite/PBR/Closure.glsl.
// The coat, fuzz, subsurface and anisotropy lobes are surface models of their own built on this one: ClearCoat, Cloth,
// Subsurface and Anisotropic. PBR_THIN_WALLED follows geometry_thin_walled, and while it is
// on the material draws both sides whatever its SurfaceCull, back faces shaded with the normal flipped.
// PBR_TRANSMISSION follows transmission_weight. While it is on the material draws as transparent whatever its surface,
// through the Transmission pass in place of Forward, which reads the opaque colour and depth behind it and, for volumes,
// the back depth the BackDepth pass draws. LITE views read none of them. See Graphite/PBR/Transmission.glsl.
// PBR_PARALLAX follows height_scale. While it is on, geometry_height_map moves both UV sets of every pass before any map
// is read, the depth passes included, so cutouts match. See PBR_Parallax in Graphite/Standard/PBRSurface.glsl.
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
        specular_ior("Specular IOR", Float) = 1.5 [Range(1, 3)]
        transmission_weight("Transmission Weight", Float) = 0.0 [Range(0, 1)] [Header("Transmission")] [Toggle(PBR_TRANSMISSION)] [Transmissive]
        transmission_color("Transmission Color", Color) = (1.0, 1.0, 1.0, 1.0)
        transmission_depth("Transmission Depth", Float) = 0.0
        transmission_scatter("Transmission Scatter", Color) = (0.0, 0.0, 0.0, 1.0)
        transmission_scatter_anisotropy("Transmission Scatter Anisotropy", Float) = 0.0 [Range(-1, 1)]
        transmission_dispersion_scale("Transmission Dispersion Scale", Float) = 0.0 [Range(0, 1)]
        transmission_dispersion_abbe_number("Transmission Dispersion Abbe Number", Float) = 20.0 [Range(9, 91)]
        emission_luminance("Emission Luminance", Float) = 0.0 [Header("Emission")]
        emission_color("Emission Color", Color) = (1.0, 1.0, 1.0, 1.0) [HDR]
        geometry_opacity("Geometry Opacity", Float) = 1.0 [Range(0, 1)] [Header("Geometry")]
        geometry_thin_walled("Geometry Thin Walled", Integer) = 0 [Enum(Off, On)] [Toggle(PBR_THIN_WALLED)] [TwoSided]
        ambient_occlusion("Ambient Occlusion", Float) = 1.0 [Range(0, 1)]
        ambient_occlusion_map_strength("Ambient Occlusion Map Strength", Float) = 1.0 [Range(0, 1)]
        normal_scale("Normal Scale", Float) = 1.0
        height_scale("Height Scale", Float) = 0.0 [Range(0, 0.2)] [Toggle(PBR_PARALLAX)]
        alpha_cutoff("Alpha Cutoff", Float) = 0.5 [Range(0, 1)] [ShowIf(_SURFACE, CUTOUT)]

        base_weight_map("Base Weight Map", Texture2D) = "white" {} [Map] [Header("Maps")]
        base_color_map("Base Color Map", Texture2D) = "white" {} [Map]
        base_metalness_map("Base Metalness Map", Texture2D) = "white" {} [Map]
        base_diffuse_roughness_map("Base Diffuse Roughness Map", Texture2D) = "white" {} [Map]
        specular_weight_map("Specular Weight Map", Texture2D) = "white" {} [Map]
        specular_color_map("Specular Color Map", Texture2D) = "white" {} [Map]
        specular_roughness_map("Specular Roughness Map", Texture2D) = "white" {} [Map]
        transmission_weight_map("Transmission Weight Map", Texture2D) = "white" {} [Map]
        transmission_color_map("Transmission Color Map", Texture2D) = "white" {} [Map]
        emission_luminance_map("Emission Luminance Map", Texture2D) = "white" {} [Map]
        emission_color_map("Emission Color Map", Texture2D) = "white" {} [Map]
        geometry_opacity_map("Geometry Opacity Map", Texture2D) = "white" {} [Map]
        ambient_occlusion_map("Ambient Occlusion Map", Texture2D) = "white" {} [Map]
        geometry_normal_map("Geometry Normal Map", Texture2D) = "normal" {} [Map] [Normal]
        geometry_height_map("Geometry Height Map", Texture2D) = "white" {} [Map]
    }

    Pass
    {
        Name "Forward"
        Tags { "LightMode" = "Forward" "Output" = "Premultiplied" }
        GLSLPROGRAM
        Shared
        {
            #define GRAPHITE_STANDARD_FORWARD
            #pragma variant global PBR_TIER FULL LITE MAX
            #pragma variant PBR_THIN_WALLED
            #pragma variant PBR_PARALLAX
            #pragma surface_lighting
            #include "Graphite/Standard/PBRSurface.glsl"
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
        Name "GBuffer"
        Tags { "LightMode" = "GBuffer" "Discards" = "_SURFACE CUTOUT" }
        GLSLPROGRAM
        Shared
        {
            #define GRAPHITE_STANDARD_FORWARD
            #define GRAPHITE_STANDARD_GBUFFER
            #pragma variant global GRAPHITE_LOD_FADE
            #pragma variant PBR_PARALLAX
            #include "Graphite/Standard/PBRSurface.glsl"
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
        Tags { "LightMode" = "Transmission" "Output" = "Premultiplied" "SceneColor" = "Read" "SceneDepth" = "Read" "BackDepth" = "Read" "ReadsWhen" = "PBR_TIER FULL MAX" }
        GLSLPROGRAM
        Shared
        {
            #define GRAPHITE_STANDARD_FORWARD
            #define PBR_TRANSMISSION
            #pragma variant global PBR_TIER FULL LITE MAX
            #pragma variant PBR_THIN_WALLED
            #pragma variant PBR_PARALLAX
            #pragma surface_lighting
            #include "Graphite/Standard/PBRSurface.glsl"
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
            #pragma variant PBR_PARALLAX
            #include "Graphite/Standard/PBRSurface.glsl"
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
            #pragma variant global GRAPHITE_LOD_FADE
            #pragma variant PBR_PARALLAX
            #include "Graphite/Standard/PBRSurface.glsl"
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
        Tags { "LightMode" = "ShadowCaster" }
        GLSLPROGRAM
        Shared
        {
            #define GRAPHITE_STANDARD_DEPTH
            #define GRAPHITE_STANDARD_SHADOW
            #pragma variant PBR_THIN_WALLED
            #pragma variant PBR_TRANSMISSION
            #pragma variant PBR_PARALLAX
            #include "Graphite/Standard/PBRSurface.glsl"
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
        Name "Voxelize"
        Tags { "LightMode" = "Voxelize" }
        GLSLPROGRAM
        Shared
        {
            #define GRAPHITE_STANDARD_VOXEL
            #include "Graphite/Standard/PBRSurface.glsl"
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
        Name "Impostor"
        Tags { "LightMode" = "Impostor" }
        GLSLPROGRAM
        Shared
        {
            #define GRAPHITE_STANDARD_IMPOSTOR
            #pragma variant PBR_PARALLAX
            #include "Graphite/Standard/PBRSurface.glsl"
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

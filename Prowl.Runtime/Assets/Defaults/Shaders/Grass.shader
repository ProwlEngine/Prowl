// Terrain grass: Graphite's Lit surface on blades the vertex stage scatters across the terrain, see Prowl/TerrainScatter.glsl.
// Blades are thin walled, so the light through them is OpenPBR's thin walled subsurface, tinted by the blade's colour.
// The detail renderer draws it cut out against the texture's alpha.
Shader "Default/Grass"
{
    Properties
    {
        base_color("Base Color", Color) = (1.0, 1.0, 1.0, 1.0)
        specular_roughness("Specular Roughness", Float) = 0.9 [Range(0, 1)]
        subsurface_weight("Subsurface Weight", Float) = 0.5 [Range(0, 1)] [Header("Subsurface")] [Toggle(PBR_SUBSURFACE)]
        subsurface_color("Subsurface Color", Color) = (1.0, 1.0, 1.0, 1.0)
        subsurface_scatter_anisotropy("Subsurface Scatter Anisotropy", Float) = 0.3 [Range(-1, 1)]
        geometry_thin_walled("Geometry Thin Walled", Integer) = 1 [Enum(Off, On)] [Toggle(PBR_THIN_WALLED)] [TwoSided]

        _WindStrength("Wind Strength", Float) = 0.3 [Header("Grass")]
        _WindSpeed("Wind Speed", Float) = 1.5
        _Billboard("Billboard", Float) = 1.0
        _AlignToNormal("Align To Normal", Float) = 0.0

        _Heightmap("Heightmap", Texture2D) = "black"
        _TerrainSize("Terrain Size", Float) = 1024.0
        _TerrainHeight("Terrain Height", Float) = 100.0
        _HolesMap("Holes Map", Texture2D) = "white"
        _HasHoles("Has Holes", Integer) = 0
        _DetailMap("Detail Map", Texture2D) = "black"
        _DetailChannel("Detail Channel", Integer) = 0
        _ScatterCellSize("Scatter Cell Size", Float) = 0.5
        _ScatterMaxLevel("Scatter Max Level", Integer) = 0

        _ProtoSize("Prototype Size", Vector) = (1.0, 2.0, 1.0, 2.0)
        _ProtoHealthyColor("Healthy Color", Color) = (0.26, 0.97, 0.16, 1.0)
        _ProtoDryColor("Dry Color", Color) = (0.80, 0.73, 0.10, 1.0)
        _ProtoNoiseSpread("Noise Spread", Float) = 0.1
        _ProtoBendFactor("Bend Factor", Float) = 0.5

        _WindZoneCount("Wind Zone Count", Integer) = 0
        _WindZoneSphere0("Wind Zone 0 Sphere", Vector) = (0.0, 0.0, 0.0, 0.0)
        _WindZoneSphere1("Wind Zone 1 Sphere", Vector) = (0.0, 0.0, 0.0, 0.0)
        _WindZoneSphere2("Wind Zone 2 Sphere", Vector) = (0.0, 0.0, 0.0, 0.0)
        _WindZoneSphere3("Wind Zone 3 Sphere", Vector) = (0.0, 0.0, 0.0, 0.0)
        _WindZoneParams0("Wind Zone 0 Params", Vector) = (0.0, 0.0, 0.0, 0.0)
        _WindZoneParams1("Wind Zone 1 Params", Vector) = (0.0, 0.0, 0.0, 0.0)
        _WindZoneParams2("Wind Zone 2 Params", Vector) = (0.0, 0.0, 0.0, 0.0)
        _WindZoneParams3("Wind Zone 3 Params", Vector) = (0.0, 0.0, 0.0, 0.0)
    }

    Surface "Lit"
    {
        GLSLPROGRAM
        void Surface(inout PBR_Params p, Graphite_SurfaceData i)
        {
            p.subsurfaceColor *= p.baseColor;
        }
        ENDGLSL

        Vertex
        {
            GLSLPROGRAM
            #include "Prowl/TerrainScatter.glsl"

            void SurfaceVertex(inout Graphite_SurfaceVertex v)
            {
                grassVertex(v);
            }
            ENDGLSL
        }
    }
}

// Leaves and needles on the Foliage surface model, for generated or modelled trees and bushes: the base colour map
// cut out by its alpha, bowing with the world's wind more the higher up the plant, so crowns sway while trunks stand.
Shader "Graphite/Foliage/Leaves"
{
    Properties
    {
        base_color("Base Color", Color) = (1.0, 1.0, 1.0, 1.0)
        base_color_map("Base Color Map", Texture2D) = "white" {} [Map]
        translucency("Translucency", Float) = 0.6 [Range(0, 2)]
        translucency_color("Translucency Color", Color) = (1.0, 1.0, 0.8, 1.0)
        foliage_normal_lean("Normal Lean", Float) = 0.2 [Range(0, 1)]
        wind_bend("Wind Bend", Float) = 0.003 [Range(0, 0.05)]
    }

    Surface "Foliage"
    {
        GLSLPROGRAM
        void Surface(inout PBR_Params p, Graphite_SurfaceData i) { }
        ENDGLSL

        Vertex
        {
            GLSLPROGRAM
            uniform float wind_bend;

            void SurfaceVertex(inout Graphite_SurfaceVertex v)
            {
                float rise = max(v.position.y, 0.0);
                vec3 sway = Graphite_WindSway(Graphite_ObjectToRelative(v.position), v.time, min(wind_bend * rise * rise, 0.6));
                v.position += inverse(Graphite_Linear(Graphite_ObjectTransform())) * sway;
            }
            ENDGLSL
        }
    }
}

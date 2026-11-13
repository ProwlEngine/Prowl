// TerrainSurface's material when it is given none: its layers from the material cache, or blended here by the control
// pages when the cache is off, see Graphite/Terrain/TerrainMaterial.glsl, or plain grey without layers. _TerrainDebug 1
// colours each node by its level, brighter as it morphs toward the next, 2 each pixel by its strongest layer, 3 the four
// blended layer weights. The last two blend the layers here to show them.
Shader "Graphite/Terrain/Default"
{
    Properties
    {
        base_color("Base Color", Color) = (0.45, 0.45, 0.45, 1.0)
        specular_roughness("Specular Roughness", Float) = 0.85 [Range(0, 1)]
        _TerrainDebug("Debug", Float) = 0.0 [Range(0, 3)]
    }

    Surface "Terrain"
    {
        GLSLPROGRAM
        uniform float _TerrainDebug;

        vec3 terrain_Hue(float h) { return clamp(abs(fract(h + vec3(0.0, 0.667, 0.333)) * 6.0 - 3.0) - 1.0, 0.0, 1.0); }

        void Surface(inout PBR_Params p, Graphite_SurfaceData i)
        {
            Terrain_Material m = _TerrainCacheParams.z > 0.5 && _TerrainDebug < 1.5 ? Terrain_CachedMaterial(i.geometryNormal) : Terrain_EvaluateMaterial(i.geometryNormal);
            if (_TerrainSpacing.y > 0.5)
            {
                p.baseColor = m.albedo;
                p.specularRoughness = m.roughness;
                p.geometryNormal = m.normal;
                p.occlusion *= m.occlusion;
            }
            if (_TerrainDebug > 2.5)
                p.baseColor = m.weights.rgb + m.weights.a;
            else if (_TerrainDebug > 1.5)
                p.baseColor = terrain_Hue(float(m.strongest) * 0.27);
            else if (_TerrainDebug > 0.5)
                p.baseColor = mix(terrain_Hue(Terrain_Level() * 0.27), vec3(1.0), Terrain_Morph() * 0.6);
        }
        ENDGLSL
    }
}

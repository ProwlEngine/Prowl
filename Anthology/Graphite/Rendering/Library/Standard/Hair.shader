// Hair strands and cards on the Lit surface, lit along the strand by Graphite/PBR/Hair.glsl: base_color is the hair's
// colour, specular_roughness how wide its highlights are. A strand runs along the mesh's tangent, turned toward its
// bitangent by hair_strand_rotation in radians. Light from behind the surface reaches it, as it passes through hair.
ShadingModel "Hair"
{
    Properties
    {
        hair_strand_rotation("Strand Rotation", Float) = 0.0 [Header("Hair")]
        hair_backlit("Backlit", Float) = 1.0 [Range(0, 1)]
        hair_scatter("Scatter", Float) = 0.5 [Range(0, 1)]
    }

    Base "Lit"
    LightWraps

    Pack
    {
        GLSLPROGRAM
        uniform float hair_strand_rotation;
        uniform float hair_backlit;
        uniform float hair_scatter;

        void Model_Surface(inout PBR_Params p, Graphite_SurfaceData i)
        {
            vec3 strand = normalize(i.tangent * cos(hair_strand_rotation) + i.bitangent * sin(hair_strand_rotation));
            p.custom = vec4(Graphite_OctEncode(strand) * 0.5 + 0.5, hair_backlit, hair_scatter);
        }

        vec4 Model_Pack(PBR_Params p) { return p.custom; }
        ENDGLSL
    }

    Lighting
    {
        GLSLPROGRAM
        #include "Graphite/PBR/Hair.glsl"

        vec3 Model_Light(PBR_Closure c, Graphite_LightContribution l, vec4 custom)
        {
            return PBR_HairLight(c, l, Graphite_OctDecode(custom.xy * 2.0 - 1.0), custom.z, custom.w);
        }

        vec3 Model_Ambient(PBR_Closure c, PBR_Indirect i, vec4 custom) { return PBR_HairAmbient(c, i, custom.w); }
        ENDGLSL
    }
}

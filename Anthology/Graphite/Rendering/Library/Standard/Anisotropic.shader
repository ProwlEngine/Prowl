// The Lit surface with its highlight stretched along the tangent, for brushed metal: the anisotropy of
// Graphite/PBR/Closure.glsl. It needs the tangent, which a GBuffer does not hold, so it is lit as it is drawn in every view.
ShadingModel "Anisotropic"
{
    Properties
    {
        specular_roughness_anisotropy("Specular Roughness Anisotropy", Float) = 0.0 [Range(0, 1)]
        tangent_rotation("Tangent Rotation", Float) = 0.0
        specular_roughness_anisotropy_map("Specular Roughness Anisotropy Map", Texture2D) = "white" {} [Map]
        geometry_tangent_map("Geometry Tangent Map", Texture2D) = "normal" {} [Map]
    }

    Base "Lit"
    Define "PBR_ANISOTROPY"
    ForwardOnly
}

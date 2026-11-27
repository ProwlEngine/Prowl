// The Lit surface with the subsurface lobe of Graphite/PBR/Closure.glsl whole, for thin walled sheets such as paper and
// leaves that light passes through: with geometry_thin_walled it reflects and transmits as a sheet. Lighting from
// behind is not something a GBuffer holds, so it is lit as it is drawn in every view.
ShadingModel "ThinSubsurface"
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
    Define "PBR_SUBSURFACE"
    ForwardOnly
}

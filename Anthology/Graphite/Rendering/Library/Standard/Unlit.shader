// Graphite's standard unlit surface: a texture times a tint and the vertex colour, fogged.
Shader "Graphite/Unlit"
{
    Properties
    {
        _MainTex("Texture", Texture2D) = "white" {}
        _MainColor("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _MainTexUV("UV Set", Integer) = 0 [Enum(UV0, UV1)]
        _AlphaCutoff("Alpha Cutoff", Float) = 0.5 [Range(0, 1)] [ShowIf(_SURFACE, CUTOUT)]
        _Tiling("Tiling", Vector2) = (1.0, 1.0)
        _Offset("Offset", Vector2) = (0.0, 0.0)
        _InstanceTint("Instance Tint", Integer) = 0 [Toggle(_INSTANCE_TINT)]
    }

    Surface "Unlit" { }
}

Shader "Default/GameUI"
{
    Properties
    {
        _MainTex("Texture", Texture2D) = "white" {}
        _MainColor("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _Tiling("Tiling", Vector) = (1.0, 1.0, 0, 0)
        _Offset("Offset", Vector) = (0.0, 0.0, 0, 0)
    }

    Pass
    {
        Name "GameUI"
        Tags { "RenderOrder" = "UI" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZTest Disabled
        ZWrite Off
        Cull Off

        SLANGPROGRAM

        import ProwlCG;
        import VertexAttributes;

        struct VertexInput
        {
            float3 position : POSITION0;
            float2 uv0 : TEXCOORD0;
            float4 color : COLOR0;
            uint vid : SV_VertexID;
        }

        struct Varyings
        {
            float4 position : SV_Position;
            float2 texCoord0 : TEXCOORD0;
            float3 worldPos : TEXCOORD1;
            float4 vColor : COLOR0;
        }

        struct Material
        {
            float2 _Tiling;
            float2 _Offset;
            float4 _MainColor;
            Sampler2D<float4> _MainTex;
        }
        ParameterBlock<Material> Mat;

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings o;
            o.position = TransformClip(input.position, input.vid);
            o.texCoord0 = input.uv0 * Mat._Tiling + Mat._Offset;
            o.worldPos = TransformPosition(input.position, input.vid);
            o.vColor = GetInstanceColor(input.color);
            return o;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float4 albedo = Mat._MainTex.Sample(input.texCoord0) * input.vColor * Mat._MainColor;
            return albedo;
        }

        ENDSLANG
    }
}

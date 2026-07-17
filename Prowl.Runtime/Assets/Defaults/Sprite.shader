Shader "Default/Sprite"
{
    Properties
    {
        _MainTex ("Sprite Texture", Texture2D) = "white" {}
        _MainColor ("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _Tiling ("Tiling", Vector) = (1.0, 1.0, 0.0, 0.0)
        _Offset ("Offset", Vector) = (0.0, 0.0, 0.0, 0.0)
    }

    Pass
    {
        Name "Sprite"
        Tags { "RenderOrder" = "Transparent" }

        Blend SourceAlpha InverseSourceAlpha
        ZWrite Off
        Cull Off

        SLANGPROGRAM
        import ProwlCG;
        import Lighting;

        struct MaterialData
        {
            Sampler2D<float4> _MainTex;
            float4 _MainColor;
            float2 _Tiling;
            float2 _Offset;
        }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput
        {
            float3 position : POSITION0;
            float2 uv : TEXCOORD0;
            float4 color : COLOR0;
        }
        struct Varyings
        {
            float4 position : SV_Position;
            float2 texCoord0 : TEXCOORD0;
            float3 worldPos : TEXCOORD1;
            float4 vColor : COLOR0;
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            output.position = mul(Object.mvp, float4(input.position, 1.0));
            output.texCoord0 = input.uv * Mat._Tiling + Mat._Offset;
            output.worldPos = mul(Object.prowl_ObjectToWorld, float4(input.position, 1.0)).xyz;
            output.vColor = input.color;
            return output;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float4 albedo = Mat._MainTex.Sample(input.texCoord0) * input.vColor * Mat._MainColor;
            float3 baseColor = gammaToLinearSpace(albedo.rgb);
            baseColor = ApplyFog(baseColor, input.worldPos);
            return float4(baseColor, albedo.a);
        }
        ENDSLANG
    }
}

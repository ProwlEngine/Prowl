Shader "Default/Blit"
{
    Pass
    {
        Name "Blit"
        Tags { "RenderOrder" = "Opaque" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZTest Disabled
        ZWrite Off

        SLANGPROGRAM

        struct VertexInput
        {
            float3 position : POSITION0;
            float2 uv : TEXCOORD0;
        }

        struct Varyings
        {
            float4 position : SV_Position;
            float2 uv : TEXCOORD0;
        }

        struct Material
        {
            Sampler2D<float4> _MainTex;
        }
        ParameterBlock<Material> Mat;

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            output.position = float4(input.position, 1.0);
            output.uv = input.uv;
            return output;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            return Mat._MainTex.Sample(input.uv);
        }

        ENDSLANG
    }
}

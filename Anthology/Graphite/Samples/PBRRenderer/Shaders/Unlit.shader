Shader "PBRRenderer/Unlit"
{
    Properties
    {
        _AlbedoTex ("Albedo", Texture2D) = "white" {}
        _BaseColor ("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
    }

    Pass
    {
        Name "Unlit"
        Cull Off
        ZTest LessEqual
        ZWrite On

        SLANGPROGRAM
        import UVOrigin;

        struct MaterialData
        {
            float4x4 MatrixMVP;
            float4 BaseColor;
            Sampler2D<float4> AlbedoTexture;
        }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput
        {
            float3 position : POSITION0;
            float3 normal : NORMAL0;
            float4 uv : UV0;
        }

        struct Varyings
        {
            float4 position : SV_Position;
            float2 uv : TEXCOORD0;
            float3 normal : TEXCOORD1;
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            output.position = mul(Mat.MatrixMVP, float4(input.position, 1.0));
            float2 uv = input.uv.xy;
            if (IsUVOriginTopLeft)
                uv.y = 1.0 - uv.y;
            output.uv = uv;
            output.normal = input.normal;
            return output;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float4 albedo = Mat.AlbedoTexture.Sample(input.uv);
            return albedo * Mat.BaseColor;
        }
        ENDSLANG
    }
}

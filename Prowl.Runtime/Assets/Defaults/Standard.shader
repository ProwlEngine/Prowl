Shader "Default/Standard"
{
    Pass
    {
        Name "Standard"
        Tags { "RenderType" = "Opaque" }
        Cull Off

        SLANGPROGRAM
        import ProwlCG;

        struct VertexInput { float3 position : POSITION0; }
        struct Varyings { float4 position : SV_Position; }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            output.position = mul(Frame.prowl_MatVP, float4(input.position, 1.0));
            return output;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            return float4(1.0, 0.0, 1.0, 1.0);
        }
        ENDSLANG
    }
}

Shader "Sample/Tonemap"
{
    Pass
    {
        Name "Post"

        Cull Off
        ZTest Disabled
        ZWrite Off

        SLANGPROGRAM
        import UVOrigin;

        uniform Sampler2D Source;
        uniform float Exposure;

        struct Varyings
        {
            float4 position : SV_Position;
            float2 uv : TEXCOORD0;
        };

        [shader("vertex")]
        Varyings Vertex(uint vertexIndex : SV_VertexID)
        {
            Varyings output;
            float2 p = float2(float((vertexIndex << 1) & 2), float(vertexIndex & 2));
            output.uv = IsUVOriginTopLeft ? float2(p.x, 1.0 - p.y) : p;
            output.position = float4(p * 2.0 - 1.0, 0.0, 1.0);
            return output;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float3 hdr = Source.Sample(input.uv).rgb * Exposure;
            float3 aces = (hdr * (2.51 * hdr + 0.03)) / (hdr * (2.43 * hdr + 0.59) + 0.14);
            return float4(pow(saturate(aces), 1.0 / 2.2), 1.0);
        }
        ENDSLANG
    }
}

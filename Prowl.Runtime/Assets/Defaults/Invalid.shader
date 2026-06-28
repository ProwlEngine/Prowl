Shader "Default/Fallback"
{
    Pass 0
    {
        SLANGPROGRAM

        [shader("vertex")]
        float4 Vertex(float3 position : POSITION) : SV_Position
        {
            return float4(position.xyz, 1.0);
        }

        [shader("fragment")]
        float4 Fragment() : SV_Target
        {
            return float4(1.0, 1.0, 0.0, 0.0);
        }

        ENDSLANG
    }
}
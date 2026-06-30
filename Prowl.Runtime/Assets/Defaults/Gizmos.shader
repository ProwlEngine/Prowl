Shader "Default/Gizmos"
{
    Pass
    {
        Name "Gizmos"
        Tags { "RenderOrder" = "Opaque" }

        Cull Off
        Blend SourceAlpha InverseSourceAlpha
        ZWrite Off
        ZTest Always

        SLANGPROGRAM
        import ProwlCG;

        struct MaterialData { Sampler2D<float4> _CameraDepthTexture; }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput
        {
            float3 position : POSITION0;
            float4 color : COLOR0;
        }
        struct Varyings
        {
            float4 position : SV_Position;
            float4 vColor : COLOR0;
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            output.position = mul(Frame.prowl_MatVP, float4(input.position, 1.0));
            output.vColor = input.color;
            return output;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float2 screenUV = input.position.xy / Frame._ScreenParams.xy;
            float sceneDepth = Mat._CameraDepthTexture.Sample(screenUV).r;
            float fragmentDepth = input.position.z;

            float occluded = step(sceneDepth, fragmentDepth - 0.00001);

            float4 color = input.vColor;
            if (occluded > 0.5)
            {
                color.rgb *= 0.5;
                color.a *= 0.3;
            }

            return color;
        }
        ENDSLANG
    }
}

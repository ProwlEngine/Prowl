Shader "Default/Gizmos"
{
    Pass
    {
        Name "Gizmos"
        Tags { "RenderOrder" = "Opaque" }
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always

        SLANGPROGRAM

        import ProwlCG;

        struct VertexInput
        {
            float3 position : POSITION0;
            float4 color : COLOR0;
        }

        struct Varyings
        {
            float4 position : SV_Position;
            float4 vColor : COLOR0;
            float4 screenPos : TEXCOORD0;
        }

        struct Material
        {
            Sampler2D<float4> _CameraDepthTexture;
        }
        ParameterBlock<Material> Mat;

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings o;
            o.position = mul(Frame.prowl_MatVP, float4(input.position, 1.0));
            o.vColor = input.color;
            o.screenPos = o.position;
            return o;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            // Screen UV from SV_Position (already in pixel coordinates)
            float2 screenUV = input.position.xy / Frame._ScreenParams.xy;

            // Sample the depth buffer at this fragment's screen position
            float sceneDepth = Mat._CameraDepthTexture.Sample(screenUV).r;

            // input.position.z is in the same depth space as the depth buffer
            float fragmentDepth = input.position.z;

            // If fragmentDepth > sceneDepth, it's occluded
            float occluded = step(sceneDepth, fragmentDepth - 0.00001); // epsilon to avoid z-fighting

            float4 color = input.vColor;
            if (occluded > 0.5)
            {
                color.rgb *= 0.5; // Darken to 50%
                color.a *= 0.3;   // Make 70% transparent
            }

            return color;
        }

        ENDSLANG
    }
}

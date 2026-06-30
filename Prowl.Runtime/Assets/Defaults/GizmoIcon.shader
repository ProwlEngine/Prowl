Shader "Default/GizmoIcon"
{
    Properties
    {
        _MainTex ("Icon", Texture2D) = "white" {}
        _IconColor ("Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _IconCenter ("Center", Vector) = (0.0, 0.0, 0.0, 0.0)
        _IconScale ("Scale", Float) = 1.0
    }

    Pass
    {
        Name "GizmoIcon"
        Tags { "RenderOrder" = "Transparent" }

        Cull Off
        Blend SourceAlpha InverseSourceAlpha
        ZWrite Off
        ZTest Always

        SLANGPROGRAM
        import ProwlCG;

        struct MaterialData
        {
            Sampler2D<float4> _MainTex;
            float4 _IconColor;
            float3 _IconCenter;
            float _IconScale;
            Sampler2D<float4> _CameraDepthTexture;
        }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput { float3 position : POSITION0; }
        struct Varyings { float4 position : SV_Position; float2 vUV : TEXCOORD0; }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            float halfSize = Mat._IconScale * 0.5;

            // Camera right/up are the first two rows of the view matrix.
            float3 camRight = Frame.prowl_MatV[0].xyz;
            float3 camUp    = Frame.prowl_MatV[1].xyz;

            float3 worldPos = Mat._IconCenter
                + camRight * (input.position.x * halfSize)
                + camUp    * (input.position.y * halfSize);

            Varyings output;
            output.position = mul(Frame.prowl_MatVP, float4(worldPos, 1.0));
            output.vUV = input.position.xy * 0.5 + 0.5;
            return output;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float4 texColor = Mat._MainTex.Sample(input.vUV);
            float4 color = texColor * Mat._IconColor;

            if (color.a < 0.01) discard;

            float2 screenUV = input.position.xy / Frame._ScreenParams.xy;
            float sceneDepth = Mat._CameraDepthTexture.Sample(screenUV).r;
            float fragmentDepth = input.position.z;
            float occluded = step(sceneDepth, fragmentDepth - 0.00001);
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

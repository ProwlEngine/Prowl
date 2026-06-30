Shader "Default/GradientSkybox"
{
    Properties
    {
        _TopColor ("Top Color", Color) = (0.4, 0.6, 0.9, 1.0)
        _BottomColor ("Bottom Color", Color) = (0.8, 0.8, 0.7, 1.0)
        _Exponent ("Exponent", Float) = 1.0
    }

    Pass
    {
        Name "GradientSkybox"
        Tags { "RenderOrder" = "Opaque" }
        Cull Front
        ZWrite Off
        ZTest LessEqual

        SLANGPROGRAM
        import ProwlCG;

        struct MaterialData
        {
            float4 _TopColor;
            float4 _BottomColor;
            float _Exponent;
        }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput { float3 position : POSITION0; }
        struct Varyings { float4 position : SV_Position; float3 vDirection : TEXCOORD0; }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            // Strip translation from view matrix so skybox stays centered.
            float4x4 viewNoTranslation = Frame.prowl_MatV;
            viewNoTranslation[0][3] = 0.0;
            viewNoTranslation[1][3] = 0.0;
            viewNoTranslation[2][3] = 0.0;

            float4 pos = mul(Frame.prowl_MatP, mul(viewNoTranslation, float4(input.position, 1.0)));

            Varyings output;
            output.position = pos.xyww; // depth = 1.0 (far plane)
            output.vDirection = normalize(input.position);
            return output;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float t = pow(clamp(input.vDirection.y * 0.5 + 0.5, 0.0, 1.0), Mat._Exponent);
            float3 color = lerp(Mat._BottomColor.rgb, Mat._TopColor.rgb, t);
            return float4(color, 1.0);
        }
        ENDSLANG
    }
}

Shader "Default/SMAA"
{
    // Subpixel Morphological Anti-Aliasing (SMAA 1x), luma edge detection.
    // Three chained fullscreen passes driven by SMAAEffect:
    //   Pass 0 "EdgeDetection"    : scene color -> edges (rg)
    //   Pass 1 "BlendWeights"     : edges + AreaTex + SearchTex -> blend weights (rgba)
    //   Pass 2 "NeighborhoodBlend": scene color + weights -> antialiased color
    // The heavy lifting is the upstream reference SMAA, ported in `SMAA.slang`.
    // Quality is HIGH (16 search steps, diagonal + corner detection); the edge
    // threshold is driven live from the _EdgeThreshold uniform.

    Pass
    {
        Name "EdgeDetection"
        Tags { "RenderOrder" = "Opaque" }

        Cull Off
        ZTest Disabled
        ZWrite Off

        SLANGPROGRAM
        import SMAA;

        struct MaterialData
        {
            Sampler2D<float4> _MainTex;
            float2 _Resolution;
            float _EdgeThreshold;
        }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput
        {
            float3 position : POSITION0;
            float2 uv : TEXCOORD0;
        }
        struct Varyings
        {
            float4 position : SV_Position;
            float2 texCoords : TEXCOORD0;
            float4 offset0 : TEXCOORD1;
            float4 offset1 : TEXCOORD2;
            float4 offset2 : TEXCOORD3;
        }

        float4 rtMetrics()
        {
            return float4(1.0 / Mat._Resolution.x, 1.0 / Mat._Resolution.y, Mat._Resolution.x, Mat._Resolution.y);
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            output.texCoords = input.uv;
            output.position = float4(input.position, 1.0);

            float4 offset[3];
            SMAAEdgeDetectionVS(rtMetrics(), output.texCoords, offset);
            output.offset0 = offset[0];
            output.offset1 = offset[1];
            output.offset2 = offset[2];
            return output;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float4 offset[3] = { input.offset0, input.offset1, input.offset2 };
            float2 edges = SMAALumaEdgeDetectionPS(Mat._EdgeThreshold, input.texCoords, offset, Mat._MainTex);
            return float4(edges, 0.0, 0.0);
        }
        ENDSLANG
    }

    Pass
    {
        Name "BlendWeights"
        Tags { "RenderOrder" = "Opaque" }

        Cull Off
        ZTest Disabled
        ZWrite Off

        SLANGPROGRAM
        import SMAA;

        struct MaterialData
        {
            Sampler2D<float4> _MainTex;   // edges texture (bound by Blit source)
            Sampler2D<float4> _AreaTex;
            Sampler2D<float4> _SearchTex;
            float2 _Resolution;
            float _EdgeThreshold;
        }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput
        {
            float3 position : POSITION0;
            float2 uv : TEXCOORD0;
        }
        struct Varyings
        {
            float4 position : SV_Position;
            float2 texCoords : TEXCOORD0;
            float2 pixCoord : TEXCOORD1;
            float4 offset0 : TEXCOORD2;
            float4 offset1 : TEXCOORD3;
            float4 offset2 : TEXCOORD4;
        }

        float4 rtMetrics()
        {
            return float4(1.0 / Mat._Resolution.x, 1.0 / Mat._Resolution.y, Mat._Resolution.x, Mat._Resolution.y);
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            output.texCoords = input.uv;
            output.position = float4(input.position, 1.0);

            float2 pixcoord;
            float4 offset[3];
            SMAABlendingWeightCalculationVS(rtMetrics(), output.texCoords, pixcoord, offset);
            output.pixCoord = pixcoord;
            output.offset0 = offset[0];
            output.offset1 = offset[1];
            output.offset2 = offset[2];
            return output;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float4 offset[3] = { input.offset0, input.offset1, input.offset2 };
            return SMAABlendingWeightCalculationPS(
                rtMetrics(), input.texCoords, input.pixCoord, offset,
                Mat._MainTex, Mat._AreaTex, Mat._SearchTex);
        }
        ENDSLANG
    }

    Pass
    {
        Name "NeighborhoodBlend"
        Tags { "RenderOrder" = "Opaque" }

        Cull Off
        ZTest Disabled
        ZWrite Off

        SLANGPROGRAM
        import SMAA;

        struct MaterialData
        {
            Sampler2D<float4> _MainTex;   // scene color (bound by Blit source)
            Sampler2D<float4> _BlendTex;  // blend weights
            float2 _Resolution;
        }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput
        {
            float3 position : POSITION0;
            float2 uv : TEXCOORD0;
        }
        struct Varyings
        {
            float4 position : SV_Position;
            float2 texCoords : TEXCOORD0;
            float4 offset : TEXCOORD1;
        }

        float4 rtMetrics()
        {
            return float4(1.0 / Mat._Resolution.x, 1.0 / Mat._Resolution.y, Mat._Resolution.x, Mat._Resolution.y);
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            output.texCoords = input.uv;
            output.position = float4(input.position, 1.0);

            float4 offset;
            SMAANeighborhoodBlendingVS(rtMetrics(), output.texCoords, offset);
            output.offset = offset;
            return output;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            return SMAANeighborhoodBlendingPS(rtMetrics(), input.texCoords, input.offset, Mat._MainTex, Mat._BlendTex);
        }
        ENDSLANG
    }
}

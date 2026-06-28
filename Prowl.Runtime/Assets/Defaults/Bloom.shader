Shader "Default/Bloom"
{
    Pass
    {
        Name "Threshold"
        Tags { "RenderOrder" = "Opaque" }
        ZTest Disabled
        ZWrite Off
        Cull Off

        SLANGPROGRAM

        import ProwlCG;

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

        struct Material
        {
            float _Threshold;
            Sampler2D<float4> _MainTex;
        }
        ParameterBlock<Material> Mat;

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            output.uv = input.uv;
            output.position = float4(input.position, 1.0);
            return output;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float4 base = Mat._MainTex.Sample(input.uv);
            float3 color = base.rgb;

            float lum = dot(color, float3(0.2126, 0.7152, 0.0722));
            float contribution = max(0.0, lum - Mat._Threshold);
            contribution /= max(lum, 0.00001);

            return float4(color * contribution, base.a);
        }

        ENDSLANG
    }

    Pass
    {
        Name "Downsample"
        ZTest Disabled
        ZWrite Off
        Cull Off

        SLANGPROGRAM

        import ProwlCG;

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

        struct Material { Sampler2D<float4> _MainTex; }
        ParameterBlock<Material> Mat;

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            output.uv = input.uv;
            output.position = float4(input.position, 1.0);
            return output;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            uint texW, texH;
            Mat._MainTex.GetDimensions(texW, texH);
            float2 halfpixel = 0.5 / float2(texW, texH);

            float4 sum = Mat._MainTex.Sample(input.uv) * 4.0;
            sum += Mat._MainTex.Sample(input.uv - halfpixel);
            sum += Mat._MainTex.Sample(input.uv + halfpixel);
            sum += Mat._MainTex.Sample(input.uv + float2(halfpixel.x, -halfpixel.y));
            sum += Mat._MainTex.Sample(input.uv - float2(halfpixel.x, -halfpixel.y));

            return sum / 8.0;
        }

        ENDSLANG
    }

    Pass
    {
        Name "Upsample"
        Blend One One
        ZTest Disabled
        ZWrite Off
        Cull Off

        SLANGPROGRAM

        import ProwlCG;

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

        struct Material { Sampler2D<float4> _MainTex; }
        ParameterBlock<Material> Mat;

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            output.uv = input.uv;
            output.position = float4(input.position, 1.0);
            return output;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            uint texW, texH;
            Mat._MainTex.GetDimensions(texW, texH);
            float2 halfpixel = 0.5 / float2(texW, texH);

            float4 sum = Mat._MainTex.Sample(input.uv + float2(-halfpixel.x * 2.0, 0.0));
            sum += Mat._MainTex.Sample(input.uv + float2(-halfpixel.x, halfpixel.y)) * 2.0;
            sum += Mat._MainTex.Sample(input.uv + float2(0.0, halfpixel.y * 2.0));
            sum += Mat._MainTex.Sample(input.uv + float2(halfpixel.x, halfpixel.y)) * 2.0;
            sum += Mat._MainTex.Sample(input.uv + float2(halfpixel.x * 2.0, 0.0));
            sum += Mat._MainTex.Sample(input.uv + float2(halfpixel.x, -halfpixel.y)) * 2.0;
            sum += Mat._MainTex.Sample(input.uv + float2(0.0, -halfpixel.y * 2.0));
            sum += Mat._MainTex.Sample(input.uv + float2(-halfpixel.x, -halfpixel.y)) * 2.0;

            return sum / 12.0;
        }

        ENDSLANG
    }

    Pass
    {
        Name "Composite"
        ZTest Disabled
        ZWrite Off
        Cull Off

        SLANGPROGRAM

        import ProwlCG;

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

        struct Material
        {
            float _Intensity;
            Sampler2D<float4> _MainTex;
            Sampler2D<float4> _BloomTex;
        }
        ParameterBlock<Material> Mat;

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            output.uv = input.uv;
            output.position = float4(input.position, 1.0);
            return output;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float4 originalColor = Mat._MainTex.Sample(input.uv);
            float3 bloomColor = Mat._BloomTex.Sample(input.uv).rgb;

            float3 finalColor = originalColor.rgb + bloomColor * Mat._Intensity;

            return float4(finalColor, originalColor.a);
        }

        ENDSLANG
    }
}

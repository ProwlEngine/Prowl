Shader "Default/AutoExposure"
{
    Pass
    {
        Name "LuminanceExtract"
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
            float2 texelSize = 1.0 / float2(texW, texH);

            // 4-tap box filter at half resolution
            float3 c0 = Mat._MainTex.Sample(input.uv + float2(-0.5, -0.5) * texelSize).rgb;
            float3 c1 = Mat._MainTex.Sample(input.uv + float2( 0.5, -0.5) * texelSize).rgb;
            float3 c2 = Mat._MainTex.Sample(input.uv + float2(-0.5,  0.5) * texelSize).rgb;
            float3 c3 = Mat._MainTex.Sample(input.uv + float2( 0.5,  0.5) * texelSize).rgb;

            // Compute log-luminance for each sample (log of geometric mean)
            float l0 = log(max(luminance(c0), 0.0001));
            float l1 = log(max(luminance(c1), 0.0001));
            float l2 = log(max(luminance(c2), 0.0001));
            float l3 = log(max(luminance(c3), 0.0001));

            float avgLogLum = (l0 + l1 + l2 + l3) * 0.25;
            return float4(avgLogLum, 0.0, 0.0, 1.0);
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
            float2 texelSize = 1.0 / float2(texW, texH);

            // 4-tap box filter
            float s0 = Mat._MainTex.Sample(input.uv + float2(-0.5, -0.5) * texelSize).r;
            float s1 = Mat._MainTex.Sample(input.uv + float2( 0.5, -0.5) * texelSize).r;
            float s2 = Mat._MainTex.Sample(input.uv + float2(-0.5,  0.5) * texelSize).r;
            float s3 = Mat._MainTex.Sample(input.uv + float2( 0.5,  0.5) * texelSize).r;

            return float4((s0 + s1 + s2 + s3) * 0.25, 0.0, 0.0, 1.0);
        }

        ENDSLANG
    }

    Pass
    {
        Name "Adapt"
        ZTest Disabled
        ZWrite Off
        Cull Off

        SLANGPROGRAM

        import ShaderVariables;

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

        struct Material
        {
            float _AdaptSpeedUp;     // Speed when going brighter (EV/s)
            float _AdaptSpeedDown;   // Speed when going darker (EV/s)
            float _HistoryValid;     // 0.0 = first frame, snap to current
            Sampler2D<float4> _MainTex;      // Current measured log-luminance (1x1 or small)
            Sampler2D<float4> _AdaptedTex;   // Previous adapted luminance
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
            // Current geometric mean luminance from downsample chain
            float currentLogLum = Mat._MainTex.Sample(float2(0.5)).r;
            float currentLum = exp(currentLogLum);

            // Previous adapted luminance
            float prevLum = Mat._AdaptedTex.Sample(float2(0.5)).r;

            if (Mat._HistoryValid < 0.5)
            {
                // First frame - snap to current
                return float4(currentLum, 0.0, 0.0, 1.0);
            }

            // Asymmetric adaptation speed: faster going dark->bright, slower bright->dark
            // (or vice versa depending on user config)
            float speed = (currentLum > prevLum) ? Mat._AdaptSpeedUp : Mat._AdaptSpeedDown;

            float dt = Frame.prowl_DeltaTime.x;
            float adaptFactor = 1.0 - exp(-dt * speed);
            float adaptedLum = prevLum + (currentLum - prevLum) * adaptFactor;

            // Clamp to reasonable range to prevent extreme values
            adaptedLum = clamp(adaptedLum, 0.0001, 100.0);

            return float4(adaptedLum, 0.0, 0.0, 1.0);
        }

        ENDSLANG
    }

    Pass
    {
        Name "ApplyExposure"
        ZTest Disabled
        ZWrite Off
        Cull Off

        SLANGPROGRAM

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

        struct Material
        {
            float _ExposureComp;     // Exposure compensation in EV stops
            float _MinExposure;      // Minimum exposure multiplier
            float _MaxExposure;      // Maximum exposure multiplier
            Sampler2D<float4> _MainTex;      // HDR scene color
            Sampler2D<float4> _AdaptedTex;   // 1x1 adapted luminance
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
            float4 sceneColor = Mat._MainTex.Sample(input.uv);
            float adaptedLum = Mat._AdaptedTex.Sample(float2(0.5)).r;

            // Standard exposure formula: key / luminance
            // 0.18 is the standard "middle gray" key value
            float exposure = 0.18 / max(adaptedLum, 0.0001);

            // Apply EV compensation (each stop doubles/halves exposure)
            exposure *= exp2(Mat._ExposureComp);

            // Clamp exposure to user-defined range
            exposure = clamp(exposure, Mat._MinExposure, Mat._MaxExposure);

            return float4(sceneColor.rgb * exposure, sceneColor.a);
        }

        ENDSLANG
    }
}

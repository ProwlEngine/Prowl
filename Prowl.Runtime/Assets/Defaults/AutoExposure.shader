Shader "Default/AutoExposure"
{
    // Pass 0: Extract log-luminance from HDR scene and downsample to half-res
    Pass
    {
        Name "LuminanceExtract"
        ZTest Disabled
        ZWrite Off
        Cull Off

        SLANGPROGRAM
        import ProwlCG;

        struct MaterialData { Sampler2D<float4> _MainTex; }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

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
            uint w, h;
            Mat._MainTex.GetDimensions(w, h);
            float2 texelSize = 1.0 / float2(w, h);

            float3 c0 = Mat._MainTex.Sample(input.uv + float2(-0.5, -0.5) * texelSize).rgb;
            float3 c1 = Mat._MainTex.Sample(input.uv + float2( 0.5, -0.5) * texelSize).rgb;
            float3 c2 = Mat._MainTex.Sample(input.uv + float2(-0.5,  0.5) * texelSize).rgb;
            float3 c3 = Mat._MainTex.Sample(input.uv + float2( 0.5,  0.5) * texelSize).rgb;

            float l0 = log(max(luminance(c0), 0.0001));
            float l1 = log(max(luminance(c1), 0.0001));
            float l2 = log(max(luminance(c2), 0.0001));
            float l3 = log(max(luminance(c3), 0.0001));

            float avgLogLum = (l0 + l1 + l2 + l3) * 0.25;
            return float4(avgLogLum, 0.0, 0.0, 1.0);
        }
        ENDSLANG
    }

    // Pass 1: Downsample log-luminance (box filter, reused in chain)
    Pass
    {
        Name "Downsample"
        ZTest Disabled
        ZWrite Off
        Cull Off

        SLANGPROGRAM

        struct MaterialData { Sampler2D<float4> _MainTex; }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

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
            uint w, h;
            Mat._MainTex.GetDimensions(w, h);
            float2 texelSize = 1.0 / float2(w, h);

            float s0 = Mat._MainTex.Sample(input.uv + float2(-0.5, -0.5) * texelSize).r;
            float s1 = Mat._MainTex.Sample(input.uv + float2( 0.5, -0.5) * texelSize).r;
            float s2 = Mat._MainTex.Sample(input.uv + float2(-0.5,  0.5) * texelSize).r;
            float s3 = Mat._MainTex.Sample(input.uv + float2( 0.5,  0.5) * texelSize).r;

            return float4((s0 + s1 + s2 + s3) * 0.25, 0.0, 0.0, 1.0);
        }
        ENDSLANG
    }

    // Pass 2: Temporal adaptation
    Pass
    {
        Name "Adapt"
        ZTest Disabled
        ZWrite Off
        Cull Off

        SLANGPROGRAM
        import ProwlCG;

        struct MaterialData
        {
            Sampler2D<float4> _MainTex;     // Current measured log-luminance
            Sampler2D<float4> _AdaptedTex;  // Previous adapted luminance
            float _AdaptSpeedUp;
            float _AdaptSpeedDown;
            float _HistoryValid;
        }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

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
            float currentLogLum = Mat._MainTex.Sample(float2(0.5)).r;
            float currentLum = exp(currentLogLum);

            float prevLum = Mat._AdaptedTex.Sample(float2(0.5)).r;

            if (Mat._HistoryValid < 0.5)
                return float4(currentLum, 0.0, 0.0, 1.0);

            float speed = (currentLum > prevLum) ? Mat._AdaptSpeedUp : Mat._AdaptSpeedDown;

            float dt = Frame.prowl_DeltaTime.x;
            float adaptFactor = 1.0 - exp(-dt * speed);
            float adaptedLum = prevLum + (currentLum - prevLum) * adaptFactor;

            adaptedLum = clamp(adaptedLum, 0.0001, 100.0);

            return float4(adaptedLum, 0.0, 0.0, 1.0);
        }
        ENDSLANG
    }

    // Pass 3: Apply exposure to HDR scene color
    Pass
    {
        Name "ApplyExposure"
        ZTest Disabled
        ZWrite Off
        Cull Off

        SLANGPROGRAM

        struct MaterialData
        {
            Sampler2D<float4> _MainTex;     // HDR scene color
            Sampler2D<float4> _AdaptedTex;  // 1x1 adapted luminance
            float _ExposureComp;
            float _MinExposure;
            float _MaxExposure;
        }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

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

            float exposure = 0.18 / max(adaptedLum, 0.0001);
            exposure *= exp2(Mat._ExposureComp);
            exposure = clamp(exposure, Mat._MinExposure, Mat._MaxExposure);

            return float4(sceneColor.rgb * exposure, sceneColor.a);
        }
        ENDSLANG
    }
}

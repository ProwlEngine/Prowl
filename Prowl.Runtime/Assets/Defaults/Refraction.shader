Shader "Default/Refraction"
{
    Properties
    {
        _RefractionStrength ("Refraction Strength", Float) = 0.1
        _NoiseScale ("Noise Scale", Float) = 1.0
        _Tint ("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _BlurRadius ("Blur Radius", Float) = 0.02
        _BlurMIP ("Blur MIP Bias", Float) = 2.0
        _BlurSteps ("Blur Steps", Integer) = 4
    }

    Pass
    {
        Name "Refraction"
        Tags { "RenderOrder" = "Transparent" }
        Blend SourceAlpha InverseSourceAlpha
        ZWrite Off
        Cull Back

        SLANGPROGRAM
        import ProwlCG;

        struct MaterialData
        {
            Sampler2D<float4> _GrabTexture;
            float _RefractionStrength;
            float _NoiseScale;
            float4 _Tint;
            float _BlurRadius;
            float _BlurMIP;
            int _BlurSteps;
        }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput
        {
            float3 position : POSITION0;
            float2 uv : TEXCOORD0;
            float3 normal : NORMAL0;
        }
        struct Varyings
        {
            float4 position : SV_Position;
            float2 texCoord0 : TEXCOORD0;
            float3 worldPos : TEXCOORD1;
            float4 screenPos : TEXCOORD2;
            float3 vNormal : TEXCOORD3;
        }

        // Simple 3D value noise (shared by both stages).
        float hash(float3 p)
        {
            p = frac(p * 0.3183099 + 0.1);
            p *= 17.0;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }

        float noise(float3 x)
        {
            float3 i = floor(x);
            float3 f = frac(x);
            f = f * f * (3.0 - 2.0 * f);

            return lerp(lerp(lerp(hash(i + float3(0.0, 0.0, 0.0)),
                               hash(i + float3(1.0, 0.0, 0.0)), f.x),
                           lerp(hash(i + float3(0.0, 1.0, 0.0)),
                               hash(i + float3(1.0, 1.0, 0.0)), f.x), f.y),
                       lerp(lerp(hash(i + float3(0.0, 0.0, 1.0)),
                               hash(i + float3(1.0, 0.0, 1.0)), f.x),
                           lerp(hash(i + float3(0.0, 1.0, 1.0)),
                               hash(i + float3(1.0, 1.0, 1.0)), f.x), f.y), f.z);
        }

        float InterleavedGradientNoise(float2 pixCoord, int frameCount)
        {
            const float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
            float2 frameMagicScale = float2(2.083, 4.867);
            pixCoord += float(frameCount) * frameMagicScale;
            return frac(magic.z * frac(dot(pixCoord, magic.xy)));
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            output.position = mul(Object.mvp, float4(input.position, 1.0));
            output.texCoord0 = input.uv;
            output.worldPos = mul(Object.prowl_ObjectToWorld, float4(input.position, 1.0)).xyz;
            output.screenPos = output.position;
            output.vNormal = normalize(mul((float3x3)Object.prowl_ObjectToWorld, input.normal));
            return output;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float2 screenUV = (input.screenPos.xy / input.screenPos.w) * 0.5 + 0.5;

            float3 noiseInput = input.worldPos * Mat._NoiseScale + (Frame._Time * 0.1).xyz;
            float2 refractionOffset = float2(
                noise(noiseInput + float3(0.0, 0.0, 0.0)),
                noise(noiseInput + float3(5.2, 1.3, 0.0))
            );
            refractionOffset = (refractionOffset * 2.0 - 1.0) * Mat._RefractionStrength;
            float2 refractedUV = clamp(screenUV + refractionOffset, 0.0, 1.0);

            float ign = InterleavedGradientNoise(input.position.xy, int(Frame._Time.w) % 60);
            float angle = ign * 6.28318530718;
            float2 dir = float2(cos(angle), sin(angle));
            float2 aspect = float2(min(1.0, Frame._ScreenParams.y / Frame._ScreenParams.x),
                                   min(1.0, Frame._ScreenParams.x / Frame._ScreenParams.y));

            int steps = clamp(Mat._BlurSteps, 1, 4);
            float4 acc = float4(0.0);
            for (int i = 0; i < steps; i++)
            {
                float2 offset = dir * Mat._BlurRadius * aspect;
                acc += Mat._GrabTexture.SampleLevel(refractedUV + offset, Mat._BlurMIP);
                dir = float2(-dir.y, dir.x);
            }
            float4 refractedColor = acc / float(steps);

            return float4(refractedColor.rgb * Mat._Tint.rgb, Mat._Tint.a);
        }
        ENDSLANG
    }
}

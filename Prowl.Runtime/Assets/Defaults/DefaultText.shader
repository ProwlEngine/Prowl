Shader "Default/DefaultText"
{
    Properties
    {
        _MainTex ("SDF Atlas", Texture2D) = "white" {}
        _MainColor ("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _Tiling ("Tiling", Vector) = (1.0, 1.0, 0.0, 0.0)
        _Offset ("Offset", Vector) = (0.0, 0.0, 0.0, 0.0)
    }

    Pass
    {
        Name "DefaultText"
        Tags { "RenderOrder" = "UI" }

        Blend SourceAlpha InverseSourceAlpha
        BlendOp Add
        ZTest Disabled
        ZWrite Off
        Cull Off

        SLANGPROGRAM
        import ProwlCG;

        struct MaterialData
        {
            Sampler2D<float4> _MainTex; // single-channel SDF replicated across RGB(A)
            float4 _MainColor;
            float2 _Tiling;
            float2 _Offset;

            // Per-item rounded-rect clip (RectMask), matching Default/DefaultUI.
            float4x4 _ClipToLocal;
            float4 _ClipRect;
            float _ClipRadius;
            float _ClipSoftness;
            float _ClipEnable;
        }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput
        {
            float3 position : POSITION0;
            float2 uv : TEXCOORD0;
            float4 color : COLOR0;
        }
        struct Varyings
        {
            float4 position : SV_Position;
            float2 texCoord0 : TEXCOORD0;
            float3 worldPos : TEXCOORD1;
            float4 vColor : COLOR0;
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            output.position = mul(Object.mvp, float4(input.position, 1.0));
            output.texCoord0 = input.uv * Mat._Tiling + Mat._Offset;
            output.worldPos = mul(Object.prowl_ObjectToWorld, float4(input.position, 1.0)).xyz;
            output.vColor = input.color;
            return output;
        }

        float uiClipCoverage(float3 worldPosition)
        {
            if (Mat._ClipEnable < 0.5) return 1.0;
            float2 p = mul(Mat._ClipToLocal, float4(worldPosition, 1.0)).xy;
            float2 c = (Mat._ClipRect.xy + Mat._ClipRect.zw) * 0.5;
            float2 e = (Mat._ClipRect.zw - Mat._ClipRect.xy) * 0.5 - float2(Mat._ClipRadius);
            float2 d = abs(p - c) - e;
            float dist = length(max(d, float2(0.0))) + min(max(d.x, d.y), 0.0) - Mat._ClipRadius;
            float soft = max(Mat._ClipSoftness, max(fwidth(dist), 1e-4));
            return clamp(0.5 - dist / soft, 0.0, 1.0);
        }

        // Reconstruct sharp, resolution-independent coverage from the distance field.
        static const float sdfPxRange = 4.0;
        float sdfScreenPxRange(float2 uv)
        {
            uint w, h;
            Mat._MainTex.GetDimensions(w, h);
            float2 unitRange = float2(sdfPxRange) / float2(w, h);
            float2 screenTexSize = float2(1.0) / fwidth(uv);
            return max(0.5 * dot(unitRange, screenTexSize), 1.0);
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float sd = Mat._MainTex.Sample(input.texCoord0).r;
            float screenPxDistance = sdfScreenPxRange(input.texCoord0) * (sd - 0.5);
            float coverage = clamp(screenPxDistance + 0.5, 0.0, 1.0);
            return input.vColor * Mat._MainColor * coverage * uiClipCoverage(input.worldPos);
        }
        ENDSLANG
    }
}

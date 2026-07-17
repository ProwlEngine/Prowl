Shader "Default/DefaultTextMesh"
{
    Properties
    {
        _MainTex ("SDF Atlas", Texture2D) = "white" {}
        _MainColor ("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _Tiling ("Tiling", Vector) = (1.0, 1.0, 0.0, 0.0)
        _Offset ("Offset", Vector) = (0.0, 0.0, 0.0, 0.0)
    }

    // World-space SDF text (TextMeshComponent). Same distance-field coverage as Default/DefaultText,
    // but tagged Transparent so the scene pipeline draws it, and depth-aware (default ZTest) so it is
    // occluded by nearer geometry. For an always-on-top nameplate, copy this and add `ZTest Disabled`.
    Pass
    {
        Name "DefaultTextMesh"
        Tags { "RenderOrder" = "Transparent" }

        Blend SourceAlpha InverseSourceAlpha
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
            float4 vColor : COLOR0;
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            output.position = mul(Object.mvp, float4(input.position, 1.0));
            output.texCoord0 = input.uv * Mat._Tiling + Mat._Offset;
            output.vColor = input.color;
            return output;
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
            return input.vColor * Mat._MainColor * coverage;
        }
        ENDSLANG
    }
}

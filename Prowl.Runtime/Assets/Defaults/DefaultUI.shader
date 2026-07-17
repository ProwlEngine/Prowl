Shader "Default/DefaultUI"
{
    Properties
    {
        _MainTex ("Texture", Texture2D) = "white" {}
        _MainColor ("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _Tiling ("Tiling", Vector) = (1.0, 1.0, 0.0, 0.0)
        _Offset ("Offset", Vector) = (0.0, 0.0, 0.0, 0.0)
    }

    Pass
    {
        Name "DefaultUI"
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
            Sampler2D<float4> _MainTex;
            float4 _MainColor;
            float2 _Tiling;
            float2 _Offset;

            // Per-item rounded-rect clip (RectMask). _ClipToLocal maps the fragment's world position
            // into the mask's local space (so the clip follows rotation/scale); the fragment is tested
            // against _ClipRect with _ClipRadius rounded corners and a _ClipSoftness edge.
            float4x4 _ClipToLocal;
            float4 _ClipRect; // minX, minY, maxX, maxY
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

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float4 albedo = Mat._MainTex.Sample(input.texCoord0) * input.vColor * Mat._MainColor;
            return albedo * uiClipCoverage(input.worldPos);
        }
        ENDSLANG
    }
}

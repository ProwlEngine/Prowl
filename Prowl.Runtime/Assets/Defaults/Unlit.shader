Shader "Default/Unlit"
{
    Properties
    {
        _MainTex("Texture", Texture2D) = "white" {}
        _MainColor("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _Tiling("Tiling", Vector) = (1.0, 1.0, 0, 0)
        _Offset("Offset", Vector) = (0.0, 0.0, 0, 0)
    }

    Pass
    {
        Name "Unlit"
        Tags { "RenderOrder" = "Opaque" }
        Cull Back

        SLANGPROGRAM

        import ProwlCG;
        import VertexAttributes;
        import Lighting;

        struct VertexInput
        {
            float3 position : POSITION0;
            float2 uv0 : TEXCOORD0;
            float4 color : COLOR0;
            uint vid : SV_VertexID;
        }

        struct Varyings
        {
            float4 position : SV_Position;
            float2 texCoord0 : TEXCOORD0;
            float3 worldPos : TEXCOORD1;
            float4 vColor : COLOR0;
        }

        struct Material
        {
            float2 _Tiling;
            float2 _Offset;
            float4 _MainColor;
            Sampler2D<float4> _MainTex;
        }
        ParameterBlock<Material> Mat;

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings o;
            o.position = TransformClip(input.position, input.vid);
            o.texCoord0 = input.uv0 * Mat._Tiling + Mat._Offset;
            o.worldPos = TransformPosition(input.position, input.vid);
            o.vColor = GetInstanceColor(input.color);
            return o;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float4 albedo = Mat._MainTex.Sample(input.texCoord0) * input.vColor * Mat._MainColor;
            float3 baseColor = gammaToLinearSpace(albedo.rgb);
            baseColor = ApplyFog(baseColor, input.worldPos);
            return float4(baseColor, albedo.a);
        }

        ENDSLANG
    }

    Pass
    {
        Name "UnlitPrepass"
        Tags { "LightMode" = "Prepass" }
        Cull Back
        ZWrite On

        SLANGPROGRAM

        import ProwlCG;
        import VertexAttributes;

        struct VertexInput
        {
            float3 position : POSITION0;
            float3 normal : NORMAL0;
            uint vid : SV_VertexID;
        }

        struct Varyings
        {
            float4 position : SV_Position;
            float3 vNormal : NORMAL0;
            float4 vCurrClipNJ : TEXCOORD0;
            float4 vPrevClip : TEXCOORD1;
        }

        struct FragOut
        {
            float4 normalOut : SV_Target0;
            float4 motionRM : SV_Target1;
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings o;
            o.position = TransformClip(input.position, input.vid); // jittered, for raster + depth
            o.vNormal = TransformDirection(input.normal);

            // Jitter-free current + previous clip positions for motion vectors.
            float4 worldPos = mul(GetModelMatrix(), float4(input.position, 1.0));
            o.vCurrClipNJ = mul(Frame.prowl_MatVP_NonJittered, worldPos);
            float4 prevWorldPos = mul(Object.prowl_PrevObjectToWorld, float4(input.position, 1.0));
            o.vPrevClip = mul(Frame.prowl_PrevViewProj, prevWorldPos);
            return o;
        }

        [shader("fragment")]
        FragOut Fragment(Varyings input)
        {
            FragOut o;
            o.normalOut = EncodeViewNormal(normalize(input.vNormal));

            // Motion vectors (jitter-free). Unlit has no PBR material -> roughness/metallic 0.
            float2 currNDC = (input.vCurrClipNJ.xy / input.vCurrClipNJ.w) * 0.5 + 0.5;
            float2 prevNDC = (input.vPrevClip.xy / input.vPrevClip.w) * 0.5 + 0.5;
            o.motionRM = float4(currNDC - prevNDC, 0.0, 0.0);
            return o;
        }

        ENDSLANG
    }
}

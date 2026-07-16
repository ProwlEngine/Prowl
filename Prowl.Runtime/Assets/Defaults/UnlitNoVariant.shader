Shader "Default/UnlitNoVariant"
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
        Name "Unlit"
        Tags { "RenderOrder" = "Opaque" }
        Cull Back

        SLANGPROGRAM
        import ProwlCG;

        struct MaterialData
        {
            Sampler2D<float4> _MainTex;
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

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float4 albedo = Mat._MainTex.Sample(input.texCoord0) * input.vColor * Mat._MainColor;
            float3 baseColor = gammaToLinearSpace(albedo.rgb);
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

        struct VertexInput
        {
            float3 position : POSITION0;
            float3 normal : NORMAL0;
        }
        struct Varyings
        {
            float4 position : SV_Position;
            float3 vNormal : TEXCOORD0;
            float4 vCurrClipNJ : TEXCOORD1;
            float4 vPrevClip : TEXCOORD2;
        }
        struct FragOutput
        {
            float4 normalOut : SV_Target0;
            float4 motionRM : SV_Target1;
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            float4 worldPos = mul(Object.prowl_ObjectToWorld, float4(input.position, 1.0));

            output.position = mul(Frame.prowl_MatVP, worldPos);
            output.vNormal = normalize(mul((float3x3)Object.prowl_ObjectToWorld, input.normal));
            output.vCurrClipNJ = mul(Frame.prowl_MatVP_NonJittered, worldPos);

            float4 prevWorldPos = mul(Object.prowl_PrevObjectToWorld, float4(input.position, 1.0));
            output.vPrevClip = mul(Frame.prowl_PrevViewProj, prevWorldPos);
            return output;
        }

        [shader("fragment")]
        FragOutput Fragment(Varyings input)
        {
            FragOutput o;
            o.normalOut = EncodeViewNormal(normalize(input.vNormal));

            float2 currNDC = (input.vCurrClipNJ.xy / input.vCurrClipNJ.w) * 0.5 + 0.5;
            float2 prevNDC = (input.vPrevClip.xy / input.vPrevClip.w) * 0.5 + 0.5;
            o.motionRM = float4(currNDC - prevNDC, 0.0, 0.0);
            return o;
        }
        ENDSLANG
    }
}

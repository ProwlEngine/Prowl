Shader "Default/Unlit"
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
        import Lighting;
        import VertexAttributes;

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
            float3 normal : NORMAL0;
            float4 color : COLOR0;
            float4 boneIndices : BLENDINDICES0;
            float4 boneWeights : BLENDWEIGHT0;
            float4 instRow0 : TEXCOORD8;
            float4 instRow1 : TEXCOORD9;
            float4 instRow2 : TEXCOORD10;
            float4 instRow3 : TEXCOORD11;
            float4 instColor : TEXCOORD12;
            float4 instCustom : TEXCOORD13;
            uint vid : SV_VertexID;
        }
        struct Varyings
        {
            float4 position : SV_Position;
            float2 texCoord0 : TEXCOORD0;
            float3 worldPos : TEXCOORD1;
            float4 vColor : COLOR0;
        }

        MeshVertex MakeVertex(VertexInput input)
        {
            MeshVertex v;
            v.position = input.position;
            v.normal = input.normal;
            v.tangent = float4(1, 0, 0, 1);
            v.color = input.color;
            v.vid = input.vid;
            v.boneIndices = input.boneIndices;
            v.boneWeights = input.boneWeights;
            v.instanceRow0 = input.instRow0;
            v.instanceRow1 = input.instRow1;
            v.instanceRow2 = input.instRow2;
            v.instanceRow3 = input.instRow3;
            v.instanceColor = input.instColor;
            v.instanceCustomData = input.instCustom;
            return v;
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            MeshVertex v = MakeVertex(input);
            Varyings output;
            output.position = TransformClip(v);
            output.texCoord0 = input.uv * Mat._Tiling + Mat._Offset;
            output.worldPos = TransformPosition(v);
            output.vColor = GetInstanceColor(v);
            return output;
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
            float4 boneIndices : BLENDINDICES0;
            float4 boneWeights : BLENDWEIGHT0;
            float4 instRow0 : TEXCOORD8;
            float4 instRow1 : TEXCOORD9;
            float4 instRow2 : TEXCOORD10;
            float4 instRow3 : TEXCOORD11;
            uint vid : SV_VertexID;
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

        MeshVertex MakeVertex(VertexInput input)
        {
            MeshVertex v;
            v.position = input.position;
            v.normal = input.normal;
            v.tangent = float4(1, 0, 0, 1);
            v.color = float4(1, 1, 1, 1);
            v.vid = input.vid;
            v.boneIndices = input.boneIndices;
            v.boneWeights = input.boneWeights;
            v.instanceRow0 = input.instRow0;
            v.instanceRow1 = input.instRow1;
            v.instanceRow2 = input.instRow2;
            v.instanceRow3 = input.instRow3;
            v.instanceColor = float4(1, 1, 1, 1);
            v.instanceCustomData = float4(0);
            return v;
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            MeshVertex v = MakeVertex(input);
            Varyings output;
            output.position = TransformClip(v);
            output.vNormal = TransformDirection(v, input.normal);

            float4 worldPos = mul(GetModelMatrix(v), float4(input.position, 1.0));
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

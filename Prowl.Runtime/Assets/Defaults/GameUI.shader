Shader "Default/GameUI"
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
        Name "GameUI"
        Tags { "RenderOrder" = "UI" }

        Blend SourceAlpha InverseSourceAlpha
        BlendOp Add
        ZTest Disabled
        ZWrite Off
        Cull Off

        SLANGPROGRAM
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
            float4 vColor : COLOR0;
        }

        MeshVertex MakeVertex(VertexInput input)
        {
            MeshVertex v;
            v.position = input.position;
            v.normal = float3(0, 1, 0);
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
            output.vColor = GetInstanceColor(v);
            return output;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            return Mat._MainTex.Sample(input.texCoord0) * input.vColor * Mat._MainColor;
        }
        ENDSLANG
    }
}

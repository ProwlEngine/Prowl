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
        ZTest LessEqual
        ZWrite On

        SLANGPROGRAM
        import ProwlCG;
        import VariantAttributes;

        [VariantAxis] extern static const bool GPU_INSTANCING;

        struct MaterialData
        {
            Sampler2D<float4> _MainTex;
            float4 _MainColor;
            float4 _Tiling;
            float4 _Offset;
        }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput
        {
            float3 position : POSITION0;
            float2 uv : TEXCOORD0;

            // Per-instance transform columns + tint. Only read on the GPU_INSTANCING variant, so
            // the compiler prunes them (and their vertex bindings) from the non-instanced variant.
            float4 instanceCol0 : TEXCOORD8;
            float4 instanceCol1 : TEXCOORD9;
            float4 instanceCol2 : TEXCOORD10;
            float4 instanceCol3 : TEXCOORD11;
            float4 instanceColor : TEXCOORD12;
        }

        struct Varyings
        {
            float4 position : SV_Position;
            float2 uv : TEXCOORD0;
            float4 color : TEXCOORD1;
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            float4 localPos = float4(input.position, 1.0);

            static if (GPU_INSTANCING)
            {
                // InstanceData packs the model matrix as four columns; float4x4(...) takes rows, so
                // transpose to recover a column-major matrix usable with mul(model, v).
                float4x4 model = transpose(float4x4(input.instanceCol0, input.instanceCol1, input.instanceCol2, input.instanceCol3));
                output.position = mul(Frame.prowl_MatVP, mul(model, localPos));
                output.color = input.instanceColor;
            }
            else
            {
                output.position = mul(Object.mvp, localPos);
                output.color = float4(1.0, 1.0, 1.0, 1.0);
            }

            output.uv = input.uv * Mat._Tiling.xy + Mat._Offset.xy;
            return output;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float4 albedo = Mat._MainTex.Sample(input.uv) * input.color * Mat._MainColor;
            float3 baseColor = gammaToLinearSpace(albedo.rgb);
            return float4(baseColor, albedo.a);
        }
        ENDSLANG
    }
}

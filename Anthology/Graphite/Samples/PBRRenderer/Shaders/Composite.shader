Shader "PBRRenderer/Composite"
{
    Pass
    {
        Name "Composite"

        Cull Off
        ZTest Disabled

        SLANGPROGRAM
        import UVOrigin;


        struct CompositeData
        {
            Sampler2D<float4> sceneTexture;
            Sampler2D<float4> bloomTexture;
            float bloomIntensity;
        };
        ParameterBlock<CompositeData> CompositeParams;


        [shader("vertex")]
        float4 Vertex(uint vertexIndex : SV_VertexID, out float2 UV : UV0) : SV_Position
        {
            float2 baseUV = float2(float((vertexIndex << 1) & 2), float(vertexIndex & 2));

            static if (IsUVOriginTopLeft)
                UV = float2(baseUV.x, 1.0 - baseUV.y);
            else
                UV = float2(baseUV.x, baseUV.y);

            return float4(baseUV * 2.0 - 1.0, 0.0, 1.0);
        }


        [shader("fragment")]
        float4 Fragment(float2 UV : UV0) : SV_Target
        {
            float4 scene = CompositeParams.sceneTexture.Sample(UV);
            float4 bloom = CompositeParams.bloomTexture.Sample(UV);
            return float4(scene.rgb + bloom.rgb * CompositeParams.bloomIntensity, 1.0);
        }
        ENDSLANG
    }
}

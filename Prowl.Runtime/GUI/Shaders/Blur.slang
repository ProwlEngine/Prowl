import VariantAttributes;
import UVOrigin;


[variant("false") variant("true")]
extern static const bool Upsample;


struct DownsampleData
{
    Sampler2D<float4> sourceTexture;
    float2 halfPixel;
    float offset;
};
ParameterBlock<DownsampleData> DownsampleParams;


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
    Sampler2D<float4> sourceTexture = DownsampleParams.sourceTexture;
    float2 halfPixel = DownsampleParams.halfPixel;
    float offset = DownsampleParams.offset;

    static if (Upsample)
    {
        float4 sum = sourceTexture.Sample(UV + float2(-halfPixel.x * 2.0, 0.0) * offset);
        sum += sourceTexture.Sample(UV + float2(-halfPixel.x, halfPixel.y) * offset) * 2.0;
        sum += sourceTexture.Sample(UV + float2(0.0, halfPixel.y * 2.0) * offset);
        sum += sourceTexture.Sample(UV + float2(halfPixel.x, halfPixel.y) * offset) * 2.0;
        sum += sourceTexture.Sample(UV + float2(halfPixel.x * 2.0, 0.0) * offset);
        sum += sourceTexture.Sample(UV + float2(halfPixel.x, -halfPixel.y) * offset) * 2.0;
        sum += sourceTexture.Sample(UV + float2(0.0, -halfPixel.y * 2.0) * offset);
        sum += sourceTexture.Sample(UV + float2(-halfPixel.x, -halfPixel.y) * offset) * 2.0;
        return sum / 12.0;
    }

    float4 sum = sourceTexture.Sample(UV) * 4.0;
    sum += sourceTexture.Sample(UV - halfPixel * offset);
    sum += sourceTexture.Sample(UV + halfPixel * offset);
    sum += sourceTexture.Sample(UV + float2(halfPixel.x, -halfPixel.y) * offset);
    sum += sourceTexture.Sample(UV - float2(halfPixel.x, -halfPixel.y) * offset);
    return sum / 8.0;
}
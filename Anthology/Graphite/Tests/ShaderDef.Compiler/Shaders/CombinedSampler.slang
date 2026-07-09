module CombinedSampler;


struct VertexInput
{
    float3 position : POSITION;
    float2 uv       : UV0;
}


struct VertexOutput
{
    float4 clipPosition : SV_Position;
    float2 uv : UV;
}


// A combined Sampler2D<> beside a split Texture2D + SamplerState, so reflection can assert it flags only the combined one.
struct ModelData
{
    float4x4 MatrixMVP;
    Sampler2D<float4> Combined;
    Texture2D<float4> SplitTexture;
    SamplerState SplitSampler;
};
ParameterBlock<ModelData> Model;


[shader("vertex")]
VertexOutput vertex(VertexInput input)
{
    VertexOutput output;
    output.clipPosition = mul(Model.MatrixMVP, float4(input.position, 1.0));
    output.uv = input.uv;
    return output;
}


[shader("fragment")]
float4 fragment(VertexOutput input) : SV_Target
{
    return Model.Combined.Sample(input.uv)
        + Model.SplitTexture.Sample(Model.SplitSampler, input.uv);
}

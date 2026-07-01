module ShaderMainModule;


struct VertexInput
{
    float3 position : POSITION;
    float2 uv       : UV0;
    float4 color : COLOR;
}


struct VertexOutput
{
    float4 clipPosition : SV_Position;
    float2 uv : UV;
    float4 color : COLOR;
}


struct ModelData
{
    float4x4 MatrixMVP;
    float4 Color;
    Sampler2D<float4> MainTexture;
};
ParameterBlock<ModelData> Model;


[shader("vertex")]
VertexOutput vertex(VertexInput input)
{
    VertexOutput output;
    output.clipPosition = mul(Model.MatrixMVP, float4(input.position, 1.0));
    output.uv = input.uv;
    output.color = input.color * Model.Color;

    return output;
}


[shader("fragment")]
float4 fragment(VertexOutput input) : SV_Target
{
    return Model.MainTexture.Sample(input.uv);
}


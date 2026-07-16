module CrossSetBinding;

// Spreads every resource kind across three descriptor sets so binding cannot be proven correct
// by accident on a single-set shader: a uniform in each set, the output buffer in set 0, a
// read-only structured buffer in set 1, and a texture + sampler pair in set 2.
//
// Output layout:
//   [0] valueA  (uniform, set 0)
//   [1] valueB  (uniform, set 1)
//   [2] Input[0] (structured buffer, set 1)
//   [3] valueC  (uniform, set 2)
//   [4] sampled texel red channel, scaled to 0..255 (texture + sampler, set 2)

struct SetA
{
    uint valueA;
    RWStructuredBuffer<uint> Output;
}
ParameterBlock<SetA> BlockA;

struct SetB
{
    uint valueB;
    StructuredBuffer<uint> Input;
}
ParameterBlock<SetB> BlockB;

struct SetC
{
    uint valueC;
    Texture2D<float4> Tex;
    SamplerState Samp;
}
ParameterBlock<SetC> BlockC;

[shader("compute")]
[numthreads(1, 1, 1)]
void compute(uint3 id : SV_DispatchThreadID)
{
    BlockA.Output[0] = BlockA.valueA;
    BlockA.Output[1] = BlockB.valueB;
    BlockA.Output[2] = BlockB.Input[0];
    BlockA.Output[3] = BlockC.valueC;

    float4 texel = BlockC.Tex.SampleLevel(BlockC.Samp, float2(0.5, 0.5), 0.0);
    BlockA.Output[4] = (uint)(texel.r * 255.0 + 0.5);
}

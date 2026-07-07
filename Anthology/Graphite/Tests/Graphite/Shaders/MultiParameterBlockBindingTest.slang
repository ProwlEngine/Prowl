module MultiParameterBlockBindingTest;

// Regression test for a GL binding-point aliasing bug
// Buffers were aliasing on OpenGL where two buffers with the same local ParameterSet binding 
// get resolved to the same shader-wide GL binding as well by accident 
// (i.e set 0, buffer 0 gets resolved to the same location in the OpenGL backend as set 1, buffer 0)
struct BlockAData
{
    uint valueA;
    RWStructuredBuffer<uint> Output;
}
ParameterBlock<BlockAData> BlockA;

struct BlockBData { uint valueB; }
ParameterBlock<BlockBData> BlockB;

[shader("compute")]
[numthreads(1, 1, 1)]
void compute(uint3 id : SV_DispatchThreadID)
{
    BlockA.Output[0] = BlockA.valueA;
    BlockA.Output[1] = BlockB.valueB;
}

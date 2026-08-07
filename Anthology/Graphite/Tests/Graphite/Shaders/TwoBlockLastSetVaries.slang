module TwoBlockLastSetVaries;

// Set 0 holds a value that stays constant across a whole recording; set 1 holds the value and
// the output buffer that both change every dispatch. Lets a test hold set 0's binding fixed while
// forcing set 1 to change every dispatch, exercising a narrowed rebind that starts above set 0.
struct BlockAData { uint fixedValue; }
ParameterBlock<BlockAData> BlockA;

struct BlockBData
{
    uint valueB;
    RWStructuredBuffer<uint> Output;
}
ParameterBlock<BlockBData> BlockB;

[shader("compute")]
[numthreads(1, 1, 1)]
void compute(uint3 id : SV_DispatchThreadID)
{
    BlockB.Output[0] = BlockA.fixedValue;
    BlockB.Output[1] = BlockB.valueB;
}

module BasicComputeTest;

// For each element: copies Source into Destination, then doubles Source in place.
// Width/Height are uniform parameters; Source/Destination are read-write structured buffers.

struct Globals
{
    uint Width;
    uint Height;
    uint _padding1;
    uint _padding2;

    RWStructuredBuffer<float> Source;
    RWStructuredBuffer<float> Destination;
};
ParameterBlock<Globals> Resources;

[shader("compute")]
[numthreads(16, 16, 1)]
void compute(uint3 id : SV_DispatchThreadID)
{
    uint index = id.y * Resources.Width + id.x;
    Resources.Destination[index] = Resources.Source[index];
    Resources.Source[index] += Resources.Source[index];
}

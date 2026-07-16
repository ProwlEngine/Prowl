module OrphanProbe;

// Keeps a frame executing long enough for the CPU to race a write against Source, then reads
// Source late and reports what the GPU actually saw. Used by BufferSafetyTests to observe the
// implicit-reallocation (orphaning) path.
//
// Output[0] is the late read of Source[0]. Output[1] receives the spin accumulator purely so the
// timing loop cannot be optimized away; nothing asserts on it.

struct Globals
{
    uint Iterations;
    uint _padding0;
    uint _padding1;
    uint _padding2;

    RWStructuredBuffer<uint> Source;
    RWStructuredBuffer<uint> Output;
};
ParameterBlock<Globals> Resources;

[shader("compute")]
[numthreads(1, 1, 1)]
void compute(uint3 id : SV_DispatchThreadID)
{
    uint spin = 1u;
    for (uint i = 0; i < Resources.Iterations; i++)
        spin = spin * 1664525u + 1013904223u;

    Resources.Output[0] = Resources.Source[0];
    Resources.Output[1] = spin;
}

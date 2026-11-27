// Spins for Iterations steps, then writes the late read of Source[0] and the spin result.
Compute
{
    layout(local_size_x = 1, local_size_y = 1, local_size_z = 1) in;

    layout(set = 0, binding = 0, std140) uniform Params
    {
        uint Iterations;
        uint _padding0;
        uint _padding1;
        uint _padding2;
    };

    layout(set = 0, binding = 1, std430) buffer Source { uint values[]; } source;
    layout(set = 0, binding = 2, std430) buffer Output { uint values[]; } outputs;

    void main()
    {
        uint spin = 1u;
        for (uint i = 0u; i < Iterations; i++)
            spin = spin * 1664525u + 1013904223u;

        outputs.values[0] = source.values[0];
        outputs.values[1] = spin;
    }
}

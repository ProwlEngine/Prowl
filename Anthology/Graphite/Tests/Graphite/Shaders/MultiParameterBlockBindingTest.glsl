// Two sets that both use binding 0, so a backend that flattens bindings would alias them.
Compute
{
    layout(local_size_x = 1, local_size_y = 1, local_size_z = 1) in;

    layout(set = 0, binding = 0, std140) uniform BlockA { uint valueA; };
    layout(set = 0, binding = 1, std430) buffer Output { uint values[]; } outputs;

    layout(set = 1, binding = 0, std140) uniform BlockB { uint valueB; };

    void main()
    {
        outputs.values[0] = valueA;
        outputs.values[1] = valueB;
    }
}

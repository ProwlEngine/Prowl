// Set 0 holds a value fixed across a recording, set 1 holds a value and the output that change every dispatch.
Compute
{
    layout(local_size_x = 1, local_size_y = 1, local_size_z = 1) in;

    layout(set = 0, binding = 0, std140) uniform BlockA { uint fixedValue; };

    layout(set = 1, binding = 0, std140) uniform BlockB { uint valueB; };
    layout(set = 1, binding = 1, std430) buffer Output { uint values[]; } outputs;

    void main()
    {
        outputs.values[0] = fixedValue;
        outputs.values[1] = valueB;
    }
}

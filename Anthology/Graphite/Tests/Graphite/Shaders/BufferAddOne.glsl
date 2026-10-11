// Writes every input value plus one into the output.
Compute
{
    layout(local_size_x = 4) in;

    layout(set = 0, binding = 0, std430) readonly buffer Input { uint values[]; } input_;
    layout(set = 0, binding = 1, std430) buffer Output { uint values[]; } output_;

    void main()
    {
        uint i = gl_GlobalInvocationID.x;
        output_.values[i] = input_.values[i] + 1u;
    }
}

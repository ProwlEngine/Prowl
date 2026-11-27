// Writes a 4x1 storage texture: red, green, blue, white.
Compute
{
    layout(local_size_x = 4, local_size_y = 1, local_size_z = 1) in;

    layout(set = 0, binding = 0, rgba32f) uniform image2D ComputeOutput;

    const vec4 Colors[4] = vec4[4](
        vec4(1, 0, 0, 1),
        vec4(0, 1, 0, 1),
        vec4(0, 0, 1, 1),
        vec4(1, 1, 1, 1));

    void main()
    {
        uvec3 id = gl_GlobalInvocationID;
        imageStore(ComputeOutput, ivec2(id.xy), Colors[id.x]);
    }
}

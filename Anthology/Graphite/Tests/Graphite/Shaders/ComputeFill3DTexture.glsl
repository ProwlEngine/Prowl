// Fills a 3D storage texture so every texel at depth z is FillValue * (z + 1).
Compute
{
    layout(local_size_x = 16, local_size_y = 16, local_size_z = 1) in;

    layout(set = 0, binding = 0, std140) uniform FillValueBuffer
    {
        float FillValue;
        float _padding1;
        float _padding2;
        float _padding3;
    };

    layout(set = 0, binding = 1, rgba32f) uniform image3D TextureToFill;

    void main()
    {
        uvec3 id = gl_GlobalInvocationID;
        float value = FillValue * float(id.z + 1u);
        imageStore(TextureToFill, ivec3(id), vec4(value, value, value, value));
    }
}

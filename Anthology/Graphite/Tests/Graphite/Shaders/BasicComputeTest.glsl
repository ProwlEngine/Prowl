// For each element: copies Source into Destination, then doubles Source in place.
Compute
{
    layout(local_size_x = 16, local_size_y = 16, local_size_z = 1) in;

    layout(set = 0, binding = 0, std140) uniform Params
    {
        uint Width;
        uint Height;
        uint _padding1;
        uint _padding2;
    };

    layout(set = 0, binding = 1, std430) buffer Source { float values[]; } source;
    layout(set = 0, binding = 2, std430) buffer Destination { float values[]; } destination;

    void main()
    {
        uvec3 id = gl_GlobalInvocationID;
        uint index = id.y * Width + id.x;
        destination.values[index] = source.values[index];
        source.values[index] += source.values[index];
    }
}

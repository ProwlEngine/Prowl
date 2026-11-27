// Copies runs of words from the frame's upload buffer into a persistent buffer, one workgroup per run.
Compute
{
    layout(local_size_x = 64) in;

    layout(set = 0, binding = 0, std430) readonly buffer Items { uvec4 items[]; };
    layout(set = 0, binding = 1, std430) readonly buffer Source { uint source[]; };
    layout(set = 0, binding = 2, std430) buffer Destination { uint destination[]; };
    layout(push_constant) uniform Push { uint firstItem; } push;

    void main()
    {
        uvec4 item = items[push.firstItem + gl_WorkGroupID.x];
        // Counted, as Adreno 610 faults on a loop counting from the invocation id. 64 chunks cover MaxItemWords
        for (uint chunk = 0u; chunk < 64u; chunk++)
        {
            uint word = gl_LocalInvocationID.x + chunk * 64u;
            if (word >= item.z)
                break;
            destination[item.x + word] = source[item.y + word];
        }
    }
}

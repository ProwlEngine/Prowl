// The GPU cull of one view, for renderables the buckets gave a shape, see Prowl.Graphite.Rendering.GpuCullPass. A thread
// a renderable tests it as BucketSet.CullObjects does and picks its level as BucketSet.Level does, then for each list of
// the view adds an instance to the command of every bucket its level draws, in one dispatch: every command's instances
// start where its record says, with room for every renderable of its bucket, so a thread claims the next of them by
// adding one to the command's count. In a view that culls what is hidden this is the early phase: it draws only what
// was in view last frame, and leaves the rest for Graphite/Internal/CullLate.glsl to test against the early depth.
Compute
{
    #extension GL_KHR_shader_subgroup_ballot : require
    #extension GL_KHR_shader_subgroup_vote : require

    layout(local_size_x = 64) in;

    #include "Graphite/Internal/CullCommon.glsl"

    void main()
    {
        uint index = gl_GlobalInvocationID.x;
        if (index >= view.counts.x)
            return;
        uint result = Cull(index);
        if (view.occlusion.x != 0u)
        {
            // A frozen cull draws what was in view when it froze and keeps it so
            bool seen = view.occlusion.w == 2u || (occlusion[index] & VisibleBit) != 0u;
            if (view.occlusion.w == 0u)
                occlusion[index] = result == 0u ? 0u : (seen ? EarlyBit : CandidateBit) | (result & 0xFFFFFFu);
            if (!seen)
                return;
        }
        if (result != 0u)
            EmitLists(index, objects[index].x - 1u, result & 0xFFu, int((result >> 8) & 0xFFu) - 1, (result >> 16) & 0xFFu, CullLateRole);
    }
}

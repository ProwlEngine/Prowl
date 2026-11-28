// The late phase of a view's GPU cull, once the early phase's renderables have drawn their depth: every renderable the
// early phase found in the frustum is tested against the view's farthest depth pyramid, which decides whether it counts
// as in view next frame, and the ones the early phase left out that pass are added to the lists the late phase fills.
// The pyramid's levels 1 to view.occlusion.y lie side by side in Cull_HiZ, see Graphite/Internal/OcclusionLevels.glsl.
// A renderable it leaves out keeps no LOD state of this frame, as one out of the frustum keeps none.
Compute
{
    #extension GL_KHR_shader_subgroup_ballot : require
    #extension GL_KHR_shader_subgroup_vote : require

    layout(local_size_x = 64) in;

    #define CULL_LATE 1
    #include "Graphite/Internal/CullCommon.glsl"

    void main()
    {
        uint index = gl_GlobalInvocationID.x;
        if (index >= view.counts.x)
            return;
        uint word = occlusion[index];
        if ((word & (EarlyBit | CandidateBit)) == 0u)
            return;
        vec3 center = (bounds[index * 3u].xyz - view.cameraHigh.xyz) + (bounds[index * 3u + 1u].xyz - view.cameraLow.xyz);
        bool hidden = Hidden(center, bounds[index * 3u + 2u].xyz);
        occlusion[index] = hidden ? 0u : VisibleBit;
        if (hidden && (word & CandidateBit) != 0u && view.counts.w != 0u)
            states[index].yz = uvec2(0u);
        if (hidden || (word & CandidateBit) == 0u)
            return;
        uint result = word & 0xFFFFFFu;
        EmitLists(index, objects[index].x - 1u, result & 0xFFu, int((result >> 8) & 0xFFu) - 1, (result >> 16) & 0xFFu, CullEarlyRole);
    }
}

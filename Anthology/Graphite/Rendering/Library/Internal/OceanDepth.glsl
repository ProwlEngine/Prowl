// The ocean's water depth around the viewer, a layer per slice, each slice twice the width of the one before and
// snapped to its own texels so they stay still in the world. Depth is metres from sea level down to the floor, deep
// where no height field reaches. Each height field draws over the slices in turn, the shallowest depth kept. The first
// texel also writes the viewer's wrapped position the slices were centred on this frame, which draws read.
// In level mode the same slices take the water's level instead, metres above sea level, 0 where no input reaches,
// each input over its area replacing what was there: a height field, or one height over the whole area. In flow mode
// they take the current in m/s, each input adding over its area: a flow map, or one velocity over the whole area.
Compute
{
    layout(local_size_x = 8, local_size_y = 8) in;

    layout(r32f) uniform image2DArray Ocean_Depth;
    layout(r32f) uniform image2DArray Ocean_Level;
    layout(rgba16f) uniform image2DArray Ocean_Flow;
    uniform sampler2D Ocean_Heights;
    layout(rgba32f) uniform writeonly image2D Ocean_State;

    layout(push_constant) uniform Push
    {
        vec4 slices;            // first slice's width, resolution, slice count, 1 to clear first
        vec4 viewer;            // the viewer's xz modulo the largest texel, 1 when a height field is bound, a flow's feather in metres
        vec4 area;              // the height field's corner relative to the viewer, its size
        vec4 heights;           // what texels scale by, what is added (a flow's x), mode (0 depth, 1 level, 2 flow), a flow's z
    } push;

    const float Deep = 1000.0;

    void main()
    {
        int resolution = int(push.slices.y);
        ivec3 id = ivec3(gl_GlobalInvocationID);
        if (id.x >= resolution || id.y >= resolution || id.z >= int(push.slices.z))
            return;
        float texel = push.slices.x * exp2(float(id.z)) / float(resolution);
        vec2 position = (vec2(id.xy) + 0.5 - 0.5 * float(resolution)) * texel - mod(push.viewer.xy, texel);

        vec2 uv = (position - push.area.xy) / push.area.zw;
        bool inside = all(greaterThanEqual(uv, vec2(0.0))) && all(lessThanEqual(uv, vec2(1.0)));
        if (push.heights.z > 1.5)
        {
            vec2 flow = push.slices.w > 0.5 ? vec2(0.0) : imageLoad(Ocean_Flow, id).xy;
            if (inside && push.area.z > 0.0)
            {
                vec2 edge = min(position - push.area.xy, push.area.xy + push.area.zw - position);
                float feather = clamp(min(edge.x, edge.y) / max(push.viewer.w, 1e-3), 0.0, 1.0);
                flow += feather * (push.viewer.z > 0.5 ? (textureLod(Ocean_Heights, uv, 0.0).rg - 0.5) * push.heights.x : push.heights.yw);
            }
            imageStore(Ocean_Flow, id, vec4(flow, 0.0, 0.0));
            return;
        }
        if (push.heights.z > 0.5)
        {
            float level = push.slices.w > 0.5 ? 0.0 : imageLoad(Ocean_Level, id).r;
            if (inside && push.area.z > 0.0)
                level = (push.viewer.z > 0.5 ? textureLod(Ocean_Heights, uv, 0.0).r * push.heights.x : 0.0) + push.heights.y;
            imageStore(Ocean_Level, id, vec4(level));
            return;
        }

        if (push.slices.w > 0.5 && id == ivec3(0))
            imageStore(Ocean_State, ivec2(0), vec4(push.viewer.xy, 0.0, 0.0));
        float depth = push.slices.w > 0.5 ? Deep : imageLoad(Ocean_Depth, id).r;
        if (push.viewer.z > 0.5 && inside)
            depth = min(depth, -(textureLod(Ocean_Heights, uv, 0.0).r * push.heights.x + push.heights.y));
        imageStore(Ocean_Depth, id, vec4(depth));
    }
}

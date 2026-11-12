// The GPU cull of one view, for renderables the buckets gave a shape, see Prowl.Graphite.Rendering.GpuCullPass. A thread
// a renderable tests it as BucketSet.CullObjects does and picks its level as BucketSet.Level does, then for each list of
// the view adds an instance to the command of every bucket its level draws. Mode 0 counts each command's instances, mode
// 1 sums the counts into where each command's instances start, mode 2 writes the instances and keeps the view's LOD state.
Compute
{
    layout(local_size_x = 64) in;

    struct CullList
    {
        uint pass;
        uint commandBase;      // the list's first GPU command in the view's commands
        uint commandCount;
        uint instanceBase;     // where the list's GPU instances start in the view's instances
        uint mapBase;          // where the list's bucket to command map starts
        uint fades;            // whether the list draws cross fades
        uint pad0;
        uint pad1;
    };

    layout(set = 0, binding = 0, std430) readonly buffer Cull_Objects { uvec4 objects[]; };
    layout(set = 0, binding = 1, std430) readonly buffer Cull_Shapes { uint shapes[]; };
    layout(set = 0, binding = 2, std430) readonly buffer Cull_Bounds { vec4 bounds[]; };
    layout(set = 0, binding = 3, std430) readonly buffer Cull_Attributes { uint attributes[]; };
    layout(set = 0, binding = 4, std430) readonly buffer Cull_Transforms { vec4 transforms[]; };
    layout(set = 0, binding = 5, std430) readonly buffer Cull_Chunks { uint chunks[]; };
    layout(set = 0, binding = 6, std430) readonly buffer Cull_View
    {
        vec4 planes[36];       // six a layer, relative to the camera
        uvec4 layerPlanes[2];  // planes each layer holds
        vec4 cameraHigh;
        vec4 cameraLow;
        vec4 lodHigh;          // where sizes are measured from
        vec4 lodLow;
        vec4 lod;              // scale, hysteresis, fade seconds, now
        ivec4 levels;          // offset, forced, finest, orthographic
        uvec4 masks;           // culling mask, hidden flags, caster filter, layers
        uvec4 counts;          // renderables, lists, stamp, whether the view keeps LOD state
        CullList lists[8];
    } view;
    layout(set = 0, binding = 7, std430) readonly buffer Cull_Maps { uint maps[]; };
    layout(set = 0, binding = 8, std430) buffer Cull_Commands { uint commands[]; };
    layout(set = 0, binding = 9, std430) buffer Cull_Records { uint records[]; };
    layout(set = 0, binding = 10, std430) buffer Cull_Instances { uint instances[]; };
    layout(set = 0, binding = 11, std430) buffer Cull_Cursors { uint cursors[]; };
    layout(set = 0, binding = 12, std430) buffer Cull_State { uvec4 states[]; };
    layout(push_constant) uniform Push { uint mode; } push;

    const uint CommandWords = 5u, RecordWords = 5u, AttributeWords = 28u, ChunkWords = 24u, Runs = 10u, LevelsPerPass = 8u;
    const uint StaticFlag = 1u, StaticOnly = 1u, None = 0xFFu;

    uint LayerPlanes(uint layer) { return view.layerPlanes[layer / 4u][layer % 4u]; }

    bool Sphere(vec3 center, float radius)
    {
        for (uint l = 0u; l < view.masks.w; l++)
        {
            bool inside = true;
            for (uint i = 0u; i < LayerPlanes(l) && inside; i++)
            {
                vec4 p = view.planes[l * 6u + i];
                inside = dot(p.xyz, center) + p.w >= -radius;
            }
            if (inside)
                return true;
        }
        return false;
    }

    bool Box(vec3 center, vec3 extents)
    {
        for (uint l = 0u; l < view.masks.w; l++)
        {
            bool inside = true;
            for (uint i = 0u; i < LayerPlanes(l) && inside; i++)
            {
                vec4 p = view.planes[l * 6u + i];
                inside = dot(p.xyz, center) + p.w >= -dot(abs(p.xyz), extents);
            }
            if (inside)
                return true;
        }
        return false;
    }

    // A chunk's box under the renderable's transform, relative to the camera
    bool ChunkVisible(uint index, uint chunk)
    {
        uint at = chunk * ChunkWords;
        vec3 low = uintBitsToFloat(uvec3(chunks[at + 12u], chunks[at + 13u], chunks[at + 14u]));
        vec3 high = uintBitsToFloat(uvec3(chunks[at + 16u], chunks[at + 17u], chunks[at + 18u]));
        vec3 c = (low + high) * 0.5, e = (high - low) * 0.5;
        vec4 r0 = transforms[index * 4u], r1 = transforms[index * 4u + 1u], r2 = transforms[index * 4u + 2u], rest = transforms[index * 4u + 3u];
        vec3 center = vec3(dot(r0.xyz, c), dot(r1.xyz, c), dot(r2.xyz, c)) + (vec3(r0.w, r1.w, r2.w) - view.cameraHigh.xyz) + (rest.xyz - view.cameraLow.xyz);
        vec3 extents = vec3(dot(abs(r0.xyz), e), dot(abs(r1.xyz), e), dot(abs(r2.xyz), e));
        return Box(center, extents);
    }

    uint Placed(uint shape, uint level) { return (shapes[shape + 9u] >> (level * 4u)) & 0xFu; }

    // Every bucket a level draws in a list's pass, outgoing levels of a cross fade untested by chunk as on the CPU
    void Emit(CullList list, uint shape, uint passes, uint level, uint index, bool fading, bool outgoing, uint fade)
    {
        if (list.pass >= passes)
            return;
        uint run = shapes[shape + Runs + list.pass * LevelsPerPass + level];
        uint first = run >> 16, count = run & 0xFFFFu;
        uint entries = shape + Runs + passes * LevelsPerPass + first * 2u;
        for (uint e = 0u; e < count; e++)
        {
            uint bucket = shapes[entries + e * 2u], chunk = shapes[entries + e * 2u + 1u];
            uint mapped = maps[list.mapBase + bucket];
            if (mapped == 0xFFFFFFFFu)
                continue;
            if ((chunk & 0x80000000u) != 0u && !outgoing && !ChunkVisible(index, chunk & 0x7FFFFFFFu))
                continue;
            bool faded = fading && (mapped & 0x80000000u) != 0u;
            if (outgoing && !faded)
                continue;
            uint command = list.commandBase + (mapped & 0x7FFFFFFFu) + (faded ? 1u : 0u);
            if (push.mode == 0u)
            {
                atomicAdd(commands[command * CommandWords + 1u], 1u);
                continue;
            }
            uint word = index | (faded ? fade << 24 | (outgoing ? 0x80000000u : 0u) : 0u);
            instances[atomicAdd(cursors[command], 1u)] = word;
        }
    }

    void Scan()
    {
        for (uint l = 0u; l < view.counts.y; l++)
        {
            CullList list = view.lists[l];
            uint at = list.instanceBase;
            for (uint c = 0u; c < list.commandCount; c++)
            {
                uint command = list.commandBase + c;
                records[command * RecordWords + 4u] = at;
                cursors[command] = at;
                at += commands[command * CommandWords + 1u];
            }
        }
    }

    void main()
    {
        if (push.mode == 1u)
        {
            if (gl_GlobalInvocationID.x == 0u)
                Scan();
            return;
        }
        uint index = gl_GlobalInvocationID.x;
        if (index >= view.counts.x)
            return;
        uvec4 object = objects[index];
        if (object.x == 0u)
            return;
        uint shape = object.x - 1u;

        uint flags = attributes[index * AttributeWords + 1u], layerMask = attributes[index * AttributeWords + 6u];
        if ((flags & view.masks.y) != 0u || (layerMask & view.masks.x) == 0u)
            return;
        if (view.masks.z != 0u && ((flags & StaticFlag) != 0u) != (view.masks.z == StaticOnly))
            return;
        vec4 centerHigh = bounds[index * 3u], centerLow = bounds[index * 3u + 1u];
        if (!Sphere((centerHigh.xyz - view.cameraHigh.xyz) + (centerLow.xyz - view.cameraLow.xyz), centerHigh.w))
            return;

        uint header = shapes[shape], levels = header & 0xFFu, passes = (header >> 8) & 0xFFu;
        bool keeps = view.counts.w != 0u, kept = false;
        uint stamp = view.counts.z;
        uvec4 state = keeps ? states[index] : uvec4(0u);
        uint level = 0u, keptLevel = 0u;
        if (levels > 1u)
        {
            int chosen = view.levels.y >= 0 ? view.levels.y : int(object.z);
            if (chosen < 0)
            {
                // A level coarser than the one the view last kept waits until the size falls the hysteresis share under its own
                float size = centerHigh.w * view.lod.x * uintBitsToFloat(object.y);
                if (view.levels.w == 0)
                    size /= max(length((centerHigh.xyz - view.lodHigh.xyz) + (centerLow.xyz - view.lodLow.xyz)), 1e-6);
                int previous = keeps && state.y == stamp - 1u ? int(state.x & 0xFFu) : -1;
                if (previous < 0 || previous > int(levels))
                    previous = int(levels);
                int sized = 0;
                while (sized < int(levels))
                {
                    float under = uintBitsToFloat(sized + 1 < int(levels) ? shapes[shape + 2u + uint(sized)] : shapes[shape + 1u]);
                    if (sized + 1 > previous)
                        under *= 1.0 - view.lod.y;
                    if (!(size < under))
                        break;
                    sized++;
                }
                if (sized == int(levels))
                    return;
                kept = true;
                keptLevel = uint(sized);
                chosen = max(sized + view.levels.x, view.levels.z);
            }
            level = Placed(shape, uint(min(chosen, int(levels) - 1)));
        }

        // A cross fade starts when the level changes from the one the last cull drew
        bool fadeView = view.lod.z > 0.0 && keeps && levels > 1u;
        uint drawn = (state.x >> 8) & 0xFFu, from = (state.x >> 16) & 0xFFu, drawnStamp = state.z;
        float start = uintBitsToFloat(state.w);
        int outgoing = -1;
        uint fade = 0u;
        if (fadeView)
        {
            if (drawnStamp != stamp - 1u)
                from = None;
            else if (drawn != level)
            {
                from = drawn;
                start = view.lod.w;
            }
            drawn = level;
            drawnStamp = stamp;
            if (from != None)
            {
                float progress = (view.lod.w - start) / view.lod.z;
                if (progress < 1.0)
                {
                    if (from < levels && Placed(shape, from) == from)
                    {
                        outgoing = int(from);
                        fade = uint(clamp(int(progress * 127.0), 1, 126));
                    }
                }
                else
                    from = None;
            }
        }

        for (uint l = 0u; l < view.counts.y; l++)
        {
            CullList list = view.lists[l];
            bool fading = outgoing >= 0 && list.fades != 0u;
            Emit(list, shape, passes, level, index, fading, false, fade);
            if (fading)
                Emit(list, shape, passes, uint(outgoing), index, true, true, fade);
        }

        if (push.mode == 2u && keeps)
        {
            if (kept)
                state = uvec4((state.x & ~0xFFu) | keptLevel, stamp, state.z, state.w);
            if (fadeView)
                state = uvec4((state.x & 0xFFu) | drawn << 8 | from << 16, state.y, drawnStamp, floatBitsToUint(start));
            states[index] = state;
        }
    }
}

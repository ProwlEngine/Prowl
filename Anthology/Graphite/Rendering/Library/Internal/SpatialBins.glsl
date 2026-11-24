// Bins a view layer's lights, decals and reflection probes for its fragments, see Graphite_BeginBins in Graphite/Trees.glsl
// and SpatialBinPass. A thread per set and depth bin finds the first and last of the set's items, nearest first, whose
// sphere reaches the bin along the layer's forward axis; a thread per set, screen tile and mask word marks which of its
// 32 items reach the tile's four side planes. The first thread writes the layer's header.
Compute
{
    #pragma resource_set 3
    #define GRAPHITE_NO_OBJECT 1

    layout(local_size_x = 64) in;
    #include "Graphite/Core.glsl"
    #include "Graphite/Trees.glsl"

    layout(std430) buffer Graphite_BinsWrite { uint bins[]; };
    layout(std430) readonly buffer Graphite_BinSpheres { vec4 spheres[]; };   // camera relative to the first layer, each set's after the one before

    uniform ivec4 _BinCounts;   // the binned items of lights, decals and probes, and the words of a layer
    uniform ivec4 _BinTiles;    // screen tiles across and down

    // The bins reach no farther than this, the last taking everything beyond
    #define BIN_REACH 4096.0

    // A sphere's centre relative to this layer's camera, and its radius grown by what rounding may move a point inside it
    vec4 LayerSphere(uint i, vec3 offset)
    {
        vec4 s = spheres[i];
        vec3 c = s.xyz - offset;
        return vec4(c, s.w + (s.w + length(c)) * 1e-5 + 1e-4);
    }

    void main()
    {
        uint thread = gl_GlobalInvocationID.x + gl_GlobalInvocationID.y * gl_NumWorkGroups.x * 64u;
        Graphite_ViewData view = Graphite_View();
        uint layerWords = uint(_BinCounts.w);
        uint base = 1u + graphite_Push.viewBase * layerWords;
        uvec3 counts = uvec3(_BinCounts.xyz);
        uvec3 words = (counts + 31u) / 32u;
        uvec2 tiles = uvec2(_BinTiles.xy);
        uint tileCount = tiles.x * tiles.y;
        uvec3 firstSphere = uvec3(0u, counts.x, counts.x + counts.y);
        uvec3 masks;
        masks.x = GRAPHITE_BIN_HEADER + GRAPHITE_BINNED_SETS * GRAPHITE_Z_BINS;
        masks.y = masks.x + tileCount * words.x;
        masks.z = masks.y + tileCount * words.y;
        vec3 offset = view.treeOffset.xyz;

        // The forward axis, toward what the middle of the screen sees, and log spaced bins from the near plane
        vec3 forward = normalize(vec3(view.view[0][2], view.view[1][2], view.view[2][2]));
        vec3 into = Graphite_RelativeFromDepth(view, vec2(0.5), 0.5) - Graphite_RelativeFromDepth(view, vec2(0.5), 1.0);
        if (dot(forward, into) < 0.0)
            forward = -forward;
        float near = max(view.depth.x, 1e-3);
        float far = max(min(view.depth.y, BIN_REACH), near * 16.0);
        float scale = float(GRAPHITE_Z_BINS) / log2(far / near);
        float bias = -log2(near) * scale;

        if (thread == 0u)
        {
            bins[0] = layerWords;
            bins[base] = floatBitsToUint(forward.x);
            bins[base + 1u] = floatBitsToUint(forward.y);
            bins[base + 2u] = floatBitsToUint(forward.z);
            bins[base + 3u] = floatBitsToUint(scale);
            bins[base + 4u] = floatBitsToUint(bias);
            bins[base + 5u] = tiles.x;
            bins[base + 6u] = tiles.y;
            for (uint s = 0u; s < GRAPHITE_BINNED_SETS; s++)
            {
                bins[base + 7u + s] = words[s];
                bins[base + 10u + s] = masks[s];
            }
        }

        // A depth bin: the first and last items whose depth span holds it, by the bin function fragments use
        if (thread < GRAPHITE_BINNED_SETS * GRAPHITE_Z_BINS)
        {
            uint set = thread / GRAPHITE_Z_BINS;
            uint bin = thread % GRAPHITE_Z_BINS;
            uint first = 0xFFFFu, last = 0u;
            for (uint i = 0u; i < counts[set]; i++)
            {
                vec4 s = LayerSphere(firstSphere[set] + i, offset);
                float d = dot(s.xyz, forward);
                if (Graphite_ZBin(d - s.w, scale, bias) <= bin && bin <= Graphite_ZBin(d + s.w, scale, bias))
                {
                    first = min(first, i);
                    last = i;
                }
            }
            bins[base + GRAPHITE_BIN_HEADER + thread] = first <= last ? first | (last << 16) : 0xFFFFFFFFu;
            return;
        }

        // A tile's mask word: the set, the tile and the word this thread marks
        uint t = thread - GRAPHITE_BINNED_SETS * GRAPHITE_Z_BINS;
        uint set = 0u;
        for (; set < GRAPHITE_BINNED_SETS; set++)
        {
            uint n = tileCount * words[set];
            if (t < n)
                break;
            t -= n;
        }
        if (set >= GRAPHITE_BINNED_SETS)
            return;
        uint tile = t / words[set];
        uint word = t % words[set];
        uvec2 at = uvec2(tile % tiles.x, tile / tiles.x);

        // The tile's corners, half a pixel out, as rays through two depths, which serves orthographic views too
        vec2 lo = (vec2(at * GRAPHITE_BIN_TILE) - 0.5) * view.screen.zw;
        vec2 hi = (vec2((at + 1u) * GRAPHITE_BIN_TILE) + 0.5) * view.screen.zw;
        vec2 uv[4] = vec2[4](lo, vec2(hi.x, lo.y), hi, vec2(lo.x, hi.y));
        vec3 nearCorner[4], farCorner[4];
        for (int k = 0; k < 4; k++)
        {
            nearCorner[k] = Graphite_RelativeFromDepth(view, uv[k], 1.0);
            farCorner[k] = Graphite_RelativeFromDepth(view, uv[k], 0.5);
        }
        vec3 middle = Graphite_RelativeFromDepth(view, (lo + hi) * 0.5, 0.75);
        vec4 planes[4];
        for (int k = 0; k < 4; k++)
        {
            vec3 n = cross(farCorner[k] - nearCorner[k], nearCorner[(k + 1) & 3] - nearCorner[k]);
            n *= inversesqrt(max(dot(n, n), 1e-30));
            if (dot(n, middle - nearCorner[k]) < 0.0)
                n = -n;
            planes[k] = vec4(n, -dot(n, nearCorner[k]));
        }

        uint mask = 0u;
        uint end = min(counts[set] - word * 32u, 32u);
        for (uint j = 0u; j < end; j++)
        {
            vec4 s = LayerSphere(firstSphere[set] + word * 32u + j, offset);
            bool inside = true;
            for (int k = 0; k < 4; k++)
                inside = inside && dot(planes[k].xyz, s.xyz) + planes[k].w >= -s.w;
            if (inside)
                mask |= 1u << j;
        }
        bins[base + masks[set] + tile * words[set] + word] = mask;
    }
}

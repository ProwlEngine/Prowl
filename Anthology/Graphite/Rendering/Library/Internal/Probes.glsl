// Samples the light probes at the bounds center of each requested renderable. The tetrahedron holding the point is
// found from the one it was in last, walking toward it through neighbours, then by scanning every tetrahedron since
// flat ones can stop a walk. Its corners blend by barycentrics. Outside the probe hull the nearest four probes blend by
// inverse squared distance.
Compute
{
    layout(local_size_x = 64) in;

    struct Graphite_ProbeSH { vec4 shAr; vec4 shAg; vec4 shAb; vec4 shBr; vec4 shBg; vec4 shBb; vec4 shC; };
    struct Graphite_Probe { vec4 high; vec4 low; Graphite_ProbeSH sh; };
    struct Graphite_ProbeTet { uvec4 corners; ivec4 neighbours; vec4 row1; vec4 row2; vec4 row3; };   // row1.w is 1 for a near flat one

    layout(set = 0, binding = 0, std430) readonly buffer Graphite_Probes { Graphite_Probe probes[]; };
    layout(set = 0, binding = 1, std430) readonly buffer Graphite_ProbeTets { Graphite_ProbeTet tets[]; };
    layout(set = 0, binding = 2, std430) readonly buffer Graphite_ProbeRequests { vec4 requests[]; };   // high and slot, then low
    layout(set = 0, binding = 3, std430) buffer Graphite_ProbeOutput { Graphite_ProbeSH outputs[]; };
    layout(push_constant) uniform Push { uint probeCount; uint tetCount; uint requestCount; uint pad; } push;

    #define INSIDE -1e-4
    #define MAX_WALK 64
    #define NO_TET 0xFFFFFFFFu
    #define DEGENERATE 0xFFFFFFFFu

    // A probe's position minus the point, exact when they are close however far both are from the origin
    vec3 FromPoint(uint probe, vec3 high, vec3 low)
    {
        precise vec3 d = (probes[probe].high.xyz - high) + (probes[probe].low.xyz - low);
        return d;
    }

    bool Barycentric(uint t, vec3 high, vec3 low, out vec4 b)
    {
        Graphite_ProbeTet tet = tets[t];
        b = vec4(0.0);
        if (tet.corners.x == DEGENERATE)
            return false;
        vec3 q = -FromPoint(tet.corners.x, high, low);
        b.y = dot(tet.row1.xyz, q);
        b.z = dot(tet.row2.xyz, q);
        b.w = dot(tet.row3.xyz, q);
        b.x = 1.0 - b.y - b.z - b.w;
        return true;
    }

    bool Contains(vec4 b) { return all(greaterThanEqual(b, vec4(INSIDE))); }

    void Accumulate(inout Graphite_ProbeSH sum, uint probe, float w)
    {
        Graphite_ProbeSH sh = probes[probe].sh;
        sum.shAr += sh.shAr * w;
        sum.shAg += sh.shAg * w;
        sum.shAb += sh.shAb * w;
        sum.shBr += sh.shBr * w;
        sum.shBg += sh.shBg * w;
        sum.shBb += sh.shBb * w;
        sum.shC += sh.shC * w;
    }

    // The tetrahedron holding the point, or NO_TET outside the hull. last is where the walk ended, the next one's start.
    uint FindTet(uint hint, vec3 high, vec3 low, out vec4 b, out uint last)
    {
        last = hint;
        if (push.tetCount == 0u)
            return NO_TET;

        uint t = hint < push.tetCount ? hint : 0u;
        for (int step = 0; step < MAX_WALK; step++)
        {
            if (!Barycentric(t, high, low, b))
                break;
            last = t;
            if (Contains(b))
                return t;

            int k = 0;
            float lowest = b.x;
            if (b.y < lowest) { lowest = b.y; k = 1; }
            if (b.z < lowest) { lowest = b.z; k = 2; }
            if (b.w < lowest) { k = 3; }
            int next = tets[t].neighbours[k];
            if (next < 0)
            {
                // Past a hull face of a sound tetrahedron the point is outside the hull, past a sliver's the scan decides
                if (tets[t].row1.w < 0.5)
                    return NO_TET;
                break;
            }
            t = uint(next);
        }

        for (uint i = 0u; i < push.tetCount; i++)
        {
            if (Barycentric(i, high, low, b) && Contains(b))
            {
                last = i;
                return i;
            }
        }
        return NO_TET;
    }

    void main()
    {
        uint request = gl_GlobalInvocationID.x;
        if (request >= push.requestCount)
            return;

        vec4 high = requests[request * 2u];
        vec3 low = requests[request * 2u + 1u].xyz;
        uint slot = floatBitsToUint(high.w);
        uint hint = floatBitsToUint(outputs[slot].shC.w);

        Graphite_ProbeSH sum = Graphite_ProbeSH(vec4(0.0), vec4(0.0), vec4(0.0), vec4(0.0), vec4(0.0), vec4(0.0), vec4(0.0));
        vec4 b;
        uint last;
        uint t = FindTet(hint, high.xyz, low, b, last);
        if (t != NO_TET)
        {
            uvec4 corners = tets[t].corners;
            Accumulate(sum, corners.x, b.x);
            Accumulate(sum, corners.y, b.y);
            Accumulate(sum, corners.z, b.z);
            Accumulate(sum, corners.w, b.w);
        }
        else
        {
            uint nearest[4] = uint[4](0u, 0u, 0u, 0u);
            float dist2[4] = float[4](3.4e38, 3.4e38, 3.4e38, 3.4e38);
            uint k = min(push.probeCount, 4u);
            for (uint i = 0u; i < push.probeCount; i++)
            {
                vec3 d = FromPoint(i, high.xyz, low);
                float dsq = dot(d, d);
                if (dsq >= dist2[k - 1u])
                    continue;
                uint j = k - 1u;
                while (j > 0u && dist2[j - 1u] > dsq)
                {
                    dist2[j] = dist2[j - 1u];
                    nearest[j] = nearest[j - 1u];
                    j--;
                }
                dist2[j] = dsq;
                nearest[j] = i;
            }

            float total = 0.0;
            for (uint j = 0u; j < k; j++)
                total += 1.0 / (dist2[j] + 1e-6);
            for (uint j = 0u; j < k; j++)
                Accumulate(sum, nearest[j], (1.0 / (dist2[j] + 1e-6)) / total);
        }

        sum.shC.w = uintBitsToFloat(last);
        outputs[slot] = sum;
    }
}

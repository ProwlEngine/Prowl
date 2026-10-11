// Samples the light probes at the bounds center of each requested renderable. The tetrahedron holding the point is
// found from the one it was in last, walking toward it through neighbours, then by scanning every tetrahedron since
// flat ones can stop a walk. Its corners blend by barycentrics. Outside the probe hull the nearest four probes blend by
// inverse squared distance.
Compute
{
    layout(local_size_x = 64) in;

    struct Graphite_ProbeSH { vec4 shAr; vec4 shAg; vec4 shAb; vec4 shBr; vec4 shBg; vec4 shBb; vec4 shC; };
    struct Graphite_Probe { vec4 position; Graphite_ProbeSH sh; };
    struct Graphite_ProbeTet { uvec4 corners; ivec4 neighbours; vec4 row1; vec4 row2; vec4 row3; };

    layout(set = 0, binding = 0, std430) readonly buffer Graphite_Probes { Graphite_Probe probes[]; };
    layout(set = 0, binding = 1, std430) readonly buffer Graphite_ProbeTets { Graphite_ProbeTet tets[]; };
    layout(set = 0, binding = 2, std430) readonly buffer Graphite_ProbeRequests { vec4 requests[]; };
    layout(set = 0, binding = 3, std430) buffer Graphite_ProbeOutput { Graphite_ProbeSH outputs[]; };
    layout(push_constant) uniform Push { uint probeCount; uint tetCount; uint requestCount; uint pad; } push;

    #define INSIDE -1e-4
    #define MAX_WALK 64
    #define NO_TET 0xFFFFFFFFu
    #define DEGENERATE 0xFFFFFFFFu

    bool Barycentric(uint t, vec3 p, out vec4 b)
    {
        Graphite_ProbeTet tet = tets[t];
        b = vec4(0.0);
        if (tet.corners.x == DEGENERATE)
            return false;
        b.y = dot(tet.row1.xyz, p) + tet.row1.w;
        b.z = dot(tet.row2.xyz, p) + tet.row2.w;
        b.w = dot(tet.row3.xyz, p) + tet.row3.w;
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

    uint FindTet(uint hint, vec3 p, out vec4 b)
    {
        if (push.tetCount == 0u)
            return NO_TET;

        uint t = hint < push.tetCount ? hint : 0u;
        for (int step = 0; step < MAX_WALK; step++)
        {
            if (!Barycentric(t, p, b))
                break;
            if (Contains(b))
                return t;

            int k = 0;
            float lowest = b.x;
            if (b.y < lowest) { lowest = b.y; k = 1; }
            if (b.z < lowest) { lowest = b.z; k = 2; }
            if (b.w < lowest) { k = 3; }
            int next = tets[t].neighbours[k];
            if (next < 0)
                break;
            t = uint(next);
        }

        for (uint i = 0u; i < push.tetCount; i++)
        {
            if (Barycentric(i, p, b) && Contains(b))
                return i;
        }
        return NO_TET;
    }

    void main()
    {
        uint request = gl_GlobalInvocationID.x;
        if (request >= push.requestCount)
            return;

        vec3 p = requests[request].xyz;
        uint slot = floatBitsToUint(requests[request].w);
        uint hint = floatBitsToUint(outputs[slot].shC.w);

        Graphite_ProbeSH sum = Graphite_ProbeSH(vec4(0.0), vec4(0.0), vec4(0.0), vec4(0.0), vec4(0.0), vec4(0.0), vec4(0.0));
        vec4 b;
        uint t = FindTet(hint, p, b);
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
                vec3 d = probes[i].position.xyz - p;
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

        sum.shC.w = uintBitsToFloat(t);
        outputs[slot] = sum;
    }
}

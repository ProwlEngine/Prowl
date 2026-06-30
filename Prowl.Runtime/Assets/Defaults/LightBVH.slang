module LightBVH;

// Stackless rope-BVH light traversal for forward rendering. The light/node textures are owned by
// the Lighting module's Lights block and passed in as parameters; this module is pure logic.

public static const int LBVH_MAX_NODE_VISITS = 4096;

public struct LightSample
{
    public float3 Position;
    public float Range;
    public float3 Color;
    public float Intensity;
    public float3 Direction;
    public int Type;            // 0 directional, 1 point, 2 spot
    public float SpotCos;       // cos(outer)
    public float InnerSpotCos;  // cos(inner)
    public float ShadowBias;
    public float ShadowNormalBias;
    public float ShadowStrength;
    public float ShadowQuality;
    public int ShadowSlot;      // -1 if no atlas slot this frame
    public int ShadowEnabled;   // 0 / 1
}

// Wrap a linear texel index into a 2D coordinate. dim must be a power of 2 and shift == log2(dim).
public int2 LBVH_Coord(int linearIdx, int dim, int shift)
{
    int mask = dim - 1;
    return int2(linearIdx & mask, linearIdx >> shift);
}

public LightSample LBVH_FetchLight(Sampler2D<float4> tex, int dim, int shift, int slot)
{
    int base = slot * 5;
    float4 t0 = tex.Load(int3(LBVH_Coord(base + 0, dim, shift), 0));
    float4 t1 = tex.Load(int3(LBVH_Coord(base + 1, dim, shift), 0));
    float4 t2 = tex.Load(int3(LBVH_Coord(base + 2, dim, shift), 0));

    int typeAndFlags = asint(t2.w);
    int type = typeAndFlags & 3;
    int shadowEnabled = (typeAndFlags >> 2) & 1;

    LightSample L;
    L.Position      = t0.xyz;
    L.Range         = t0.w;
    L.Color         = t1.rgb;
    L.Intensity     = t1.w;
    L.Direction     = t2.xyz;
    L.Type          = type;
    L.ShadowEnabled = shadowEnabled;

    if (type == 2 || shadowEnabled != 0)
    {
        float4 t3 = tex.Load(int3(LBVH_Coord(base + 3, dim, shift), 0));
        L.SpotCos          = t3.x;
        L.InnerSpotCos     = t3.y;
        L.ShadowBias       = t3.z;
        L.ShadowNormalBias = t3.w;
    }
    else
    {
        L.SpotCos          = -1.0;
        L.InnerSpotCos     =  1.0;
        L.ShadowBias       =  0.0;
        L.ShadowNormalBias =  0.0;
    }

    if (shadowEnabled != 0)
    {
        float4 t4 = tex.Load(int3(LBVH_Coord(base + 4, dim, shift), 0));
        L.ShadowStrength = t4.x;
        L.ShadowQuality  = t4.y;
        L.ShadowSlot     = asint(t4.z);
    }
    else
    {
        L.ShadowStrength = 0.0;
        L.ShadowQuality  = 0.0;
        L.ShadowSlot     = -1;
    }
    return L;
}

public struct LBVH_Iter
{
    public int current;
}

public void LBVH_Begin(out LBVH_Iter it, int root)
{
    it.current = root;
}

public int LBVH_Next(inout LBVH_Iter it, Sampler2D<float4> nodeTex, int dim, int shift, float3 worldPos)
{
    int budget = LBVH_MAX_NODE_VISITS;
    while (it.current >= 0 && budget > 0)
    {
        budget--;
        int base = it.current * 2;
        float4 lo = nodeTex.Load(int3(LBVH_Coord(base + 0, dim, shift), 0));
        float4 hi = nodeTex.Load(int3(LBVH_Coord(base + 1, dim, shift), 0));

        int hitNext  = asint(lo.w);
        int missNext = asint(hi.w);

        if (hitNext < 0)
        {
            float3 c = (lo.xyz + hi.xyz) * 0.5;
            float r = (hi.x - lo.x) * 0.5;
            float3 d = worldPos - c;
            it.current = missNext;
            if (dot(d, d) <= r * r)
                return -hitNext - 1;
        }
        else
        {
            bool inside =
                worldPos.x >= lo.x && worldPos.x <= hi.x &&
                worldPos.y >= lo.y && worldPos.y <= hi.y &&
                worldPos.z >= lo.z && worldPos.z <= hi.z;
            it.current = inside ? hitNext : missNext;
        }
    }
    return -1;
}

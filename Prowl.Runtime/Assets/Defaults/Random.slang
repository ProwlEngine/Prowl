module Random;

__exported import ShaderVariables;

public uint triple32(uint x) {
	// https://nullprogram.com/blog/2018/07/31/
	x ^= x >> 17;
	x *= 0xed5ad4bbu;
	x ^= x >> 11;
	x *= 0xac4c1b51u;
	x ^= x >> 15;
	x *= 0x31848babu;
	x ^= x >> 14;
	return uint(x);
}

// Slang forbids the GLSL mutable-global pattern, so the per-fragment state is an explicit struct
// the caller threads through (seed it once from SV_Position via SeedRand).
public struct RandState { public uint state; };

public RandState SeedRand(float2 fragCoord) {
	RandState r;
	r.state = triple32(uint(fragCoord.x + int(Frame._ScreenParams.x) * fragCoord.y)
		+ uint(Frame._ScreenParams.x * Frame._ScreenParams.y) * uint(Frame._ScreenParams.w));
	return r;
}

public uint RandNext(inout RandState r) { r.state = triple32(r.state); return r.state; }
public uint2 RandNext2(inout RandState r) { return uint2(RandNext(r), RandNext(r)); }
public uint3 RandNext3(inout RandState r) { return uint3(RandNext2(r), RandNext(r)); }
public uint4 RandNext4(inout RandState r) { return uint4(RandNext3(r), RandNext(r)); }
public float RandNextF(inout RandState r) { return float(RandNext(r)) / float(0xffffffffu); }
public float2 RandNext2F(inout RandState r) { return float2(RandNext2(r)) / float(0xffffffffu); }
public float3 RandNext3F(inout RandState r) { return float3(RandNext3(r)) / float(0xffffffffu); }
public float4 RandNext4F(inout RandState r) { return float4(RandNext4(r)) / float(0xffffffffu); }

public float RandF (uint  seed) { return float(triple32(seed))                    / float(0xffffffffu); }
public float2 Rand2F(uint2 seed) { return float2(triple32(seed.x), triple32(seed.y)) / float(0xffffffffu); }

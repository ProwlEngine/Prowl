// Procedural terrain detail placement.
//
// Every blade is derived from its own cell on a fixed world grid, so its position, size and
// orientation depend only on where it is, never on where the camera is. Cascades sample every
// 2^level-th cell of that same grid, which makes each one a strict subset of the cascade inside
// it: distance thins the field out, it never moves a blade.

// Terrain sources. Declared here rather than by the includer so every scatter consumer samples
// the height field the same way the surface does.
uniform sampler2D _Heightmap;
uniform float _TerrainSize;
uniform float _TerrainHeight;

uniform vec2 _ScatterOriginCell;   // fine-grid cell index of this ring's lower-left corner
uniform float _ScatterCellSize;    // world size of one fine cell
uniform int _ScatterCellsPerSide;  // instances per side, instance count is the square of this
uniform int _ScatterLevel;         // ring index, so this ring steps 2^level fine cells
uniform int _ScatterMaxLevel;      // level of the outermost ring
uniform vec2 _ScatterCentre;       // ring centre in terrain-local XZ
uniform vec2 _ScatterRadii;        // inner and outer radius of this cascade's band
uniform sampler2D _DetailMap;
uniform int _DetailChannel;

// How far a blade may wander, as a power of two in fine cells. Mirrored by
// TerrainDetailRenderer.kMaxJitterLevel, which insets the ring radii to match.
#define SCATTER_MAX_JITTER 3

// Vertex UV -> texel-center UV. Heights are a vertex grid, so sample 0 sits on the terrain edge.
vec2 scatterHeightUV(vec2 uv)
{
    vec2 s = vec2(textureSize(_Heightmap, 0));
    return uv * (s - 1.0) / s + 0.5 / s;
}

#ifdef TERRAIN_BICUBIC
// Same 4-tap Catmull-Rom the terrain surface runs, so blades sit exactly on the ground
// rather than a curvature error above or below it.
float scatterSampleHeight(vec2 uv)
{
    vec2 texSize = vec2(textureSize(_Heightmap, 0));
    vec2 invTexSize = 1.0 / texSize;

    vec2 coord = uv * (texSize - 1.0);
    vec2 f = fract(coord);
    coord -= f;

    vec2 f2 = f * f; vec2 f3 = f2 * f;
    vec2 w0 = -0.5 * f3 + f2 - 0.5 * f;
    vec2 w1 = 1.5 * f3 - 2.5 * f2 + 1.0;
    vec2 w2 = -1.5 * f3 + 2.0 * f2 + 0.5 * f;
    vec2 w3 = 0.5 * f3 - 0.5 * f2;

    vec2 s0 = max(w0 + w1, vec2(1e-5));
    vec2 s1 = max(w2 + w3, vec2(1e-5));
    vec2 fa = w1 / s0;
    vec2 fb = w3 / s1;

    vec2 t0 = (coord - 0.5 + fa) * invTexSize;
    vec2 t1 = (coord + 1.5 + fb) * invTexSize;

    float h00 = texture(_Heightmap, vec2(t0.x, t0.y)).r;
    float h10 = texture(_Heightmap, vec2(t1.x, t0.y)).r;
    float h01 = texture(_Heightmap, vec2(t0.x, t1.y)).r;
    float h11 = texture(_Heightmap, vec2(t1.x, t1.y)).r;

    float row0 = mix(h00, h10, s1.x / (s0.x + s1.x));
    float row1 = mix(h01, h11, s1.x / (s0.x + s1.x));
    return mix(row0, row1, s1.y / (s0.y + s1.y)) * _TerrainHeight;
}
#else
float scatterSampleHeight(vec2 uv)
{
    return texture(_Heightmap, scatterHeightUV(uv)).r * _TerrainHeight;
}
#endif

// Value noise matching TerrainGrassRenderer.NoiseAt so painted variation looks the same on
// both paths (broad dry/healthy patches rather than per-blade static).
float scatterHashN(int x, int z)
{
    uint h = uint(x * 73856093 ^ z * 19349663);
    h ^= h >> 16u; h *= 0x45d9f3bu; h ^= h >> 16u;
    return float(h & 0xFFFFu) / 65535.0;
}

float scatterNoise(float x, float z)
{
    int ix = int(floor(x)), iz = int(floor(z));
    float fx = x - float(ix), fz = z - float(iz);
    float sx = fx * fx * (3.0 - 2.0 * fx);
    float sz = fz * fz * (3.0 - 2.0 * fz);
    float n0 = mix(scatterHashN(ix, iz), scatterHashN(ix + 1, iz), sx);
    float n1 = mix(scatterHashN(ix, iz + 1), scatterHashN(ix + 1, iz + 1), sx);
    return mix(n0, n1, sz);
}

// One well-mixed value per cell and salt. Each component is an independent hash rather than a
// step of one sequence: chained generators leave their low bits correlated, which lines the
// blades up along the cell grid instead of filling it.
float scatterRandom(ivec2 cell, uint salt)
{
    uint h = uint(cell.x) * 0x8da6b343u + uint(cell.y) * 0xd8163841u + salt * 0xcb1ab31fu;
    h ^= h >> 15u; h *= 0x2c1b3c6du;
    h ^= h >> 12u; h *= 0x297a2d39u;
    h ^= h >> 15u;
    return float(h >> 8u) / 16777215.0;
}

struct ScatterBlade
{
    bool valid;
    vec2 terrainUV;   // 0..1 across the terrain
    vec2 localXZ;     // terrain-local position
    float density;    // painted density at this spot, 0..1
    float rotation;   // 0..2pi
    float windPhase;  // 0..2pi
    float noise;      // smooth 0..1 variation for size and colour
    float fade;       // shrinks blades the next ring out will not draw
};

// Resolve one instance into a blade. Returns valid = false when the instance falls outside the
// ring's annulus, off the terrain, or loses the density test, and the caller collapses the quad.
ScatterBlade scatterResolve(int instanceID, float terrainSize, float noiseSpread)
{
    ScatterBlade blade;
    blade.valid = false;
    blade.terrainUV = vec2(0.0);
    blade.localXZ = vec2(0.0);
    blade.density = 0.0;
    blade.rotation = 0.0;
    blade.windPhase = 0.0;
    blade.noise = 0.0;
    blade.fade = 1.0;

    int n = _ScatterCellsPerSide;
    int stride = 1 << _ScatterLevel;
    ivec2 fine = ivec2(_ScatterOriginCell) + ivec2(instanceID % n, instanceID / n) * stride;

    // The coarsest ring that still draws this cell decides how far its blade may wander. A blade
    // kept by a ring whose cells are 8 fine cells apart spreads across all 8, so distant rings
    // fill their spacing evenly instead of sitting on a visible lattice.
    int lsbX = findLSB(uint(fine.x));
    int lsbZ = findLSB(uint(fine.y));
    int level = min(lsbX < 0 ? 31 : lsbX, lsbZ < 0 ? 31 : lsbZ);
    int jitterLevel = clamp(level, 0, min(_ScatterMaxLevel, SCATTER_MAX_JITTER));
    float spread = float(1 << jitterLevel);

    float jitterX = scatterRandom(fine, 0u);
    float jitterZ = scatterRandom(fine, 1u);
    float rotation = scatterRandom(fine, 2u);
    float keep = scatterRandom(fine, 3u);

    vec2 localXZ = (vec2(fine) + 0.5 + (vec2(jitterX, jitterZ) - 0.5) * spread) * _ScatterCellSize;

    // Rings are circles around the camera, so grass reaches equally far in every direction
    float dist = length(localXZ - _ScatterCentre);
    if (dist < _ScatterRadii.x || dist >= _ScatterRadii.y) return blade;

    vec2 uv = localXZ / terrainSize;
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) return blade;

    float density = texture(_DetailMap, uv)[_DetailChannel];
    if (keep >= density) return blade;

    // A blade shrinks away across the band that drops it, so density decays smoothly outward
    // instead of stepping down at a cascade edge. In the last cascade every blade is dropped,
    // which is what fades the field out at the draw distance.
    bool dropped = level == _ScatterLevel || _ScatterLevel == _ScatterMaxLevel;
    if (dropped)
    {
        blade.fade = 1.0 - smoothstep(_ScatterRadii.x, _ScatterRadii.y, dist);
        if (blade.fade <= 0.0) return blade;
    }

    blade.valid = true;
    blade.terrainUV = uv;
    blade.localXZ = localXZ;
    blade.density = density;
    blade.rotation = rotation * 6.28318531;
    blade.windPhase = jitterX * 6.28318531;
    blade.noise = scatterNoise(localXZ.x * noiseSpread, localXZ.y * noiseSpread);
    return blade;
}

#pragma variant GI PROBES TRACE PROBE_AO RESTIR_TEMPORAL RESTIR_SPATIAL SH OCCLUSION GATHER SPLIT TEMPORAL FILTER REFERENCE OUTPUT VISIBILITY
#pragma variant GI_GATHER_OCCLUSION
#pragma variant GI_SPLIT_VERTICAL
// World space GI from screen probes, one pass a define. A probe sits at a jittered pixel of each GI_PROBE square of the
// view, its sixteen rays spread over the hemisphere of its normal, a 4 x 4 cell each, traced through last frame's image
// and then the world's voxels, lit by their hit light, the radiance cache and the sky. Each cell keeps a ReSTIR
// reservoir over frames, its sample a hit point, resampled with the same cell of last frame's nearest probe and then of
// probes around it, each sample moved to the probe reusing it with the change in solid angle that brings. Samples live
// GI_MAX_AGE frames, so changed light shows within them. Each probe projects its cells onto L2 harmonics, every pixel
// takes one of the 5 x 5 probes around it, and a temporal denoiser smooths what that leaves.
// Light is at the view's exposure. Past the voxel box rays take the sky where the screen finds nothing, above the
// horizon and as far as the sun's shadow where the screen lost them says it reaches.
//   GI_PROBES          the probe's normal, octahedral, its reversed Z depth and camera distance, 0 where it found no
//                      surface, at probe resolution, and a second target its camera relative position
//   GI_TRACE           a ray's light and hit distance in metres, GI_FAR where it reached the sky, at 4x probe
//                      resolution: through the depth pyramid against last frame's colour, then through the voxels, and
//                      a second target the hit's normal
//   GI_PROBE_AO        how open each probe is, its rays' cosine weighted hit distance over GI_AO_RANGE, then how much
//                      of its cosine weighted hemisphere reaches the sky, each 0 to 1
//   GI_RESTIR_TEMPORAL the ray merged into the reservoir of the same cell of last frame's probe nearest where this one
//                      was on the same surface
//   GI_RESTIR_SPATIAL  each reservoir merged with the same cell of probes on a disk around, by plane and normal,
//                      the disk a world size shrunk where the probe is closed in
//   GI_SH              each probe's cells as L2 harmonics of irradiance over pi, 3 x 3 texels a probe
//   GI_OCCLUSION       each pixel's bent normal, octahedral, and visibility from a horizon search over slices of the
//                      depth, each slice's half disc kept as a mask of sectors the surfaces it shows cover
//   GI_GATHER          irradiance over pi at each pixel from one of the 5 x 5 probes around, picked by their weights,
//                      and in alpha the sky's visibility, their weighted mean. With GI_GATHER_OCCLUSION the light comes
//                      along the bent normal within the cone left open and the sky's visibility is occluded the same.
//   GI_SPLIT_SURFACE   each texel's camera relative position and, in alpha, the settled history's brightness where it
//                      was last frame, -1 where it has no surface, and a second target its octahedral normal
//   GI_SPLIT           the gather blurred along a row, or with GI_SPLIT_VERTICAL a column, over GI_SPLIT_TAPS each
//                      way, kept on the pixel's surface and to light as bright as the settled history found there, so
//                      the blur follows the light's edges without the noise breaking them. The sky's visibility in
//                      alpha is blurred the same.
//   GI_TEMPORAL        the gather blended into last frame's, reprojected, rejected by distance and normal, clipped to
//                      the 7 x 7 around it, and a second target holding each pixel's camera distance, normal and frames
//                      held for next frame's rejection
//   GI_REFERENCE       the truth to compare against: irradiance over pi at every pixel path traced through the voxels
//                      alone, averaged over the frames the camera holds still
//   GI_FILTER          the history blurred over the 5 x 5 around each pixel on its surface, the blur's step wider the
//                      fewer frames the pixel's history holds, so new pixels lose their noise and settled ones keep detail
//   GI_OUTPUT          the filtered history as the view's screen irradiance, or none while the world has no box
//   GI_VISIBILITY      the history's sky visibility as the view's screen visibility, which occludes the sky's
//                      reflection, or full visibility while the world has no box
#include "Graphite/Internal/PostView.glsl"

#define GI_PROBE 8
#define GI_CELLS 4
#define GI_FAR 1000.0               // the hit distance of a ray that reached the sky
#define GI_MAX_M 20.0
#define GI_MAX_LIGHT 10.0
#define GI_SAME_DISTANCE 0.1        // how far apart, relative, two camera distances may be on one surface
#define GI_SAME_NORMAL 0.9          // how near the same way two normals must face on one surface

uniform vec2 _ScreenSize;       // the view's pixels, which probes and the scene targets are laid over
uniform vec2 _ProbeJitter;      // where in its square this frame's probes sit, 0 to 1
uniform int _Frame;
uniform vec4 _BoxMin;           // the box's first corner relative to the first layer's camera in metres, the voxel size
uniform vec3 _BoxSize;          // the box's size in metres

// A random number from 0 to 1 for three keys
float GI_Random(uint a, uint b, uint c) { return float(Graphite_Hash(a ^ Graphite_Hash(b ^ Graphite_Hash(c))) & 0xFFFFFFu) / 16777216.0; }

ivec2 GI_ProbePixel(ivec2 probe, vec2 jitter) { return min(probe * GI_PROBE + ivec2(jitter * float(GI_PROBE)), ivec2(_ScreenSize) - 1); }

ivec2 GI_ProbePixel(ivec2 probe) { return GI_ProbePixel(probe, _ProbeJitter); }

vec2 GI_PixelUV(ivec2 pixel) { return (vec2(pixel) + 0.5) / _ScreenSize; }

bool GI_OnScreen(vec2 uv) { return all(greaterThanEqual(uv, vec2(0.0))) && all(lessThan(uv, vec2(1.0))); }

ivec2 GI_CellTexel(ivec2 probe, int cell) { return probe * GI_CELLS + ivec2(cell % GI_CELLS, cell / GI_CELLS); }

// Whether two camera distances lie within a tolerance of the second
bool GI_SameDistance(float distance, float other, float tolerance) { return abs(distance - other) <= tolerance * other; }

// The tangents a probe's cells turn around its normal from: the up axis on walls and x on floors and ceilings, so they
// stay put as the normal wobbles over a surface
void GI_CellFrame(vec3 normal, out vec3 t, out vec3 b)
{
    t = normalize(cross(normal, abs(normal.y) < 0.7 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0)));
    b = cross(normal, t);
}

// A direction in a cell of the hemisphere around a normal, u its place in the cell, cos theta even so every cell
// carries the same solid angle
vec3 GI_CellDirection(int cell, vec2 u, vec3 normal)
{
    u = (vec2(cell % GI_CELLS, cell / GI_CELLS) + u) / float(GI_CELLS);
    float r = sqrt(max(1.0 - u.x * u.x, 0.0));
    float phi = GRAPHITE_TWO_PI * u.y;
    vec3 t, b;
    GI_CellFrame(normal, t, b);
    return normalize(t * (r * cos(phi)) + b * (r * sin(phi)) + normal * u.x);
}

// The cell of the hemisphere around a normal with its cell frame a direction lies in, -1 below it
int GI_CellIn(vec3 direction, vec3 normal, vec3 t, vec3 b)
{
    float c = dot(direction, normal);
    if (c <= 0.0)
        return -1;
#if GI_CELLS == 4
    // The quarter turn the direction lies in around the normal, its edges where atan2 puts them
    float x = dot(direction, t), y = dot(direction, b);
    int quarter = y > 0.0 ? (x > 0.0 ? 0 : 1) : y < 0.0 ? (x < 0.0 ? 2 : 3) : (x < 0.0 ? 2 : 0);
    return min(int(c * float(GI_CELLS)), GI_CELLS - 1) + quarter * GI_CELLS;
#else
    float phi = atan(dot(direction, b), dot(direction, t));
    float u = (phi < 0.0 ? phi + GRAPHITE_TWO_PI : phi) / GRAPHITE_TWO_PI;
    return min(int(c * float(GI_CELLS)), GI_CELLS - 1) + min(int(u * float(GI_CELLS)), GI_CELLS - 1) * GI_CELLS;
#endif
}

int GI_CellOf(vec3 direction, vec3 normal)
{
    vec3 t, b;
    GI_CellFrame(normal, t, b);
    return GI_CellIn(direction, normal, t, b);
}

// A probe's ray this frame, jittered inside its cell
vec3 GI_RayDirection(ivec2 probe, int cell, vec3 normal)
{
    uint h = Graphite_Hash(uint(probe.x) * 73856093u ^ uint(probe.y) * 19349663u ^ uint(cell) * 83492791u ^ uint(_Frame) * 2654435761u);
    return GI_CellDirection(cell, vec2(h & 0xFFFFu, h >> 16u) / 65536.0, normal);
}

// A camera relative point in the box's voxels from its first corner
vec3 GI_VoxelPosition(Graphite_ViewData view, vec3 relative) { return (relative - (_BoxMin.xyz - view.treeOffset.xyz)) / _BoxMin.w; }

// Whether a camera relative point lies in the voxel box, at least a voxel in from its faces
bool GI_InBox(Graphite_ViewData view, vec3 relative)
{
    vec3 v = GI_VoxelPosition(view, relative);
    vec3 inside = min(v, _BoxSize / _BoxMin.w - v);
    return min(min(inside.x, inside.y), inside.z) > 1.0;
}

#if !defined(GI_PROBES)
uniform GRAPHITE_SCREEN_TEXTURE _ProbePositions;

vec3 GI_ProbePosition(ivec2 probe) { return Graphite_Fetch(_ProbePositions, probe, 0).xyz; }
#endif

// How much a probe's light suits a point: on its plane and facing its way
float GI_ProbeAgreement(vec3 normal, vec3 relative, vec3 otherNormal, vec3 otherPosition)
{
    float scale = max(length(relative) * 0.02, 0.01);
    float plane = abs(dot(otherPosition - relative, normal)) / scale;
    float facing = max(dot(otherNormal, normal), 0.0);
    return exp2(-plane * plane) * facing * facing;
}

float GI_SHBasis(int k, vec3 d)
{
    switch (k)
    {
        case 0: return 0.282095;
        case 1: return 0.488603 * d.y;
        case 2: return 0.488603 * d.z;
        case 3: return 0.488603 * d.x;
        case 4: return 1.092548 * d.x * d.y;
        case 5: return 1.092548 * d.y * d.z;
        case 6: return 0.315392 * (3.0 * d.z * d.z - 1.0);
        case 7: return 1.092548 * d.x * d.z;
        default: return 0.546274 * (d.x * d.x - d.y * d.y);
    }
}

// One cell's sample and its weight: light at the view's exposure, its direction and hit distance, the hit's normal and
// how many frames the sample has lived, the reservoir's unbiased contribution weight W and how many samples M it stands
// for. Cells are normalised to a pdf of one, so a cell's light is light times W. target is the chosen sample's target
// at this probe, kept while the reservoir is built.
struct GI_Reservoir
{
    vec3 light;
    float W;
    vec3 direction;
    float distance;
    float M;
    vec3 hitNormal;
    float age;
    float target;
};

float GI_Target(vec3 light) { return Graphite_Luminance(light) + 1e-4; }

GI_Reservoir GI_EmptyReservoir()
{
    GI_Reservoir r;
    r.light = vec3(0.0);
    r.W = 0.0;
    r.direction = vec3(0.0, 1.0, 0.0);
    r.distance = GI_FAR;
    r.M = 0.0;
    r.hitNormal = vec3(0.0, -1.0, 0.0);
    r.age = 0.0;
    r.target = 0.0;
    return r;
}

GI_Reservoir GI_ReadReservoir(vec4 color, vec4 data, vec4 hit)
{
    GI_Reservoir r;
    r.light = color.rgb;
    r.W = color.a;
    r.direction = Graphite_OctDecode(data.xy);
    r.distance = data.z;
    r.M = data.w;
    r.hitNormal = Graphite_OctDecode(hit.xy);
    r.age = hit.z;
    r.target = GI_Target(r.light);
    return r;
}

vec4 GI_ReservoirColor(GI_Reservoir r) { return vec4(r.light, r.W); }

vec4 GI_ReservoirData(GI_Reservoir r) { return vec4(Graphite_OctEncode(r.direction), r.distance, r.M); }

vec4 GI_ReservoirHit(GI_Reservoir r) { return vec4(Graphite_OctEncode(r.hitNormal), r.age, 0.0); }

// Streams another reservoir, its sample moved to this probe with that Jacobian, into one being built, standing for a
// share of its samples, picking its sample by weight
// Returns whether the other reservoir's sample was chosen
bool GI_Merge(inout GI_Reservoir r, inout float weights, GI_Reservoir other, float share, float jacobian, float u)
{
    float target = GI_Target(other.light) * jacobian;
    float w = target * other.W * other.M * share;
    if (w <= 0.0)
        return false;
    weights += w;
    r.M += other.M * share;
    if (u * weights < w)
    {
        r.light = other.light;
        r.direction = other.direction;
        r.distance = other.distance;
        r.hitNormal = other.hitNormal;
        r.age = other.age;
        r.target = target;
        return true;
    }
    return false;
}

void GI_Finish(inout GI_Reservoir r, float weights) { r.W = r.M > 0.0 && r.target > 0.0 ? weights / (r.M * r.target) : 0.0; }

// As GI_Finish, over only the Z samples whose probes could have taken the chosen sample, so samples another probe
// could never reach do not dim it
void GI_FinishOver(inout GI_Reservoir r, float weights, float Z) { r.W = Z > 0.0 && r.target > 0.0 ? weights / (Z * r.target) : 0.0; }

// Holds a reservoir to standing for at most M samples, keeping its W
void GI_Cap(inout GI_Reservoir r, inout float weights, float M)
{
    if (r.M <= M)
        return;
    weights *= M / r.M;
    r.M = M;
}

#define GI_MAX_JACOBIAN 2.5         // how far moving a sample to another probe may grow or shrink its weight before it is too
#define GI_MIN_JACOBIAN (1.0 / GI_MAX_JACOBIAN) // unlike the samples there to reuse, the same both ways so a sample moved
                                    // here can always be moved back to the probe it came from

// Whether a sample, seen from a camera relative point, lands in a cell of a probe at another point facing a normal
bool GI_Reaches(GI_Reservoir r, vec3 from, vec3 to, vec3 normal, int cell);

// Moves a sample's hit, seen from one camera relative point, to another: its direction and distance from there. Returns
// the Jacobian of the change in solid angle, 0 where the hit faces away from either or it falls outside GI_MIN_JACOBIAN
// to GI_MAX_JACOBIAN. The sky stays where it is.
float GI_Shift(vec3 from, vec3 to, inout vec3 direction, inout float distance, vec3 hitNormal)
{
    if (distance >= GI_FAR)
        return 1.0;
    vec3 offset = from + direction * distance - to;
    float squared = dot(offset, offset);
    vec3 moved = offset * inversesqrt(max(squared, 1e-8));
    float before = -dot(direction, hitNormal), after = -dot(moved, hitNormal);
    if (before <= 0.0 || after <= 0.0)
        return 0.0;
    float jacobian = distance * distance * after / (squared * before);
    direction = moved;
    distance = sqrt(squared);
    return jacobian < GI_MIN_JACOBIAN || jacobian > GI_MAX_JACOBIAN ? 0.0 : jacobian;
}

bool GI_Reaches(GI_Reservoir r, vec3 from, vec3 to, vec3 normal, int cell)
{
    return GI_Shift(from, to, r.direction, r.distance, r.hitNormal) > 0.0 && GI_CellOf(r.direction, normal) == cell;
}

#if defined(GI_TRACE) || defined(GI_REFERENCE)
#include "Graphite/Lights.glsl"
#include "Graphite/VoxelCache.glsl"

#define GI_SKY_HORIZON 0.05         // how far above the horizon a ray past the box must point to take the sky
#define GI_SKY_SAMPLES 4            // points along a ray past the box its sun shadow is averaged over
#define GI_VOXEL_LIFT 1.0           // voxels a voxel ray starts off its surface
#define GI_VOXEL_RESUME 2.0         // voxels before where the screen lost a ray its voxel ray picks up

// The sky a ray past the box finds nothing on screen against: only above the horizon, as far as the sun's shadow
// along it up to where the screen lost it says it reaches, which stands in for what roofs the screen cannot see
vec3 GI_SkyPastTheBox(vec3 start, vec3 end, vec3 direction, out float reach)
{
    reach = 0.0;
    if (direction.y <= GI_SKY_HORIZON)
        return vec3(0.0);
    float shadow = 0.0;
    for (int i = 0; i < GI_SKY_SAMPLES; i++)
        shadow += Graphite_MainDirectionalMapShadow(mix(start, end, (float(i) + 0.5) / float(GI_SKY_SAMPLES)), graphite_VoxelLight.sunDirection.xyz);
    reach = Graphite_VoxelSkyReach(shadow / float(GI_SKY_SAMPLES));
    return Graphite_VoxelSkyRadiance(direction) * reach;
}
#endif

#if defined(GI_TRACE)
uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
uniform GRAPHITE_SCREEN_TEXTURE _Normals;
uniform GRAPHITE_SCREEN_TEXTURE _HiZ;

#include "Graphite/HiZ.glsl"

#define GI_HIZ_STEPS 64
#define GI_SCREEN_REACH 500.0       // metres a ray marches the screen, far enough to cross it
#define GI_SCREEN_THICKNESS 0.05    // how far behind a pixel past the box a hit may lie, of its distance

// Marches the depth pyramid from a camera relative start along a direction to the screen's edge, climbing to coarser
// cells while the ray stays nearer than everything in them and down to pixels where it may pass behind something. The
// first pixel it passes behind is the hit if the ray lies within a thickness behind it, growing with distance, on a
// surface facing the ray. Else stop is where the screen lost the ray. jitter, 0 to 1, moves the start along the first pixel.
bool GI_ScreenTrace(Graphite_ViewData view, vec3 start, vec3 direction, float jitter, out vec3 surface, out vec3 hitNormal,
    out float travelled, out vec3 stop)
{
    surface = vec3(0.0);
    hitNormal = -direction;
    travelled = GI_FAR;
    stop = start;
    float distance = GI_SCREEN_REACH;
    vec4 c0 = view.viewProjection * vec4(start, 1.0);
    vec4 c1 = view.viewProjection * vec4(start + direction * distance, 1.0);
    if (c0.w <= 0.0)
        return false;
    if (c1.w < 0.1 * c0.w)
    {
        distance *= (0.9 * c0.w) / (c0.w - c1.w);
        c1 = view.viewProjection * vec4(start + direction * distance, 1.0);
    }
    vec3 n0 = c0.xyz / c0.w, n1 = c1.xyz / c1.w;
    vec2 p0 = vec2(n0.x * 0.5 + 0.5, 0.5 - n0.y * 0.5) * _ScreenSize;
    vec2 p1 = vec2(n1.x * 0.5 + 0.5, 0.5 - n1.y * 0.5) * _ScreenSize;
    if (length(p1 - p0) < 1.0)
        return false;
    float t = jitter / length(p1 - p0);
    ivec2 cell;
    float nearest;
    if (Graphite_HiZMarch(_CameraDepthTexture, _HiZ, _ScreenSize, p0, p1, n0.z, n1.z, GI_HIZ_STEPS, t, cell, nearest))
    {
        // At a pixel the ray reaches or passes behind, its distance along the ray found perspective correct
        vec3 at = Graphite_RelativeFromDepth(view, (vec2(cell) + 0.5) / _ScreenSize, nearest);
        float u = clamp(t, 0.0, 1.0);
        float along = (u / c1.w) / ((1.0 - u) / c0.w + u / c1.w) * distance;
        vec3 q = start + direction * along;
        vec3 facing = Graphite_OctDecode(Graphite_Fetch(_Normals, cell, 0).rg);
        float thickness = GI_InBox(view, at) ? 0.05 + 0.02 * length(at) : GI_SCREEN_THICKNESS * length(at);
        stop = q;
        if (nearest == 0.0 || length(q - at) > thickness || dot(facing, direction) >= 0.0)
            return false;
        surface = at;
        hitNormal = facing;
        travelled = along;
        return true;
    }
    float u = clamp(t, 0.0, 1.0);
    stop = start + direction * ((u / c1.w) / ((1.0 - u) / c0.w + u / c1.w) * distance);
    return false;
}
#endif

#if defined(GI_PROBES)
layout(location = 1) out vec4 outPosition;

uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
uniform GRAPHITE_SCREEN_TEXTURE _Normals;

void main()
{
    ivec2 pixel = GI_ProbePixel(ivec2(gl_FragCoord.xy));
    float depth = Graphite_Fetch(_CameraDepthTexture, pixel, 0).r;
    vec3 relative = depth > 0.0 ? Graphite_RelativeFromDepth(Graphite_PostView(), GI_PixelUV(pixel), depth) : vec3(0.0);
    outColor = vec4(Graphite_Fetch(_Normals, pixel, 0).rg, depth, depth > 0.0 ? length(relative) : 0.0);
    outPosition = vec4(relative, 0.0);
}
#elif defined(GI_TRACE)
layout(location = 1) out vec4 outHit;

uniform GRAPHITE_SCREEN_TEXTURE _Probes;
uniform GRAPHITE_SCREEN_TEXTURE _PreviousColor;
uniform float _HasPreviousColor;

#define GI_RAY_LIFT 0.02            // metres a ray starts off its surface, growing by GI_RAY_LIFT_FAR a metre away
#define GI_RAY_LIFT_FAR 0.005

void main()
{
    ivec2 texel = ivec2(gl_FragCoord.xy);
    ivec2 probe = texel / GI_CELLS;
    ivec2 c = texel % GI_CELLS;
    vec4 p = Graphite_Fetch(_Probes, probe, 0);
    outHit = vec4(0.0);
    if (p.w == 0.0)
    {
        outColor = vec4(0.0);
        return;
    }
    Graphite_ViewData view = Graphite_PostView();
    vec3 relative = GI_ProbePosition(probe);
    vec3 normal = Graphite_OctDecode(p.xy);
    vec3 direction = GI_RayDirection(probe, c.x + c.y * GI_CELLS, normal);

    // The screen first, then the voxels for what it misses, and past the box the sky for what neither finds
    vec3 light;
    float travelled;
    vec3 surface;
    vec3 hitNormal = -direction;
    bool inBox = GI_InBox(view, relative);
    float jitter = float(Graphite_Hash(uint(texel.x) * 9781u ^ uint(texel.y) * 6271u ^ uint(_Frame) * 26699u) & 0xFFFFu) / 65536.0;
    vec3 start = relative + normal * (GI_RAY_LIFT + GI_RAY_LIFT_FAR * length(relative));
    vec3 stop = start;
    if (_HasPreviousColor != 0.0 && GI_ScreenTrace(view, start, direction, jitter, surface, hitNormal, travelled, stop))
    {
        ivec2 previous = clamp(ivec2(Graphite_PostPreviousUV(view, surface) * _ScreenSize), ivec2(0), ivec2(_ScreenSize) - 1);
        light = Graphite_Fetch(_PreviousColor, previous, 0).rgb * Graphite_PostExposureRatio(view);
    }
    else
    {
        // Rays from past the box still find what it holds where they pass through it, picking up a little before where
        // the screen lost them since everything the ray passed in front of is clear
        vec3 origin = GI_VoxelPosition(view, relative) + normal * GI_VOXEL_LIFT;
        float skip = max(length(stop - start) / _BoxMin.w - GI_VOXEL_RESUME, 0.0);
        vec3 resume = origin + direction * skip;
        Graphite_VoxelHit hit = Graphite_TraceVoxels(resume, direction, 1e6, 256u);
        float reach = 0.0;
        if (hit.hit || hit.lost)
        {
            light = hit.hit ? Graphite_VoxelHitLight(hit, resume, direction, true) : vec3(0.0);
            travelled = (hit.t + skip + GI_VOXEL_LIFT) * _BoxMin.w;
            hitNormal = hit.hit && hit.normal != vec3(0.0) ? hit.normal : hitNormal;
        }
        else if (inBox)
        {
            light = Graphite_VoxelSkyLeaving(origin, direction, reach);
            travelled = (Graphite_VoxelBoxExit(origin, direction) + GI_VOXEL_LIFT) * _BoxMin.w;
        }
        else
        {
            light = GI_SkyPastTheBox(start, stop, direction, reach);
            travelled = length(stop - start);
        }
        travelled = reach > 0.5 ? GI_FAR : travelled;
        light = Graphite_VoxelToView(light, view.exposure.x);
    }

    // No ray brings back more than GI_MAX_LIGHT at the view's exposure, so the few that find a small bright light do
    // not leave blotches the history cannot smooth
    light = any(isnan(light)) || any(isinf(light)) ? vec3(0.0) : max(light, vec3(0.0));
    light *= min(1.0, GI_MAX_LIGHT / max(max(light.r, light.g), max(light.b, 1e-6)));
    outColor = vec4(light, travelled);
    outHit = vec4(Graphite_OctEncode(hitNormal), 0.0, 0.0);
}
#elif defined(GI_PROBE_AO)
uniform GRAPHITE_SCREEN_TEXTURE _Probes;
uniform GRAPHITE_SCREEN_TEXTURE _Atlas;
uniform GRAPHITE_SCREEN_TEXTURE _Occlusion;
uniform float _HasOcclusion;

#define GI_AO_RANGE 1.0

void main()
{
    ivec2 probe = ivec2(gl_FragCoord.xy);
    if (Graphite_Fetch(_Probes, probe, 0).w == 0.0)
    {
        outColor = vec4(1.0);
        return;
    }
    float open = 0.0, sky = 0.0, weights = 0.0;
    for (int cell = 0; cell < GI_CELLS * GI_CELLS; cell++)
    {
        float cosine = (float(cell % GI_CELLS) + 0.5) / float(GI_CELLS);
        float distance = Graphite_Fetch(_Atlas, GI_CellTexel(probe, cell), 0).a;
        open += min(distance / GI_AO_RANGE, 1.0) * cosine;
        sky += distance >= GI_FAR ? cosine : 0.0;
        weights += cosine;
    }
    float visibility = 1.0;
    if (_HasOcclusion != 0.0)
    {
        ivec2 size = Graphite_Size(_Occlusion, 0);
        visibility = Graphite_Fetch(_Occlusion, min(ivec2((vec2(GI_ProbePixel(probe)) + 0.5) / _ScreenSize * vec2(size)), size - 1), 0).z;
    }
    outColor = vec4(open / weights, sky / weights, visibility, 0.0);
}
#elif defined(GI_RESTIR_TEMPORAL)
layout(location = 1) out vec4 outData;
layout(location = 2) out vec4 outHit;

uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
uniform GRAPHITE_SCREEN_TEXTURE _Motion;
uniform GRAPHITE_SCREEN_TEXTURE _Probes;
uniform GRAPHITE_SCREEN_TEXTURE _Atlas;
uniform GRAPHITE_SCREEN_TEXTURE _AtlasHit;
uniform GRAPHITE_SCREEN_TEXTURE _PreviousProbes;
uniform GRAPHITE_SCREEN_TEXTURE _PreviousProbePositions;
uniform GRAPHITE_SCREEN_TEXTURE _PreviousReservoir;
uniform GRAPHITE_SCREEN_TEXTURE _PreviousReservoirData;
uniform GRAPHITE_SCREEN_TEXTURE _PreviousReservoirHit;
uniform vec2 _PreviousProbeJitter;
uniform float _ProbeHistoryValid;

#define GI_MAX_AGE 12.0             // frames a sample lives, so a change in the light shows within them
#define GI_MOVING_DISTANCE 0.5      // the distance tolerance where an object moved, its distance then unknown

// Where a point lay in last frame's probe grid, in probes, from its uv there
vec2 GI_GridAt(vec2 uv) { return (uv * _ScreenSize - floor(_PreviousProbeJitter * float(GI_PROBE)) - 0.5) / float(GI_PROBE); }

// Where a probe's point was on screen last frame, moved by the motion of the nearest pixel around it so a moving
// object's edge carries its own. Returns whether that motion is the object's own rather than the camera's.
bool GI_ProbeMoved(Graphite_ViewData view, ivec2 probe, vec3 relative, out vec2 previous)
{
    previous = Graphite_PostPreviousUV(view, relative);
    ivec2 pixel = GI_ProbePixel(probe);
    float nearest = -1.0;
    vec2 motion = vec2(0.0);
    for (int i = 0; i < 9; i++)
    {
        ivec2 around = clamp(pixel + ivec2(i % 3 - 1, i / 3 - 1), ivec2(0), ivec2(_ScreenSize) - 1);
        float depth = Graphite_Fetch(_CameraDepthTexture, around, 0).r;
        if (depth > nearest)
        {
            nearest = depth;
            motion = Graphite_Fetch(_Motion, around, 0).rg;
        }
    }
    if (motion == vec2(0.0))
        return false;
    vec2 moved = GI_PixelUV(pixel) - motion;
    bool own = length((moved - previous) * _ScreenSize) > 1.0;
    previous = moved;
    return own;
}

// An earlier reservoir of a cell, its light at this frame's exposure and age counted on, its sample moved here from
// where its probe lay with the Jacobian that brings. False where it is empty, lived out GI_MAX_AGE or its sample leaves
// the cell.
bool GI_TakeReservoir(vec4 color, vec4 data, vec4 hit, float ratio, float frames, bool shift, vec3 from, vec3 relative, vec3 normal, int cell,
    out GI_Reservoir h, out float jacobian)
{
    h = GI_ReadReservoir(color, data, hit);
    h.age += frames;
    jacobian = 0.0;
    if (h.M <= 0.0 || h.age > GI_MAX_AGE)
        return false;
    h.light = min(h.light * ratio, vec3(GI_MAX_LIGHT));
    h.M = min(h.M, GI_MAX_M);
    jacobian = shift ? GI_Shift(from, relative, h.direction, h.distance, h.hitNormal) : 1.0;
    return jacobian > 0.0 && GI_CellOf(h.direction, normal) == cell;
}

// The reservoir of the same cell of last frame's probe among the 2 x 2 around where this one was that lay nearest it on
// its plane, within a probe square as the jitter moves probes. Where the object moved on its own its probes then lay
// elsewhere, so they are matched by camera distance and the sample kept as it was.
bool GI_LastReservoir(Graphite_ViewData view, ivec2 probe, vec3 relative, vec3 normal, int cell, out GI_Reservoir h, out float jacobian,
    out vec3 from, out vec3 fromNormal)
{
    jacobian = 0.0;
    from = relative;
    fromNormal = normal;
    vec2 previous;
    bool moving = GI_ProbeMoved(view, probe, relative, previous);
    float plane = GI_SAME_DISTANCE * length(relative);
    if (!GI_OnScreen(previous))
        return false;
    ivec2 base = ivec2(floor(GI_GridAt(previous)));
    ivec2 last = Graphite_Size(_PreviousProbes, 0) - 1;
    vec3 cameraMoved = Graphite_PostCameraMoved(view);
    float best = moving ? GI_MOVING_DISTANCE * length(relative) : 1.5 * float(GI_PROBE) / Graphite_PostPixelsPerMetre(view, _ScreenSize.y, relative);
    ivec2 chosen = ivec2(-1);
    for (int i = 0; i < 4; i++)
    {
        ivec2 other = base + ivec2(i & 1, i >> 1);
        if (any(lessThan(other, ivec2(0))) || any(greaterThan(other, last)))
            continue;
        vec4 q = Graphite_Fetch(_PreviousProbes, other, 0);
        if (q.w == 0.0 || dot(Graphite_OctDecode(q.xy), normal) < GI_SAME_NORMAL)
            continue;
        vec3 position = Graphite_Fetch(_PreviousProbePositions, other, 0).xyz - cameraMoved;
        if (!moving && abs(dot(position - relative, normal)) > plane)
            continue;
        float away = moving ? abs(q.w - length(relative + cameraMoved)) : length(position - relative);
        if (away < best)
        {
            best = away;
            chosen = other;
            from = position;
            fromNormal = Graphite_OctDecode(q.xy);
        }
    }
    if (chosen.x < 0)
        return false;
    ivec2 texel = GI_CellTexel(chosen, cell);
    return GI_TakeReservoir(Graphite_Fetch(_PreviousReservoir, texel, 0), Graphite_Fetch(_PreviousReservoirData, texel, 0), Graphite_Fetch(_PreviousReservoirHit, texel, 0),
        Graphite_PostExposureRatio(view), 1.0, !moving, from, relative, normal, cell, h, jacobian);
}

void main()
{
    ivec2 texel = ivec2(gl_FragCoord.xy);
    ivec2 probe = texel / GI_CELLS;
    ivec2 c = texel % GI_CELLS;
    int cell = c.x + c.y * GI_CELLS;
    vec4 p = Graphite_Fetch(_Probes, probe, 0);
    GI_Reservoir r = GI_EmptyReservoir();
    outColor = GI_ReservoirColor(r);
    outData = GI_ReservoirData(r);
    outHit = GI_ReservoirHit(r);
    if (p.w == 0.0)
        return;

    Graphite_ViewData view = Graphite_PostView();
    vec3 normal = Graphite_OctDecode(p.xy);
    vec4 traced = Graphite_Fetch(_Atlas, texel, 0);
    r.light = traced.rgb;
    r.W = 1.0;
    vec4 tracedHit = Graphite_Fetch(_AtlasHit, texel, 0);
    r.direction = GI_RayDirection(probe, cell, normal);
    r.distance = traced.a;
    r.M = 1.0;
    r.hitNormal = Graphite_OctDecode(tracedHit.xy);
    r.target = GI_Target(r.light);
    float weights = r.target;

    GI_Reservoir h;
    float jacobian;
    vec3 relative = GI_ProbePosition(probe), from, fromNormal;
    float Z = 1.0;
    if (_ProbeHistoryValid != 0.0 && GI_LastReservoir(view, probe, relative, normal, cell, h, jacobian, from, fromNormal))
    {
        bool taken = GI_Merge(r, weights, h, 1.0, jacobian, GI_Random(uint(texel.x) * 2u + 1u, uint(texel.y), uint(_Frame)));
        Z += taken || GI_Reaches(r, relative, from, fromNormal, cell) ? h.M : 0.0;
    }
    GI_FinishOver(r, weights, Z);
    r.M = min(r.M, GI_MAX_M);
    outColor = GI_ReservoirColor(r);
    outData = GI_ReservoirData(r);
    outHit = GI_ReservoirHit(r);
}
#elif defined(GI_RESTIR_SPATIAL)
layout(location = 1) out vec4 outData;
layout(location = 2) out vec4 outHit;

uniform GRAPHITE_SCREEN_TEXTURE _Probes;
uniform GRAPHITE_SCREEN_TEXTURE _Reservoir;
uniform GRAPHITE_SCREEN_TEXTURE _ReservoirData;
uniform GRAPHITE_SCREEN_TEXTURE _ReservoirHit;
uniform GRAPHITE_SCREEN_TEXTURE _ProbeAO;
uniform int _Pass;
uniform int _Passes;

#define GI_SPATIAL_RANGE 1.0
#define GI_SPATIAL_MAX 8.0
#define GI_SPATIAL_MAX_M 8192.0     // the most samples a reservoir here stands for, which only weigh merges

// The disk's radius in probes: GI_SPATIAL_RANGE metres where the probe is open, down to a fifth of that where its rays
// stop close, seen at its distance, growing over the passes to the whole of it in the last
float GI_SpatialRadius(Graphite_ViewData view, vec3 relative, float open)
{
    float metres = GI_SPATIAL_RANGE * mix(0.2, 1.0, open);
    float radius = clamp(metres * Graphite_PostPixelsPerMetre(view, _ScreenSize.y, relative) / float(GI_PROBE), 1.0, GI_SPATIAL_MAX);
    return max(1.0, radius * float(_Pass + 1) / float(max(_Passes, 1)));
}

GI_Reservoir GI_ReservoirAt(ivec2 texel)
{
    return GI_ReadReservoir(Graphite_Fetch(_Reservoir, texel, 0), Graphite_Fetch(_ReservoirData, texel, 0), Graphite_Fetch(_ReservoirHit, texel, 0));
}

void main()
{
    ivec2 texel = ivec2(gl_FragCoord.xy);
    ivec2 probe = texel / GI_CELLS;
    ivec2 c = texel % GI_CELLS;
    int cell = c.x + c.y * GI_CELLS;
    vec4 p = Graphite_Fetch(_Probes, probe, 0);
    GI_Reservoir r = GI_ReservoirAt(texel);
    outColor = GI_ReservoirColor(r);
    outData = GI_ReservoirData(r);
    outHit = GI_ReservoirHit(r);
    if (p.w == 0.0 || r.M <= 0.0)
        return;

    Graphite_ViewData view = Graphite_PostView();
    vec3 normal = Graphite_OctDecode(p.xy);
    vec3 relative = GI_ProbePosition(probe);
    float weights = r.target * r.W * r.M;
    ivec2 last = Graphite_Size(_Probes, 0) - 1;

    float diskRadius = GI_SpatialRadius(view, relative, Graphite_Fetch(_ProbeAO, probe, 0).r);

    // Every cell of a probe takes the same neighbours, so reuse is always between the same cell of different probes,
    // each sample moved here and kept only where it still lands in this cell
    // Neighbours merged are kept as their probe and share of samples, their place found again for Z
    vec3 t, b;
    GI_CellFrame(normal, t, b);
    ivec2 others[8];
    float counts[8];
    int source = -1;
    float Z = r.M;
    // The neighbours lie on a Vogel disk turned and stretched at random per probe, and each merge takes the next number
    // of a per texel sequence
    uint key = Graphite_Hash(uint(probe.x) * 7919u + uint(probe.y) * 104729u ^ Graphite_Hash(uint(_Frame) * 8u + uint(_Pass)));
    float turn = GRAPHITE_TWO_PI * float(key & 0xFFFFu) / 65536.0, offset = float(key >> 16u) / 65536.0;
    vec2 spoke = vec2(cos(turn), sin(turn));
    const vec2 golden = vec2(-0.7373688, 0.6754903);
    uint sequence = Graphite_Hash(uint(texel.x) * 73856093u ^ uint(texel.y) * 19349663u ^ key);
    for (int i = 0; i < 8; i++)
    {
        counts[i] = 0.0;
        others[i] = probe;
        float radius = diskRadius * sqrt((float(i) + offset) / 8.0);
        ivec2 other = probe + ivec2(round(spoke * radius));
        spoke = vec2(spoke.x * golden.x - spoke.y * golden.y, spoke.x * golden.y + spoke.y * golden.x);
        sequence = sequence * 747796405u + 2891336453u;
        if (other == probe || any(lessThan(other, ivec2(0))) || any(greaterThan(other, last)))
            continue;
        vec4 q = Graphite_Fetch(_Probes, other, 0);
        if (q.w == 0.0)
            continue;
        vec3 position = GI_ProbePosition(other);
        float agreement = GI_ProbeAgreement(normal, relative, Graphite_OctDecode(q.xy), position);
        if (agreement < 1e-3)
            continue;
        GI_Reservoir n = GI_ReservoirAt(GI_CellTexel(other, cell));
        if (n.M <= 0.0)
            continue;
        float jacobian = GI_Shift(position, relative, n.direction, n.distance, n.hitNormal);
        if (jacobian <= 0.0 || GI_CellIn(n.direction, normal, t, b) != cell)
            continue;
        if (GI_Merge(r, weights, n, agreement, jacobian, float(Graphite_Hash(sequence) >> 8u) / 16777216.0))
            source = i;
        others[i] = other;
        counts[i] = n.M * agreement;
    }

    for (int i = 0; i < 8; i++)
    {
        if (counts[i] <= 0.0)
            continue;
        if (i == source)
        {
            Z += counts[i];
            continue;
        }
        vec4 q = Graphite_Fetch(_Probes, others[i], 0);
        Z += GI_Reaches(r, relative, GI_ProbePosition(others[i]), Graphite_OctDecode(q.xy), cell) ? counts[i] : 0.0;
    }
    GI_FinishOver(r, weights, Z);
    r.M = min(r.M, GI_SPATIAL_MAX_M);
    outColor = GI_ReservoirColor(r);
    outData = GI_ReservoirData(r);
    outHit = GI_ReservoirHit(r);
}
#elif defined(GI_SH)
#define GI_CELL_SPIKE 4.0

uniform GRAPHITE_SCREEN_TEXTURE _Probes;
uniform GRAPHITE_SCREEN_TEXTURE _Reservoir;
uniform GRAPHITE_SCREEN_TEXTURE _ReservoirData;

// Each texel one coefficient of its probe's light, convolved with the cosine lobe and over pi so evaluating it along a
// normal gives irradiance over pi
void main()
{
    ivec2 texel = ivec2(gl_FragCoord.xy);
    ivec2 probe = texel / 3;
    int k = (texel.x % 3) + (texel.y % 3) * 3;
    if (Graphite_Fetch(_Probes, probe, 0).w == 0.0)
    {
        outColor = vec4(0.0);
        return;
    }
    // No cell brings more than GI_CELL_SPIKE times the probe's mean, nor more than GI_MAX_LIGHT, so a lone sample with a
    // large weight cannot flash
    vec3 light[GI_CELLS * GI_CELLS], directions[GI_CELLS * GI_CELLS];
    float mean = 0.0;
    for (int cell = 0; cell < GI_CELLS * GI_CELLS; cell++)
    {
        ivec2 at = GI_CellTexel(probe, cell);
        GI_Reservoir r = GI_ReadReservoir(Graphite_Fetch(_Reservoir, at, 0), Graphite_Fetch(_ReservoirData, at, 0), vec4(0.0));
        light[cell] = r.light * r.W;
        directions[cell] = r.direction;
        mean += Graphite_Luminance(light[cell]) / float(GI_CELLS * GI_CELLS);
    }
    vec3 sum = vec3(0.0);
    for (int cell = 0; cell < GI_CELLS * GI_CELLS; cell++)
    {
        float luminance = Graphite_Luminance(light[cell]);
        float keep = luminance > 0.0 ? min(1.0, min(GI_CELL_SPIKE * mean, GI_MAX_LIGHT) / luminance) : 1.0;
        sum += light[cell] * keep * GI_SHBasis(k, directions[cell]);
    }
    float band = k == 0 ? 1.0 : k < 4 ? 2.0 / 3.0 : 0.25;
    outColor = vec4(sum * (GRAPHITE_TWO_PI / float(GI_CELLS * GI_CELLS)) * band, 1.0);
}
#elif defined(GI_OCCLUSION)
uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
uniform GRAPHITE_SCREEN_TEXTURE _Normals;
uniform float _OcclusionRadius;     // metres the horizon search reaches
uniform float _OcclusionThickness;  // metres a surface is taken to reach behind what the depth shows

#define GI_OCCLUSION_SLICES 2
#define GI_OCCLUSION_STEPS 8
#define GI_OCCLUSION_SECTORS 32
#define GI_OCCLUSION_REACH 128.0    // the most pixels the search reaches

// The integral of a slice's cosine weighted visibility from the view direction to angle g, signed so the arc from a
// to b is the difference of their two
float GI_SliceCumulative(float g, float n) { return g >= 0.0 ? Graphite_SliceArc(g, n) : -Graphite_SliceArc(g, n); }

// atan(y, x) by a polynomial within about 1e-5 radians, far inside a sector
float GI_FastAtan(float y, float x)
{
    float ax = abs(x), ay = abs(y);
    float a = min(ax, ay) / max(max(ax, ay), 1e-20), s = a * a;
    float r = ((-0.0464964749 * s + 0.15931422) * s - 0.327622764) * s * a + a;
    r = ay > ax ? GRAPHITE_HALF_PI - r : r;
    r = x < 0.0 ? GRAPHITE_PI - r : r;
    return y < 0.0 ? -r : r;
}

// Each slice through the view direction is split into sectors over the normal's half disc, a bit each, set where a
// surface the depth shows covers it from its front to a thickness behind. What is left open, cosine weighted, is the
// visibility, and its mean direction the bent normal.
void main()
{
    ivec2 pixel = min(ivec2(vUV * _ScreenSize), ivec2(_ScreenSize) - 1);
    float depth = Graphite_Fetch(_CameraDepthTexture, pixel, 0).r;
    vec3 normal = Graphite_OctDecode(Graphite_Fetch(_Normals, pixel, 0).rg);
    outColor = vec4(Graphite_OctEncode(normal), 1.0, 0.0);
    if (depth == 0.0)
        return;
    Graphite_ViewData view = Graphite_PostView();
    vec2 uv = GI_PixelUV(pixel);
    vec3 relative = Graphite_RelativeFromDepth(view, uv, depth);
    vec3 toward = Graphite_PostTowardCamera(view, uv, relative);
    float reach = min(_OcclusionRadius * Graphite_PostPixelsPerMetre(view, _ScreenSize.y, relative), GI_OCCLUSION_REACH);
    if (reach < 2.0)
        return;
    float bias = 0.01 + 0.002 * length(relative);
    float sector = GRAPHITE_PI / float(GI_OCCLUSION_SECTORS);

    float noise = GI_Random(uint(pixel.x), uint(pixel.y), uint(_Frame) * 3u + 7u);
    float stepNoise = GI_Random(uint(pixel.y), uint(pixel.x), uint(_Frame) * 3u + 8u);
    float open = 0.0, whole = 0.0;
    vec3 bent = vec3(0.0);
    for (int slice = 0; slice < GI_OCCLUSION_SLICES; slice++)
    {
        float phi = (float(slice) + noise) / float(GI_OCCLUSION_SLICES) * GRAPHITE_PI;
        vec2 screen = vec2(cos(phi), sin(phi));
        vec3 along = Graphite_RelativeFromDepth(view, uv + screen / _ScreenSize, depth) - relative;
        vec3 side = normalize(along - toward * dot(along, toward));
        vec3 axis = cross(toward, side);
        vec3 projected = normal - axis * dot(normal, axis);
        float weight = length(projected);
        if (weight < 1e-4)
            continue;
        float n = atan(dot(projected, side), dot(projected, toward));
        float low = n - GRAPHITE_HALF_PI;

        uint mask = 0u;
        for (int s = 0; s < 2; s++)
        {
            float way = s == 0 ? 1.0 : -1.0;
            for (int i = 0; i < GI_OCCLUSION_STEPS; i++)
            {
                float t = (float(i) + stepNoise) / float(GI_OCCLUSION_STEPS);
                vec2 at = vec2(pixel) + 0.5 + screen * way * max(t * t * reach, 1.0 + float(i));
                if (!GI_OnScreen(at / _ScreenSize))
                    break;
                float d = Graphite_Fetch(_CameraDepthTexture, ivec2(at), 0).r;
                if (d == 0.0)
                    continue;
                vec3 front = Graphite_RelativeFromDepth(view, at / _ScreenSize, d) - relative;
                if (dot(front, normal) < bias || dot(front, front) > _OcclusionRadius * _OcclusionRadius)
                    continue;
                vec3 back = front - toward * _OcclusionThickness;
                float a = (GI_FastAtan(dot(front, side), dot(front, toward)) - low) / GRAPHITE_PI;
                float b = (GI_FastAtan(dot(back, side), dot(back, toward)) - low) / GRAPHITE_PI;
                int first = int(clamp(min(a, b), 0.0, 1.0) * float(GI_OCCLUSION_SECTORS));
                int last = int(ceil(clamp(max(a, b), 0.0, 1.0) * float(GI_OCCLUSION_SECTORS)));
                if (last > first)
                    mask |= (last - first >= GI_OCCLUSION_SECTORS ? 0xFFFFFFFFu : ((1u << uint(last - first)) - 1u)) << uint(first);
            }
        }

        float start = GI_SliceCumulative(low, n);
        whole += (GI_SliceCumulative(low + GRAPHITE_PI, n) - start) * weight;
        if (mask == 0xFFFFFFFFu)
            continue;

        // The sectors' edges and middles stepped by rotation, as they are evenly spaced
        float cn = cos(n), sn = sin(n);
        vec2 edge = vec2(cos(2.0 * low - n), sin(2.0 * low - n)), middle = vec2(cos(low + 0.5 * sector), sin(low + 0.5 * sector));
        vec2 edgeStep = vec2(cos(2.0 * sector), sin(2.0 * sector)), middleStep = vec2(cos(sector), sin(sector));
        for (int k = 0; k < GI_OCCLUSION_SECTORS; k++)
        {
            float g = low + float(k + 1) * sector;
            edge = vec2(edge.x * edgeStep.x - edge.y * edgeStep.y, edge.x * edgeStep.y + edge.y * edgeStep.x);
            float arc = 0.25 * (cn + 2.0 * g * sn - edge.x);
            float end = g >= 0.0 ? arc : -arc;
            if ((mask & (1u << uint(k))) == 0u)
            {
                open += (end - start) * weight;
                bent += (toward * middle.x + side * middle.y) * ((end - start) * weight);
            }
            middle = vec2(middle.x * middleStep.x - middle.y * middleStep.y, middle.x * middleStep.y + middle.y * middleStep.x);
            start = end;
        }
    }
    if (whole <= 0.0)
        return;
    vec3 direction = dot(bent, bent) > 1e-8 ? normalize(bent) : normal;
    outColor = vec4(Graphite_OctEncode(direction), clamp(open / whole, 0.0, 1.0), 0.0);
}
#elif defined(GI_GATHER)
layout(location = 1) out vec4 outSurface;
layout(location = 2) out vec4 outNormal;

uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
uniform GRAPHITE_SCREEN_TEXTURE _Normals;
uniform GRAPHITE_SCREEN_TEXTURE _Motion;
uniform GRAPHITE_SCREEN_TEXTURE _History;
uniform float _HistoryValid;
uniform GRAPHITE_SCREEN_TEXTURE _Probes;
uniform GRAPHITE_SCREEN_TEXTURE _SH;
uniform GRAPHITE_SCREEN_TEXTURE _ProbeAO;

#define GI_GATHER_REACH 2
#define GI_FAILED 2.0               // taken off the sky's visibility in alpha where no probe suits the pixel

// Irradiance over pi along a direction from the light inside a cone around it, its cosine weighted share of the
// hemisphere the visibility. The harmonics hold the cosine lobe's bands, each scaled to the cone's.
vec3 GI_EvaluateSH(ivec2 probe, vec3 n, float visibility)
{
    float c2 = 1.0 - visibility, c = sqrt(c2);
    vec3 bands = vec3(visibility, 1.0 - c2 * c, 1.0 + 2.0 * c2 - 3.0 * c2 * c2);
    vec3 e = vec3(0.0);
    for (int k = 0; k < 9; k++)
        e += Graphite_Fetch(_SH, probe * 3 + ivec2(k % 3, k / 3), 0).rgb * (GI_SHBasis(k, n) * bands[k == 0 ? 0 : k < 4 ? 1 : 2]);
    return max(e, vec3(0.0));
}

#if defined(GI_GATHER_OCCLUSION)
uniform GRAPHITE_SCREEN_TEXTURE _Occlusion;

#define GI_OCCLUSION_MATCH 0.25     // a difference in visibility that halves a probe's weight

// The bent normal and visibility at a pixel of the view
vec4 GI_OcclusionAt(ivec2 pixel, vec3 normal)
{
    ivec2 size = Graphite_Size(_Occlusion, 0);
    vec4 o = Graphite_Fetch(_Occlusion, min(ivec2((vec2(pixel) + 0.5) / _ScreenSize * vec2(size)), size - 1), 0);
    return vec4(Graphite_OctDecode(o.xy), o.z);
}

// probeVisibility the occlusion's visibility at the probe's pixel, kept by the probe AO pass
float GI_OcclusionMatch(float probeVisibility, vec4 occlusion)
{
    float difference = (probeVisibility - occlusion.w) / GI_OCCLUSION_MATCH;
    return exp2(-difference * difference);
}
#else
vec4 GI_OcclusionAt(ivec2 pixel, vec3 normal) { return vec4(normal, 1.0); }

float GI_OcclusionMatch(float probeVisibility, vec4 occlusion) { return 1.0; }
#endif

// One probe of the 5 x 5 around rather than a blend, picked by plane, normal, distance and occlusion, so light never
// leaks from a probe that does not suit the pixel and the temporal denoiser smooths the choice. The search centres on a
// point jittered each frame by up to half a probe square, kept only on the pixel's plane, so over frames a thin surface
// reaches every probe that suits it. Where none does, the nearest stand in and alpha is marked by GI_FAILED.
void main()
{
    ivec2 pixel = min(ivec2(vUV * _ScreenSize), ivec2(_ScreenSize) - 1);
    float depth = Graphite_Fetch(_CameraDepthTexture, pixel, 0).r;
    vec2 encoded = Graphite_Fetch(_Normals, pixel, 0).rg;
    outColor = vec4(0.0, 0.0, 0.0, 1.0);
    outSurface = vec4(0.0, 0.0, 0.0, -1.0);
    outNormal = vec4(encoded, 0.0, 0.0);
    if (depth == 0.0)
        return;
    Graphite_ViewData view = Graphite_PostView();
    vec3 normal = Graphite_OctDecode(encoded);
    vec3 relative = Graphite_RelativeFromDepth(view, GI_PixelUV(pixel), depth);

    // The split blur's surface: where the pixel lies and the brightness its history had, which guides the blur
    vec2 previous = vUV - Graphite_Fetch(_Motion, pixel, 0).rg;
    float guide = _HistoryValid != 0.0 && GI_OnScreen(previous) ? Graphite_Luminance(Graphite_Sample(_History, previous).rgb) : 0.0;
    outSurface = vec4(relative, guide);
    ivec2 last = Graphite_Size(_Probes, 0) - 1;
    vec4 occlusion = GI_OcclusionAt(pixel, normal);

    vec2 jitter = vec2(GI_Random(uint(pixel.x), uint(pixel.y), uint(_Frame) * 2u + 1u), GI_Random(uint(pixel.y), uint(pixel.x), uint(_Frame) * 2u + 3u)) - 0.5;
    ivec2 look = clamp(pixel + ivec2(round(jitter * float(GI_PROBE))), ivec2(0), ivec2(_ScreenSize) - 1);
    float lookDepth = Graphite_Fetch(_CameraDepthTexture, look, 0).r;
    if (lookDepth == 0.0 || GI_ProbeAgreement(normal, relative, normal, Graphite_RelativeFromDepth(view, GI_PixelUV(look), lookDepth)) < 0.01)
        look = pixel;
    ivec2 centre = look / GI_PROBE;

    // One pass picks a probe by weight and another by nearness alone in case none suits, each streamed with a single
    // random number rescaled after every probe
    const int side = GI_GATHER_REACH * 2 + 1;
    float total = 0.0, nearTotal = 0.0, sky = 0.0, nearSky = 0.0;
    float u = GI_Random(uint(pixel.x), uint(pixel.y), uint(_Frame)), nearU = u;
    ivec2 chosen = ivec2(-1), nearChosen = ivec2(-1);
    for (int i = 0; i < side * side; i++)
    {
        ivec2 probe = centre + ivec2(i % side, i / side) - GI_GATHER_REACH;
        if (any(lessThan(probe, ivec2(0))) || any(greaterThan(probe, last)))
            continue;
        vec4 p = Graphite_Fetch(_Probes, probe, 0);
        if (p.w == 0.0)
            continue;
        vec2 away = vec2(GI_ProbePixel(probe) - pixel) / float(GI_PROBE);
        float near = exp2(-dot(away, away));
        vec3 ao = Graphite_Fetch(_ProbeAO, probe, 0).rgb;
        float open = ao.g;
        float weight = GI_ProbeAgreement(normal, relative, Graphite_OctDecode(p.xy), GI_ProbePosition(probe)) * near * GI_OcclusionMatch(ao.b, occlusion);
        nearTotal += near;
        nearSky += near * open;
        float share = near / nearTotal;
        nearChosen = nearU < share ? probe : nearChosen;
        nearU = nearU < share ? nearU / share : (nearU - share) / (1.0 - share);
        if (weight <= 0.0)
            continue;
        total += weight;
        sky += weight * open;
        share = weight / total;
        chosen = u < share ? probe : chosen;
        u = u < share ? u / share : (u - share) / (1.0 - share);
    }
    if (nearTotal < 1e-6)
        return;
    bool failed = total < 1e-6;
    if (failed)
    {
        chosen = nearChosen;
        total = nearTotal;
        sky = nearSky;
    }
    if (chosen.x < 0)
        return;
    sky = sky / total * occlusion.w;
    outColor = vec4(GI_EvaluateSH(chosen, occlusion.xyz, occlusion.w), failed ? sky - GI_FAILED : sky);
}
#elif defined(GI_SPLIT)
uniform GRAPHITE_SCREEN_TEXTURE _Current;
uniform GRAPHITE_SCREEN_TEXTURE _Gather;
uniform GRAPHITE_SCREEN_TEXTURE _Surface;
uniform GRAPHITE_SCREEN_TEXTURE _SurfaceNormals;

#define GI_SPLIT_TAPS 10            // taps each way, GI_SPLIT_STEP texels apart
#define GI_SPLIT_STEP 2
#define GI_SPLIT_EDGE 4.0           // how sharply a difference in the history's brightness, relative, stops the blur
#define GI_SPLIT_KEEP 0.08          // the share of the pixel's own light kept unblurred
#define GI_FAILED 2.0

// A texel's surface from the gather, its normal out
vec4 GI_SplitSurface(ivec2 texel, out vec3 normal)
{
    normal = Graphite_OctDecode(Graphite_Fetch(_SurfaceNormals, texel, 0).rg);
    return Graphite_Fetch(_Surface, texel, 0);
}

// The sky's visibility in a gather texel's alpha, without the mark of a pixel no probe suited
float GI_SplitSky(float alpha) { return alpha < -0.5 ? alpha + GI_FAILED : alpha; }

void main()
{
    ivec2 size = Graphite_Size(_Current, 0);
    ivec2 own = min(ivec2(vUV * vec2(size)), size - 1);
    vec4 centre = Graphite_Fetch(_Current, own, 0);
#if defined(GI_SPLIT_VERTICAL)
    vec4 original = Graphite_Fetch(_Gather, own, 0);
    ivec2 along = ivec2(0, 1), across = ivec2(1, 0);
#else
    vec4 original = centre;
    ivec2 along = ivec2(1, 0), across = ivec2(0, 1);
#endif
    vec3 normal;
    vec4 surface = GI_SplitSurface(own, normal);
    if (surface.w < 0.0)
    {
        outColor = vec4(centre.rgb, original.a);
        return;
    }
    float plane = GI_SAME_DISTANCE * length(surface.xyz);
    vec4 sum = vec4(0.0);
    float weights = 0.0;
    uint sides = Graphite_Hash(uint(own.x) * 73856093u ^ uint(own.y) * 19349663u ^ uint(_Frame) * 83492791u);
    for (int i = -GI_SPLIT_TAPS; i <= GI_SPLIT_TAPS; i++)
    {
        int side = i != 0 ? int((sides >> uint(i + GI_SPLIT_TAPS)) & 1u) : 0;
        ivec2 texel = clamp(own + along * (i * GI_SPLIT_STEP) + across * side, ivec2(0), size - 1);
        vec3 tapNormal;
        vec4 tapSurface = GI_SplitSurface(texel, tapNormal);
        if (tapSurface.w < 0.0 || abs(dot(tapSurface.xyz - surface.xyz, normal)) > plane)
            continue;
        vec4 tap = Graphite_Fetch(_Current, texel, 0);
        tap.a = GI_SplitSky(tap.a);
        float facing = max(dot(tapNormal, normal), 0.0);
        float w = exp(-float(i * i) / float(2 * GI_SPLIT_TAPS * GI_SPLIT_TAPS / 4)) * facing * facing * facing * facing
            / (1.0 + abs(tapSurface.w - surface.w) / max(surface.w, 1e-3) * GI_SPLIT_EDGE);
        sum += tap * w;
        weights += w;
    }
    vec4 blurred = weights > 0.0 ? sum / weights : vec4(centre.rgb, GI_SplitSky(centre.a));
#if defined(GI_SPLIT_VERTICAL)
    vec4 kept = mix(blurred, vec4(original.rgb, GI_SplitSky(original.a)), GI_SPLIT_KEEP);
    outColor = vec4(kept.rgb, original.a < -0.5 ? kept.a - GI_FAILED : kept.a);
#else
    outColor = blurred;
#endif
}
#elif defined(GI_TEMPORAL)
layout(location = 1) out vec4 outMeta;

uniform GRAPHITE_SCREEN_TEXTURE _Current;
uniform GRAPHITE_SCREEN_TEXTURE _History;
uniform GRAPHITE_SCREEN_TEXTURE _HistoryMeta;
uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
uniform GRAPHITE_SCREEN_TEXTURE _Normals;
uniform GRAPHITE_SCREEN_TEXTURE _Motion;
uniform float _HistoryValid;
uniform float _MaxHistory;

#define GI_FAILED 2.0
#define GI_CLIP_DEVIATIONS 4.0      // how far from the 7 x 7's mean, in its deviations, history is clipped to
#define GI_CLIP_FLOOR 0.25          // the least deviation, of the mean, as spatial reuse moves whole probes together
#define GI_CLIP_TINT 0.05           // the least deviation of the tint, of the mean brightness, so a stray colour fades fast
#define GI_FAILED_BLEND 0.05        // the share of the current light a pixel with no suiting probe takes each frame

// The light in rgb and the sky's visibility in alpha, the second target the pixel's camera distance, octahedral normal
// and how many frames its history holds, up to _MaxHistory
void main()
{
    ivec2 size = Graphite_Size(_Current, 0);
    ivec2 own = min(ivec2(vUV * vec2(size)), size - 1);
    vec4 current = Graphite_Fetch(_Current, own, 0);
    bool failed = current.a < -0.5;
    current.a += failed ? GI_FAILED : 0.0;
    ivec2 pixel = min(ivec2(vUV * _ScreenSize), ivec2(_ScreenSize) - 1);
    float depth = Graphite_Fetch(_CameraDepthTexture, pixel, 0).r;
    vec2 octahedral = Graphite_Fetch(_Normals, pixel, 0).rg;
    outColor = current;
    outMeta = vec4(0.0, octahedral, 1.0);
    if (depth == 0.0)
        return;
    Graphite_ViewData view = Graphite_PostView();
    vec3 relative = Graphite_RelativeFromDepth(view, GI_PixelUV(pixel), depth);
    outMeta.x = length(relative);
    if (_HistoryValid == 0.0)
        return;

    // Motion leaves out the camera's jitter, so a still pixel reads its history in place rather than at a new subpixel
    // offset each frame, which the sharp filter below would ring into lines
    vec2 previous = vUV - Graphite_Fetch(_Motion, pixel, 0).rg;
    if (!GI_OnScreen(previous))
        return;

    // History from the 4 x 4 texels around where the pixel was through a Catmull-Rom filter, which keeps its detail as
    // it is resampled frame after frame. Each texel counts only where it held this surface. Where any does not, the
    // kept ones of the inner 2 x 2 go bilinear, so history never bleeds across an edge.
    vec3 normal = Graphite_OctDecode(octahedral);
    float then = length(relative + Graphite_PostCameraMoved(view));
    vec2 at = previous * vec2(size) - 0.5;
    ivec2 base = ivec2(floor(at));
    vec2 f = at - vec2(base);
    vec4 cx = vec4(f.x * (-0.5 + f.x * (1.0 - 0.5 * f.x)), 1.0 + f.x * f.x * (-2.5 + 1.5 * f.x), f.x * (0.5 + f.x * (2.0 - 1.5 * f.x)), f.x * f.x * (-0.5 + 0.5 * f.x));
    vec4 cy = vec4(f.y * (-0.5 + f.y * (1.0 - 0.5 * f.y)), 1.0 + f.y * f.y * (-2.5 + 1.5 * f.y), f.y * (0.5 + f.y * (2.0 - 1.5 * f.y)), f.y * f.y * (-0.5 + 0.5 * f.y));
    vec4 sharp = vec4(0.0), soft = vec4(0.0);
    float kept = 0.0, held = 0.0;
    bool whole = true;
    for (int i = 0; i < 16; i++)
    {
        ivec2 o = ivec2(i & 3, i >> 2);
        ivec2 texel = clamp(base + o - 1, ivec2(0), size - 1);
        vec4 surface = Graphite_Fetch(_HistoryMeta, texel, 0);
        if (!GI_SameDistance(surface.r, then, GI_SAME_DISTANCE) || dot(Graphite_OctDecode(surface.gb), normal) < GI_SAME_NORMAL)
        {
            whole = false;
            continue;
        }
        vec4 h = Graphite_Fetch(_History, texel, 0);
        sharp += h * (cx[o.x] * cy[o.y]);
        if (o.x == 1 || o.x == 2)
        {
            if (o.y == 1 || o.y == 2)
            {
                float w = (o.x == 2 ? f.x : 1.0 - f.x) * (o.y == 2 ? f.y : 1.0 - f.y) + 1e-4;
                soft += h * w;
                held += surface.a * w;
                kept += w;
            }
        }
    }
    if (kept == 0.0)
        return;
    vec4 history = whole ? vec4(max(sharp.rgb, vec3(0.0)), clamp(sharp.a, 0.0, 1.0)) : soft / kept;
    if (any(isnan(history)) || any(isinf(history)))
        return;

    // The history clipped to the current sample and the deviations of a 7 x 7 gaussian around it
    vec3 mean = vec3(0.0), squares = vec3(0.0);
    float weights = 0.0;
    for (int y = -3; y <= 3; y++)
    {
        for (int x = -3; x <= 3; x++)
        {
            float w = exp(-float(x * x + y * y) / 8.0);
            vec3 c = Graphite_ToYCoCg(Graphite_Fetch(_Current, clamp(own + ivec2(x, y), ivec2(0), size - 1), 0).rgb);
            mean += c * w;
            squares += c * c * w;
            weights += w;
        }
    }
    mean /= weights;
    vec3 spread = sqrt(max(squares / weights - mean * mean, vec3(0.0)));
    vec3 deviation = GI_CLIP_DEVIATIONS * max(spread, mean.x * vec3(GI_CLIP_FLOOR, GI_CLIP_TINT, GI_CLIP_TINT));
    vec3 unclipped = history.rgb * Graphite_PostExposureRatio(view);
    vec3 centre = Graphite_ToYCoCg(current.rgb);
    history.rgb = max(Graphite_FromYCoCg(clamp(Graphite_ToYCoCg(unclipped), centre - deviation, centre + deviation)), vec3(0.0));

    // Where the gather found no probe suiting the pixel its history holds unclipped, at most twice as bright as
    // clipping would leave it, so a thin surface keeps what the frames that did find one gave it
    if (failed)
    {
        float clipped = Graphite_Luminance(history.rgb), unclippedLuminance = Graphite_Luminance(unclipped);
        history.rgb = unclipped * clamp(clipped / max(unclippedLuminance, 1e-8) * 2.0, 0.01, 1.0);
        outColor = mix(history, current, GI_FAILED_BLEND);
        outMeta.w = _MaxHistory;
        return;
    }

    // Pixels on the move hold half the frames, so their light keeps up
    float moved = length((vUV - previous) * vec2(size));
    float frames = min(max(held / kept, 1.0) + 1.0, mix(_MaxHistory, _MaxHistory * 0.5, clamp(moved, 0.0, 1.0)));
    outColor = mix(history, current, 1.0 / frames);
    outMeta.w = frames;
}
#elif defined(GI_FILTER)
uniform GRAPHITE_SCREEN_TEXTURE _History;
uniform GRAPHITE_SCREEN_TEXTURE _HistoryMeta;
uniform float _MaxHistory;

#define GI_FILTER_STEP 4.0          // the blur's step in texels for a pixel with no history
#define GI_FILTER_SETTLED 0.25      // the share of _MaxHistory after which a pixel takes the narrowest blur

void main()
{
    ivec2 size = Graphite_Size(_History, 0);
    ivec2 own = min(ivec2(vUV * vec2(size)), size - 1);
    vec4 centre = Graphite_Fetch(_History, own, 0);
    vec4 meta = Graphite_Fetch(_HistoryMeta, own, 0);
    outColor = centre;
    if (meta.x == 0.0)
        return;
    vec3 normal = Graphite_OctDecode(meta.yz);
    float fresh = 1.0 - clamp((meta.w - 1.0) / (GI_FILTER_SETTLED * _MaxHistory), 0.0, 1.0);
    float stride = mix(1.0, GI_FILTER_STEP, fresh);
    vec4 sum = centre;
    float weights = 1.0;
    for (int i = 0; i < 25; i++)
    {
        ivec2 o = ivec2(i % 5, i / 5) - 2;
        if (o == ivec2(0))
            continue;
        ivec2 texel = clamp(own + ivec2(round(vec2(o) * stride)), ivec2(0), size - 1);
        vec4 m = Graphite_Fetch(_HistoryMeta, texel, 0);
        if (m.x == 0.0 || !GI_SameDistance(m.x, meta.x, GI_SAME_DISTANCE))
            continue;
        float facing = max(dot(Graphite_OctDecode(m.yz), normal), 0.0);
        float w = exp2(-0.5 * float(dot(o, o))) * facing * facing * facing * facing;
        sum += Graphite_Fetch(_History, texel, 0) * w;
        weights += w;
    }
    outColor = sum / weights;
}
#elif defined(GI_REFERENCE)
uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
uniform GRAPHITE_SCREEN_TEXTURE _Normals;
uniform GRAPHITE_SCREEN_TEXTURE _Reference;
uniform float _ReferenceFrames;     // frames already averaged into _Reference, 0 to start over

#define GI_REFERENCE_PATHS 4
#define GI_REFERENCE_BOUNCES 4

// The light a hit sends back: its direct light, then its albedo times the local lights as the cache holds them
vec3 GI_HitLight(Graphite_VoxelHit hit, vec3 origin, vec3 direction)
{
    uint word = Graphite_VoxelColorWord(hit.voxel);
    vec3 light = Graphite_VoxelHitRadiance(hit, origin, direction);
    return Graphite_VoxelIsEmissive(word) ? light : light + Graphite_VoxelAlbedo(word) * Graphite_VoxelLocalIndirect(hit, true);
}

// With cosine distributed directions, irradiance over pi is the mean light the paths bring back, and the sky's
// visibility the share whose first ray leaves. Past the box the first ray takes the sky as the trace does there.
void main()
{
    ivec2 pixel = min(ivec2(vUV * _ScreenSize), ivec2(_ScreenSize) - 1);
    ivec2 size = Graphite_Size(_Reference, 0);
    vec4 before = Graphite_Fetch(_Reference, min(ivec2(vUV * vec2(size)), size - 1), 0);
    float depth = Graphite_Fetch(_CameraDepthTexture, pixel, 0).r;
    outColor = vec4(0.0);
    if (depth == 0.0)
        return;
    Graphite_ViewData view = Graphite_PostView();
    vec3 relative = Graphite_RelativeFromDepth(view, GI_PixelUV(pixel), depth);
    vec3 normal = Graphite_OctDecode(Graphite_Fetch(_Normals, pixel, 0).rg);
    bool inBox = GI_InBox(view, relative);

    vec3 sum = vec3(0.0);
    float escaped = 0.0;
    for (int path = 0; path < GI_REFERENCE_PATHS; path++)
    {
        vec3 origin = GI_VoxelPosition(view, relative) + normal * GI_VOXEL_LIFT;
        vec3 n = normal;
        vec3 carried = vec3(1.0);
        for (int bounce = 0; bounce < GI_REFERENCE_BOUNCES; bounce++)
        {
            uint key = uint(_Frame) * 64u + uint(path) * 8u + uint(bounce);
            vec3 direction = Graphite_CosineDirection(n, GI_Random(uint(pixel.x), uint(pixel.y) * 2u, key), GI_Random(uint(pixel.x), uint(pixel.y) * 2u + 1u, key));
            Graphite_VoxelHit hit = Graphite_TraceVoxels(origin, direction, 1e6, 512u);
            if (hit.lost)
                break;
            if (!hit.hit)
            {
                float reach;
                vec3 sky = bounce == 0 && !inBox ? GI_SkyPastTheBox(relative, relative, direction, reach)
                    : Graphite_VoxelSkyLeaving(origin, direction, reach);
                sum += carried * sky;
                escaped += bounce == 0 ? reach : 0.0;
                break;
            }
            sum += carried * GI_HitLight(hit, origin, direction);
            carried *= Graphite_VoxelAlbedo(Graphite_VoxelColorWord(hit.voxel));
            if (max(carried.r, max(carried.g, carried.b)) <= 0.0)
                break;
            origin += direction * hit.t + hit.normal * 0.5;
            n = hit.normal;
        }
    }
    vec4 estimate = vec4(Graphite_VoxelToView(sum, view.exposure.x), escaped) / float(GI_REFERENCE_PATHS);
    outColor = (before * _ReferenceFrames + estimate) / (_ReferenceFrames + 1.0);
}
#elif defined(GI_OUTPUT) || defined(GI_VISIBILITY)
uniform GRAPHITE_SCREEN_TEXTURE _History;
uniform GRAPHITE_SCREEN_TEXTURE _HistoryMeta;
uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
uniform GRAPHITE_SCREEN_TEXTURE _Normals;
uniform float _HasVoxels;

#define GI_UPSAMPLE_DISTANCE 0.05   // a difference in camera distance, relative, that halves a texel's weight
#define GI_UPSAMPLE_NORMAL 8.0      // how sharply a texel facing away from the pixel loses weight

// The GI at a pixel of the view. A lower resolution history is brought up from the 2 x 2 texels around the pixel,
// each weighted bilinearly and by how well its distance and normal match the pixel's, or the best match on an edge
// where none suits.
vec4 GI_Upsample()
{
    ivec2 size = Graphite_Size(_History, 0);
    ivec2 pixel = min(ivec2(vUV * _ScreenSize), ivec2(_ScreenSize) - 1);
    if (size == ivec2(_ScreenSize))
        return Graphite_Fetch(_History, pixel, 0);
    float depth = Graphite_Fetch(_CameraDepthTexture, pixel, 0).r;
    if (depth == 0.0)
        return Graphite_Sample(_History, vUV);
    float distance = length(Graphite_RelativeFromDepth(Graphite_PostView(), GI_PixelUV(pixel), depth));
    vec3 normal = Graphite_OctDecode(Graphite_Fetch(_Normals, pixel, 0).rg);

    vec2 at = vUV * vec2(size) - 0.5;
    ivec2 base = ivec2(floor(at));
    vec2 f = at - vec2(base);
    vec4 sum = vec4(0.0);
    float weights = 0.0, bestMatch = -1.0;
    ivec2 best = clamp(ivec2(vUV * vec2(size)), ivec2(0), size - 1);
    for (int i = 0; i < 4; i++)
    {
        ivec2 o = ivec2(i & 1, i >> 1);
        ivec2 texel = clamp(base + o, ivec2(0), size - 1);
        vec4 meta = Graphite_Fetch(_HistoryMeta, texel, 0);
        if (meta.x == 0.0)
            continue;
        float difference = (meta.x - distance) / (GI_UPSAMPLE_DISTANCE * distance);
        float match = exp2(-difference * difference) * pow(max(dot(Graphite_OctDecode(meta.yz), normal), 0.0), GI_UPSAMPLE_NORMAL);
        if (match > bestMatch)
        {
            bestMatch = match;
            best = texel;
        }
        float w = (o.x == 1 ? f.x : 1.0 - f.x) * (o.y == 1 ? f.y : 1.0 - f.y) * match;
        sum += Graphite_Fetch(_History, texel, 0) * w;
        weights += w;
    }
    return weights > 1e-4 ? sum / weights : Graphite_Fetch(_History, best, 0);
}

#if defined(GI_OUTPUT)
void main() { outColor = _HasVoxels != 0.0 ? vec4(GI_Upsample().rgb, 1.0) : vec4(0.0); }
#else
void main() { outColor = vec4(_HasVoxels != 0.0 ? clamp(GI_Upsample().a, 0.0, 1.0) : 1.0); }
#endif
#endif

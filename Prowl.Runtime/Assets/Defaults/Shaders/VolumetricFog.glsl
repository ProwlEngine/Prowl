#pragma variant FOG COMPOSITE FROXEL MARCH
// Volumetric fog as three fullscreen passes. FOG_FROXEL lights a grid of cells fitted to the view, 8 pixels across and
// depth slices squared out to the fog's reach, kept as the slices tiled in one texture: each cell's light scattered per
// unit of fog from the world's lights and shadows, as a lit program reading each light's HostData as its fog colour and
// scattering bias, sampled at a point jittered through the cell each frame and blended with the cell's reprojected
// value from last frame. FOG_MARCH finds where each pixel's ray enters and leaves every fog volume, integrates their
// density per pixel against the cells' light at a reduced resolution, and the default composites it over the colour
// with a depth aware upsample.
// Light is pre-exposed from the cells on, so the half float targets hold it in the range the colour does.
// Positions are camera relative. The host's fog volumes are relative to the view's first layer.

#include "Graphite/Internal/PostView.glsl"

#define FOG_MAX_VOLUMES 16

uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
uniform vec2 _LowResolution;
uniform float _FogMaxDistance;
uniform vec4 _FroxelGrid;     // cells across and down, depth slices, slices along each row of the texture
uniform vec2 _FroxelAtlas;    // the texture's size in texels
uniform float _FogDensity;

// Each volume: its world to local rows, camera relative, then shape, density, falloff and the cone's cosine, then its
// colour and the cone's sine
uniform int _FogVolumeCount;
#define FOG_VOLUME(i) uniform vec4 _FogVolumeRow0_##i; uniform vec4 _FogVolumeRow1_##i; uniform vec4 _FogVolumeRow2_##i; uniform vec4 _FogVolumeParams_##i; uniform vec4 _FogVolumeColor_##i;
FOG_VOLUME(0) FOG_VOLUME(1) FOG_VOLUME(2) FOG_VOLUME(3) FOG_VOLUME(4) FOG_VOLUME(5) FOG_VOLUME(6) FOG_VOLUME(7)
FOG_VOLUME(8) FOG_VOLUME(9) FOG_VOLUME(10) FOG_VOLUME(11) FOG_VOLUME(12) FOG_VOLUME(13) FOG_VOLUME(14) FOG_VOLUME(15)

struct Fog_Volume { vec4 row0; vec4 row1; vec4 row2; vec4 params; vec4 color; };

#define FOG_VOLUME_CASE(i) case i: v.row0 = _FogVolumeRow0_##i; v.row1 = _FogVolumeRow1_##i; v.row2 = _FogVolumeRow2_##i; v.params = _FogVolumeParams_##i; v.color = _FogVolumeColor_##i; break;

Fog_Volume Fog_GetVolume(int i)
{
    Fog_Volume v;
    switch (i)
    {
        FOG_VOLUME_CASE(0) FOG_VOLUME_CASE(1) FOG_VOLUME_CASE(2) FOG_VOLUME_CASE(3)
        FOG_VOLUME_CASE(4) FOG_VOLUME_CASE(5) FOG_VOLUME_CASE(6) FOG_VOLUME_CASE(7)
        FOG_VOLUME_CASE(8) FOG_VOLUME_CASE(9) FOG_VOLUME_CASE(10) FOG_VOLUME_CASE(11)
        FOG_VOLUME_CASE(12) FOG_VOLUME_CASE(13) FOG_VOLUME_CASE(14) default: FOG_VOLUME_CASE(15)
    }
    return v;
}

float Fog_Falloff(Fog_Volume v) { return max(v.params.z, 0.001); }

vec3 Fog_ToLocal(Fog_Volume v, vec3 p)
{
    vec4 q = vec4(p, 1.0);
    return vec3(dot(v.row0, q), dot(v.row1, q), dot(v.row2, q));
}

// A volume's density at a point, fading out over its falloff past its shape, with the shape grown by expand meters
float Fog_VolumeDensity(Fog_Volume v, vec3 p, float expand)
{
    int shape = int(v.params.x);
    if (shape == 0)
        return v.params.y;

    vec3 lp = Fog_ToLocal(v, p);
    float sdf;
    if (shape == 1)
    {
        vec3 d = abs(lp) - vec3(1.0);
        sdf = length(max(d, 0.0)) + min(max(d.x, max(d.y, d.z)), 0.0);
    }
    else if (shape == 2)
    {
        sdf = length(lp) - 1.0;
    }
    else if (shape == 3)
    {
        vec2 d = vec2(length(lp.xz) - 1.0, abs(lp.y) - 1.0);
        sdf = length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
    }
    else
    {
        // Apex at the origin opening up +Y to a cap at height 1
        vec2 c = vec2(length(lp.xz), lp.y);
        sdf = max(c.x * v.params.w - c.y * v.color.w, max(c.y - 1.0, -lp.y));
    }

    // A meter is at most the longest row's length in local units
    float localExpand = expand * sqrt(max(dot(v.row0.xyz, v.row0.xyz), max(dot(v.row1.xyz, v.row1.xyz), dot(v.row2.xyz, v.row2.xyz))));
    return v.params.y * clamp(1.0 - (sdf - localExpand) / Fog_Falloff(v), 0.0, 1.0);
}

// Where depth slice s starts along a ray, fractional slices between
float Fog_SliceDistance(float s)
{
    float f = s / _FroxelGrid.z;
    return _FogMaxDistance * f * f;
}

// The texel of the atlas under a screen uv in a slice, kept half a cell inside the slice's tile so filtering stays in it
vec2 Fog_AtlasUV(vec2 uv, float slice)
{
    vec2 tile = vec2(mod(slice, _FroxelGrid.w), floor(slice / _FroxelGrid.w));
    vec2 cell = clamp(uv * _FroxelGrid.xy, vec2(0.5), _FroxelGrid.xy - 0.5);
    return (tile * _FroxelGrid.xy + cell) / _FroxelAtlas;
}

float Fog_InterleavedGradientNoise(vec2 p)
{
    return fract(52.9829189 * fract(dot(p, vec2(0.06711056, 0.00583715))));
}

float Fog_LinearDepth(Graphite_ViewData view, vec2 uv)
{
    return abs(Graphite_PostViewPosition(view, uv, max(Graphite_Sample(_CameraDepthTexture, uv).r, 1e-7)).z);
}

// Where the pixel's ray starts and the way it runs: from the camera under perspective, from the pixel's point on the
// near plane along the shared view direction under orthographic
void Fog_Ray(Graphite_ViewData view, vec2 uv, out vec3 origin, out vec3 dir)
{
    vec3 near = Graphite_RelativeFromDepth(view, uv, 1.0);
    if (Graphite_PostOrthographic(view))
    {
        origin = near;
        dir = normalize(Graphite_RelativeFromDepth(view, uv, 0.5) - near);
        return;
    }
    origin = vec3(0.0);
    dir = normalize(near);
}

#if defined(FOG_FROXEL)

// Fullscreen programs take no world keywords, so the fog always reads cookies and profiles
#define GRAPHITE_LIGHT_MASKS 1
#include "Graphite/Lighting.glsl"

uniform GRAPHITE_SCREEN_TEXTURE _FroxelHistoryTex;
uniform float _FogHistoryValid;
uniform float _FogTemporalBlend;

uniform float _FogScattering;
uniform vec4 _FogAmbientColor;
uniform float _FogAmbientIntensity;
uniform vec3 _FogLightTypes;

// cosTheta is between the view ray and the direction to the light, so with g above 0 the glow sits around the light
float Fog_HenyeyGreenstein(float cosTheta, float g)
{
    float g2 = g * g;
    float denom = 1.0 + g2 - 2.0 * g * cosTheta;
    return (1.0 - g2) / (4.0 * 3.14159265 * pow(max(denom, 1e-4), 1.5));
}

// One light's scattering toward the eye, from its HostData colour and bias
vec3 Fog_Scatter(vec3 color, float hostBias, vec3 viewDir, vec3 toLight, float attenuation)
{
    float bias = hostBias > 3.0 ? hostBias - 4.0 : hostBias;
    float g = clamp(_FogScattering + bias, -0.99, 0.99);
    return color * (8.0 * attenuation * Fog_HenyeyGreenstein(dot(viewDir, toLight), g));
}

// Local lights fall off over the cell's radius squared too, the cell's average rather than the spike at its point
vec3 Fog_SampleScattering(vec3 p, vec3 viewDir, float radius2)
{
    vec3 scatter = vec3(0.0);
    if (_FogLightTypes.x > 0.5)
    {
        uint count = Graphite_DirectionalCount();
        for (uint i = 0u; i < count; i++)
        {
            Graphite_DirectionalLight d = Graphite_GetDirectional(i);
            if (dot(d.hostData.rgb, d.hostData.rgb) <= 0.0)
                continue;
            float shadow = d.hostData.w > 3.0 ? 1.0 : Graphite_DirectionalLightShadow(d, i, p, d.direction);
            scatter += Fog_Scatter(d.hostData.rgb, d.hostData.w, viewDir, d.direction, shadow);
        }
    }

    Graphite_LightIterator it = Graphite_BeginLights(p);
    Graphite_LightSample L;
    while (Graphite_NextLight(it, L))
    {
        bool spot = L.type == GRAPHITE_LIGHT_SPOT;
        if ((L.type != GRAPHITE_LIGHT_POINT && !spot) || (spot ? _FogLightTypes.z : _FogLightTypes.y) < 0.5)
            continue;
        vec3 d = L.position - p;
        float dist2 = dot(d, d);
        vec3 toLight = d * inversesqrt(max(dist2, 1e-8));
        float attenuation = Graphite_Window(dist2, L.range) / max(dist2 + radius2, 0.01);
        if (spot)
        {
            vec2 spotCos = Graphite_LightSpotCos(L);
            attenuation *= smoothstep(spotCos.x, spotCos.y, dot(Graphite_LightDirection(L), -toLight));
        }
        vec4 fog = Graphite_LightHostData(L);
        if (attenuation <= 0.0 || dot(fog.rgb, fog.rgb) <= 0.0)
            continue;
        if (fog.w <= 3.0)
            attenuation *= Graphite_LocalLightShadow(L, p, toLight);
        scatter += Fog_Scatter(fog.rgb * Graphite_LightMask(L, -toLight), fog.w, viewDir, toLight, attenuation);
    }
    return scatter;
}

// Whether any fog could reach within radius of a point, so cells far from every volume skip their lighting
bool Fog_AnyFogNear(vec3 p, float radius)
{
    if (_FogDensity > 0.0)
        return true;
    int count = min(_FogVolumeCount, FOG_MAX_VOLUMES);
    for (int i = 0; i < count; i++)
    {
        if (Fog_VolumeDensity(Fog_GetVolume(i), p, radius) > 0.0)
            return true;
    }
    return false;
}

// Last frame's cells at a screen uv and fractional slice, Catmull-Rom across four slices held between the middle two,
// so moving through the slices does not smear them together frame after frame
vec4 Fog_History(vec2 uv, float slice)
{
    float last = _FroxelGrid.z - 1.0;
    float s = clamp(slice - 0.5, 0.0, last);
    float s1 = floor(s);
    float f = s - s1;
    vec4 a = Graphite_SampleLod(_FroxelHistoryTex, Fog_AtlasUV(uv, max(s1 - 1.0, 0.0)), 0.0);
    vec4 b = Graphite_SampleLod(_FroxelHistoryTex, Fog_AtlasUV(uv, s1), 0.0);
    vec4 c = Graphite_SampleLod(_FroxelHistoryTex, Fog_AtlasUV(uv, min(s1 + 1.0, last)), 0.0);
    vec4 d = Graphite_SampleLod(_FroxelHistoryTex, Fog_AtlasUV(uv, min(s1 + 2.0, last)), 0.0);
    vec4 result = a * (f * (-0.5 + f * (1.0 - 0.5 * f)))
        + b * (1.0 + f * f * (-2.5 + 1.5 * f))
        + c * (f * (0.5 + f * (2.0 - 1.5 * f)))
        + d * (f * f * (-0.5 + 0.5 * f));
    return clamp(result, min(b, c), max(b, c));
}

// Where a camera relative point was in last frame's grid, as a screen uv and fractional slice
bool Fog_Reproject(Graphite_ViewData view, vec3 p, vec3 origin, vec3 viewDir, out vec2 uv, out float slice)
{
    vec3 moved = p + (view.cameraHigh.xyz - view.previousCameraHigh.xyz) + (view.cameraLow.xyz - view.previousCameraLow.xyz);
    uv = Graphite_PostPreviousUV(view, p);
    float distance = Graphite_PostOrthographic(view) ? dot(moved - origin, viewDir) : length(moved);
    slice = sqrt(max(distance, 0.0) / _FogMaxDistance) * _FroxelGrid.z;
    return all(greaterThanEqual(uv, vec2(0.0))) && all(lessThanEqual(uv, vec2(1.0))) && slice <= _FroxelGrid.z;
}

// One cell: the light it scatters toward the eye per unit of fog, pre-exposed, ambient included as a stand in for sky bounce
void main()
{
    Graphite_ViewData view = Graphite_PostView();
    vec2 texel = floor(vUV * _FroxelAtlas);
    vec2 tile = floor(texel / _FroxelGrid.xy);
    float slice = tile.y * _FroxelGrid.w + tile.x;
    if (slice >= _FroxelGrid.z)
    {
        outColor = vec4(0.0);
        return;
    }

    // The cell's centre, and the radius of a sphere around it holding the whole cell
    vec2 cell = texel - tile * _FroxelGrid.xy;
    vec2 centreUV = (cell + 0.5) / _FroxelGrid.xy;
    vec3 origin, viewDir, nextOrigin, nextDir;
    Fog_Ray(view, centreUV, origin, viewDir);
    Fog_Ray(view, centreUV + vec2(1.0 / _FroxelGrid.x, 0.0), nextOrigin, nextDir);
    float t = Fog_SliceDistance(slice + 0.5);
    vec3 centre = origin + viewDir * t;
    float across = distance(centre, nextOrigin + nextDir * t);
    float deep = Fog_SliceDistance(slice + 1.0) - Fog_SliceDistance(slice);
    float radius = 0.5 * sqrt(2.0 * across * across + deep * deep);

    // The host's volumes are relative to the first layer's camera, the world's lights to this layer's
    if (!Fog_AnyFogNear(centre + view.treeOffset.xyz, radius))
    {
        outColor = vec4(0.0);
        return;
    }

    // Each cell samples a different point through its volume each frame, decorrelated from its neighbours
    float frame = view.time.z;
    float noise = Fog_InterleavedGradientNoise(cell + vec2(slice * 7.0, slice * 13.0) + 5.588238 * mod(frame, 64.0));
    vec2 offset = fract(vec2(0.7548777, 0.5698403) * mod(frame, 1024.0) + noise) - 0.5;
    vec3 sampleOrigin, sampleDir;
    Fog_Ray(view, (cell + 0.5 + offset) / _FroxelGrid.xy, sampleOrigin, sampleDir);
    vec3 p = sampleOrigin + sampleDir * Fog_SliceDistance(slice + noise);
    vec3 ambient = _FogAmbientColor.rgb * _FogAmbientIntensity;
    vec4 current = vec4((Fog_SampleScattering(p, sampleDir, radius * radius) + ambient) * view.exposure.x, 0.0);

    vec2 previousUV;
    float previousSlice;
    if (_FogHistoryValid < 0.5 || !Fog_Reproject(view, centre, origin, viewDir, previousUV, previousSlice))
    {
        outColor = current;
        return;
    }
    outColor = mix(current, Fog_History(previousUV, previousSlice) * Graphite_PostExposureRatio(view), _FogTemporalBlend);
}

#elif defined(FOG_MARCH)

uniform GRAPHITE_SCREEN_TEXTURE _FroxelTex;
uniform float _FogDithering;
uniform float _FogExtinction;
uniform vec4 _FogColorTint;

const vec2 FOG_EMPTY = vec2(1e30, -1e30);
const vec2 FOG_ALL = vec2(-1e30, 1e30);

vec2 Fog_Overlap(vec2 a, vec2 b) { return vec2(max(a.x, b.x), min(a.y, b.y)); }

// Where a ray's coordinate along one axis lies between lo and hi
vec2 Fog_Slab(float o, float d, float lo, float hi)
{
    if (abs(d) < 1e-8)
        return o >= lo && o <= hi ? FOG_ALL : FOG_EMPTY;
    float a = (lo - o) / d, b = (hi - o) / d;
    return vec2(min(a, b), max(a, b));
}

// Where a t squared plus b t plus c is below zero, as one span. Under a negative a only the half the caller's slab
// keeps is wanted, so the two outer spans are clipped to it and joined
vec2 Fog_Quadric(float a, float b, float c, vec2 slab)
{
    if (abs(a) < 1e-10)
    {
        if (abs(b) < 1e-10)
            return c < 0.0 ? slab : FOG_EMPTY;
        float root = -c / b;
        return Fog_Overlap(slab, b > 0.0 ? vec2(-1e30, root) : vec2(root, 1e30));
    }
    float disc = b * b - 4.0 * a * c;
    if (disc < 0.0)
        return a > 0.0 ? FOG_EMPTY : slab;
    float q = sqrt(disc);
    vec2 roots = vec2(min(-b - q, -b + q), max(-b - q, -b + q)) / (2.0 * a);
    if (a > 0.0)
        return Fog_Overlap(slab, roots);
    vec2 before = Fog_Overlap(slab, vec2(-1e30, roots.x));
    vec2 after = Fog_Overlap(slab, vec2(roots.y, 1e30));
    if (before.x >= before.y)
        return after;
    if (after.x >= after.y)
        return before;
    return vec2(before.x, after.y);
}

// The span of a ray where a volume can hold fog, its shape grown by its falloff, solved exactly in its local space
vec2 Fog_VolumeSpan(Fog_Volume v, vec3 origin, vec3 dir)
{
    int shape = int(v.params.x);
    if (shape == 0)
        return FOG_ALL;

    float f = Fog_Falloff(v);
    vec3 o = Fog_ToLocal(v, origin);
    vec3 d = vec3(dot(v.row0.xyz, dir), dot(v.row1.xyz, dir), dot(v.row2.xyz, dir));
    if (shape == 1)
    {
        float e = 1.0 + f;
        return Fog_Overlap(Fog_Overlap(Fog_Slab(o.x, d.x, -e, e), Fog_Slab(o.y, d.y, -e, e)), Fog_Slab(o.z, d.z, -e, e));
    }
    if (shape == 2)
    {
        float r = 1.0 + f;
        return Fog_Quadric(dot(d, d), 2.0 * dot(o, d), dot(o, o) - r * r, FOG_ALL);
    }
    if (shape == 3)
    {
        float r = 1.0 + f;
        vec2 slab = Fog_Slab(o.y, d.y, -r, r);
        return Fog_Quadric(dot(d.xz, d.xz), 2.0 * dot(o.xz, d.xz), dot(o.xz, o.xz) - r * r, slab);
    }

    // The cone grown by the falloff keeps its angle and drops its apex, its radius at height y being k y plus b
    float cosine = max(v.params.w, 1e-4);
    float k = v.color.w / cosine;
    float b = f / cosine;
    float ro = k * o.y + b;
    vec2 slab = Fog_Slab(o.y, d.y, -f, 1.0 + f);
    return Fog_Quadric(dot(d.xz, d.xz) - k * k * d.y * d.y, 2.0 * (dot(o.xz, d.xz) - k * d.y * ro), dot(o.xz, o.xz) - ro * ro, slab);
}

// The cell of a slice under a screen uv, filtered across its neighbours in the slice
vec3 Fog_Froxel(vec2 uv, float slice)
{
    return Graphite_SampleLod(_FroxelTex, Fog_AtlasUV(uv, slice), 0.0).rgb;
}

void main()
{
    Graphite_ViewData view = Graphite_PostView();
    vec2 uv = vUV;
    float depth = Graphite_Sample(_CameraDepthTexture, uv).r;

    vec3 origin, viewDir;
    Fog_Ray(view, uv, origin, viewDir);

    // Distance along the ray to the geometry, not its view depth, so off axis pixels stop on the surface
    float sceneDist = depth <= 1e-7 ? _FogMaxDistance : distance(Graphite_RelativeFromDepth(view, uv, depth), origin);
    float end = min(sceneDist, _FogMaxDistance);

    // Where the ray meets each volume. The host's volumes are relative to the first layer's camera
    vec3 volumeOrigin = origin + view.treeOffset.xyz;
    int count = min(_FogVolumeCount, FOG_MAX_VOLUMES);
    vec2 spans[FOG_MAX_VOLUMES];
    float reach = _FogDensity > 0.0 ? end : 0.0;
    for (int i = 0; i < count; i++)
    {
        spans[i] = Fog_Overlap(Fog_VolumeSpan(Fog_GetVolume(i), volumeOrigin, viewDir), vec2(0.0, end));
        if (spans[i].x < spans[i].y)
            reach = max(reach, spans[i].y);
    }

    // Each slice's fog integrated per pixel, two points inside each volume's part of it, scattering the cell's light
    // against the light it loses on the way out
    vec3 accum = vec3(0.0);
    float transmittance = 1.0;
    for (float slice = 0.0; slice < _FroxelGrid.z && transmittance > 0.01; slice += 1.0)
    {
        float t0 = Fog_SliceDistance(slice);
        if (t0 >= reach)
            break;
        float t1 = min(Fog_SliceDistance(slice + 1.0), end);

        float fog = max(_FogDensity, 0.0) * (t1 - t0);
        float positive = fog;
        vec3 colored = vec3(fog);
        for (int i = 0; i < count; i++)
        {
            vec2 part = Fog_Overlap(spans[i], vec2(t0, t1));
            if (part.x >= part.y)
                continue;
            Fog_Volume v = Fog_GetVolume(i);
            float middle = 0.5 * (part.x + part.y);
            float gauss = 0.2886751 * (part.y - part.x);
            float amount = 0.5 * (part.y - part.x) * (Fog_VolumeDensity(v, volumeOrigin + viewDir * (middle - gauss), 0.0)
                + Fog_VolumeDensity(v, volumeOrigin + viewDir * (middle + gauss), 0.0));
            fog += amount;
            positive += max(amount, 0.0);
            colored += v.color.rgb * max(amount, 0.0);
        }
        fog = max(fog, 0.0);
        if (fog <= 0.0)
            continue;

        float optical = fog * _FogExtinction;
        float sliceTransmittance = exp(-optical);
        float scattered = optical > 1e-4 ? fog * (1.0 - sliceTransmittance) / optical : fog;
        vec3 color = colored / max(positive, 1e-6) * _FogColorTint.rgb;
        accum += transmittance * Fog_Froxel(uv, slice) * color * scattered;
        transmittance *= sliceTransmittance;
    }

    // A small fixed shift against banding, scaled by how much fog the ray met
    if (_FogDithering > 0.0)
    {
        float dither = (Fog_InterleavedGradientNoise(gl_FragCoord.xy) - 0.5) * _FogDithering;
        accum = max(accum + vec3(dither) * (1.0 - transmittance), vec3(0.0));
    }

    outColor = vec4(accum, transmittance);
}

#else

uniform GRAPHITE_SCREEN_TEXTURE _FogTex;
uniform float _FogUpsampleThreshold;

// The low resolution taps around the pixel blended by how close their depth is to its own, or the closest alone when
// none is close, so fog does not bleed across silhouettes
vec4 Fog_Upsample(Graphite_ViewData view, vec2 uv, float refDepth)
{
    vec2 lowTexel = 1.0 / _LowResolution;
    vec2 offsets[4] = vec2[](vec2(-0.5, -0.5), vec2(0.5, -0.5), vec2(-0.5, 0.5), vec2(0.5, 0.5));
    float depths[4];
    float bestDiff = 1e30;
    int best = 0;
    for (int i = 0; i < 4; i++)
    {
        depths[i] = Fog_LinearDepth(view, uv + offsets[i] * lowTexel);
        float diff = abs(depths[i] - refDepth);
        if (diff < bestDiff)
        {
            bestDiff = diff;
            best = i;
        }
    }

    if (bestDiff / max(refDepth, 1.0) >= _FogUpsampleThreshold)
        return Graphite_Sample(_FogTex, uv + offsets[best] * lowTexel);

    vec4 sum = vec4(0.0);
    float total = 0.0;
    for (int i = 0; i < 4; i++)
    {
        float w = exp(-abs(depths[i] - refDepth) / max(refDepth, 1.0) / max(_FogUpsampleThreshold, 1e-4));
        sum += Graphite_Sample(_FogTex, uv + offsets[i] * lowTexel) * w;
        total += w;
    }
    return sum / max(total, 1e-4);
}

void main()
{
    Graphite_ViewData view = Graphite_PostView();
    vec4 scene = Graphite_SampleSource(vUV);
    vec4 fog = Fog_Upsample(view, vUV, Fog_LinearDepth(view, vUV));
    outColor = vec4(scene.rgb * fog.a + fog.rgb, scene.a);
}

#endif

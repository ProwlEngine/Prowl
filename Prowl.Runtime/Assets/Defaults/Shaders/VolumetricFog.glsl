// Volumetric fog as four fullscreen passes. FOG_FROXEL lights a grid of cells fitted to the view, 8 pixels across and
// depth slices squared out to the fog's reach, kept as the slices tiled in one texture: each cell's scattered light and
// extinction from the world's lights and shadows, as a lit program reading each light's HostData as its fog colour and
// scattering bias. FOG_MARCH integrates the cells along each pixel's ray at a reduced resolution, FOG_TEMPORAL blends
// it with last frame's reprojected result, and the default composites it over the colour with a depth aware upsample.
// Positions are camera relative. The host's fog volumes are relative to the view's first layer.

#include "Graphite/Internal/PostView.glsl"

#define FOG_MAX_VOLUMES 16

uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
uniform vec2 _LowResolution;
uniform float _FogMaxDistance;
uniform float _FogTime;
uniform vec4 _FroxelGrid;     // cells across and down, depth slices, slices along each row of the texture
uniform vec2 _FroxelAtlas;    // the texture's size in texels

// Where depth slice s starts along a ray, fractional slices between
float Fog_SliceDistance(float s)
{
    float f = s / _FroxelGrid.z;
    return _FogMaxDistance * f * f;
}

float Fog_Hash13(vec3 p)
{
    p = fract(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return fract((p.x + p.y) * p.z);
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
    vec3 near = Graphite_PostRelativePosition(view, uv, 1.0);
    if (Graphite_PostOrthographic(view))
    {
        origin = near;
        dir = normalize(Graphite_PostRelativePosition(view, uv, 0.5) - near);
        return;
    }
    origin = vec3(0.0);
    dir = normalize(near);
}

#if defined(FOG_FROXEL)

// Fullscreen programs take no world keywords, so the fog always reads cookies and profiles
#define GRAPHITE_LIGHT_MASKS 1
#include "Graphite/Lighting.glsl"

uniform float _FogDensity;
uniform float _FogScattering;
uniform float _FogExtinction;
uniform vec4 _FogColorTint;
uniform vec4 _FogAmbientColor;
uniform float _FogAmbientIntensity;
uniform vec3 _FogLightTypes;

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

// cosTheta is between the view ray and the direction to the light, so with g above 0 the glow sits around the light
float Fog_HenyeyGreenstein(float cosTheta, float g)
{
    float g2 = g * g;
    float denom = 1.0 + g2 - 2.0 * g * cosTheta;
    return (1.0 - g2) / (4.0 * 3.14159265 * pow(max(denom, 1e-4), 1.5));
}

// Density and colour of the volumes at a point. Globals add everywhere, shapes fade out over their falloff past an SDF
void Fog_EvaluateVolumes(vec3 p, out float density, out vec3 color)
{
    density = 0.0;
    color = vec3(0.0);
    float colorWeight = 0.0;
    int count = min(_FogVolumeCount, FOG_MAX_VOLUMES);
    for (int i = 0; i < count; i++)
    {
        Fog_Volume v = Fog_GetVolume(i);
        int shape = int(v.params.x);
        float falloff = max(v.params.z, 0.001);
        float w = 1.0;
        if (shape != 0)
        {
            vec4 q = vec4(p, 1.0);
            vec3 lp = vec3(dot(v.row0, q), dot(v.row1, q), dot(v.row2, q));
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
            w = clamp(1.0 - sdf / falloff, 0.0, 1.0);
        }

        float d = v.params.y * w;
        density += d;
        color += v.color.rgb * max(d, 0.0);
        colorWeight += max(d, 0.0);
    }
    color = colorWeight > 1e-4 ? color / colorWeight : vec3(1.0);
}

// One light's scattering toward the eye, from its HostData colour and bias
vec3 Fog_Scatter(vec3 color, float hostBias, vec3 viewDir, vec3 toLight, float attenuation)
{
    float bias = hostBias > 3.0 ? hostBias - 4.0 : hostBias;
    float g = clamp(_FogScattering + bias, -0.99, 0.99);
    return color * (8.0 * attenuation * Fog_HenyeyGreenstein(dot(viewDir, toLight), g));
}

vec3 Fog_SampleScattering(vec3 p, vec3 viewDir, vec3 volColor)
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
    LightSample L;
    while (Graphite_NextLight(it, L))
    {
        bool spot = L.Type == GRAPHITE_LIGHT_SPOT;
        if ((L.Type != GRAPHITE_LIGHT_POINT && !spot) || (spot ? _FogLightTypes.z : _FogLightTypes.y) < 0.5)
            continue;
        vec3 d = L.Position - p;
        float dist2 = dot(d, d);
        vec3 toLight = d * inversesqrt(max(dist2, 1e-8));
        float attenuation = Graphite_Window(dist2, L.Range) / max(dist2, 0.01);
        if (spot)
            attenuation *= smoothstep(L.SpotCos, L.InnerSpotCos, dot(L.Direction, -toLight));
        vec4 fog = Graphite_LightHostData(L);
        if (attenuation <= 0.0 || dot(fog.rgb, fog.rgb) <= 0.0)
            continue;
        if (fog.w <= 3.0)
            attenuation *= Graphite_LocalLightShadow(L, p, toLight);
        scatter += Fog_Scatter(fog.rgb * Graphite_LightMask(L, -toLight), fog.w, viewDir, toLight, attenuation);
    }
    return scatter * volColor;
}

// One cell: its scattered light per meter and extinction per meter, at a point moved through its slice each frame
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

    vec2 cellUV = (texel - tile * _FroxelGrid.xy + 0.5) / _FroxelGrid.xy;
    vec3 origin, viewDir;
    Fog_Ray(view, cellUV, origin, viewDir);
    // One offset through the slices for every cell a frame, so neighbours agree and the temporal blend averages frames
    float jitter = fract(floor(_FogTime * 60.0) * 0.618034);
    vec3 p = origin + viewDir * Fog_SliceDistance(slice + jitter);

    // The host's volumes are relative to the first layer's camera, the world's lights to this layer's
    float volumeDensity;
    vec3 volColor;
    Fog_EvaluateVolumes(p + view.treeOffset.xyz, volumeDensity, volColor);
    float density = max(_FogDensity + volumeDensity, 0.0);
    if (density <= 0.0)
    {
        outColor = vec4(0.0);
        return;
    }

    // Ambient keeps fog visible in shadow, a stand in for sky bounce
    vec3 ambient = _FogAmbientColor.rgb * _FogAmbientIntensity * volColor;
    vec3 inscatter = (Fog_SampleScattering(p, viewDir, volColor) + ambient) * density * _FogColorTint.rgb;
    outColor = vec4(inscatter, density * _FogExtinction);
}

#elif defined(FOG_MARCH)

uniform GRAPHITE_SCREEN_TEXTURE _FroxelTex;
uniform float _FogDithering;

// The cell of a slice under a screen uv, filtered across its neighbours in the slice
vec4 Fog_Froxel(vec2 uv, float slice)
{
    vec2 tile = vec2(mod(slice, _FroxelGrid.w), floor(slice / _FroxelGrid.w));
    vec2 cell = clamp(uv * _FroxelGrid.xy, vec2(0.5), _FroxelGrid.xy - 0.5);
    return Graphite_SampleLod(_FroxelTex, (tile * _FroxelGrid.xy + cell) / _FroxelAtlas, 0.0);
}

void main()
{
    Graphite_ViewData view = Graphite_PostView();
    vec2 uv = vUV;
    float depth = Graphite_Sample(_CameraDepthTexture, uv).r;

    vec3 origin, viewDir;
    Fog_Ray(view, uv, origin, viewDir);

    // Distance along the ray to the geometry, not its view depth, so off axis pixels stop on the surface
    float sceneDist = depth <= 1e-7 ? _FogMaxDistance : distance(Graphite_PostRelativePosition(view, uv, depth), origin);
    float end = min(sceneDist, _FogMaxDistance);

    // Each slice's scattering integrated against the light it loses on the way out
    vec3 accum = vec3(0.0);
    float transmittance = 1.0;
    for (float slice = 0.0; slice < _FroxelGrid.z && transmittance > 0.01; slice += 1.0)
    {
        float t0 = Fog_SliceDistance(slice);
        if (t0 >= end)
            break;
        float span = min(Fog_SliceDistance(slice + 1.0), end) - t0;
        vec4 cell = Fog_Froxel(uv, slice);
        float extinction = cell.a;
        float sliceTransmittance = exp(-extinction * span);
        accum += transmittance * (extinction > 1e-4 ? cell.rgb * (1.0 - sliceTransmittance) / extinction : cell.rgb * span);
        transmittance *= sliceTransmittance;
    }

    // A small random shift against banding, scaled by how much fog the ray met
    if (_FogDithering > 0.0)
    {
        float dither = (Fog_Hash13(vec3(gl_FragCoord.xy, _FogTime)) - 0.5) * _FogDithering;
        accum = max(accum + vec3(dither) * (1.0 - transmittance), vec3(0.0));
    }

    outColor = vec4(accum, transmittance);
}

#elif defined(FOG_TEMPORAL)

uniform GRAPHITE_SCREEN_TEXTURE _FogCurrentTex;
uniform GRAPHITE_SCREEN_TEXTURE _FogHistoryTex;
uniform float _FogHistoryValid;
uniform float _FogTemporalBlend;

void main()
{
    Graphite_ViewData view = Graphite_PostView();
    vec2 uv = vUV;
    vec4 current = Graphite_Sample(_FogCurrentTex, uv);
    if (_FogHistoryValid < 0.5)
    {
        outColor = current;
        return;
    }

    // The fog sits on the geometry behind each pixel, or at the march's end over the sky
    float depth = Graphite_Sample(_CameraDepthTexture, uv).r;
    vec3 origin, dir;
    Fog_Ray(view, uv, origin, dir);
    vec3 position = depth <= 1e-7 ? origin + dir * _FogMaxDistance : Graphite_PostRelativePosition(view, uv, depth);
    vec2 previousUV = Graphite_PostPreviousUV(view, position);
    if (any(lessThan(previousUV, vec2(0.0))) || any(greaterThan(previousUV, vec2(1.0))))
    {
        outColor = current;
        return;
    }

    // History held within this frame's 3x3 neighbourhood, against disocclusion and moving lights
    vec4 history = Graphite_Sample(_FogHistoryTex, previousUV);
    vec2 texel = 1.0 / _LowResolution;
    vec4 lowest = current, highest = current;
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            vec4 s = Graphite_Sample(_FogCurrentTex, uv + vec2(float(x), float(y)) * texel);
            lowest = min(lowest, s);
            highest = max(highest, s);
        }
    }
    outColor = mix(current, clamp(history, lowest, highest), clamp(_FogTemporalBlend, 0.0, 0.99));
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

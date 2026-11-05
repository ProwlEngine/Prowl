#pragma variant CLOUD COMPOSITE MAPS DEPTH MARCH TEMPORAL
// Volumetric clouds as fullscreen passes, one per define:
// CLOUD_MAPS       how cloudy each point of the ground plane is and how much cloud the sunlight crosses to reach four
//                  heights through the layer, over a square around the camera. Kept from frame to frame, only the
//                  texels the square newly covers and a slice of the rest are drawn again
// CLOUD_DEPTH      scene distance at the clouds' resolution
// CLOUD_MARCH      the cloud layer and the flat layers marched front to back at the clouds' resolution
// CLOUD_TEMPORAL   the clouds built up at their target resolution from one pixel of each 2x2 block a frame
// otherwise        the clouds scaled up over the colour, matched to the scene's depth
// Cloud positions are absolute, the camera's from the host. Each ray's planet centre sits straight under the camera.
// The maps ride the wind: a texel holds one point of the pattern for as long as it is in the square, wrapped around the
// map by its place on the wind's lattice, so the maps are read with repeat at the position relative to the wind.

#include "Graphite/Internal/PostView.glsl"

uniform sampler2D _CloudCoverage;
uniform sampler2D _CloudNoise2D;
uniform sampler3D _CloudNoise3D;
uniform sampler2D _CloudSunDepth;
uniform sampler2D _CloudProfile;
uniform vec4 _CloudCoverageRect; // min x, min z, 1 / span, span
uniform vec4 _CloudShape;        // base altitude, thickness, 1 / thickness, planet radius
uniform vec4 _CloudPattern;      // 1 / pattern scale, coverage, 1 / detail scale, erosion
uniform vec2 _CloudWind;
uniform vec3 _CloudToSun;        // toward the sun, straight up when the scene has none
uniform float _CloudErosionSize; // texels along each side of the 3D noise
uniform float _CloudFormation;   // how far the formations have changed, along the 3D noise's third axis

// The weather at a point relative to the wind, what the coverage map stores: broad noise that thins and thickens the
// sky, and how tall the clouds there grow
vec2 CloudWeatherNoise(vec2 windRel)
{
    vec2 q = windRel * _CloudPattern.x;
    float broad = textureLod(_CloudNoise3D, vec3(q * 0.5, 0.25), 0.0).b;
    float tall = smoothstep(0.3, 0.7, textureLod(_CloudNoise3D, vec3(q * 1.7 + vec2(0.43, 0.71), 0.75), 0.0).b);
    return vec2(broad, mix(0.3, 1.0, tall));
}

// Cloudiness at a point: 0 at a cloud's edge, rising to 1 in its core. Smooth noise, bent by more smooth noise, gives
// the clouds irregular outlines, cells of the 3D noise add lumps to them, and the weather's broad noise thins and
// thickens the sky. Read live as the noise moves through time, so clouds change shape smoothly. Also gives the shape
// noise alone, which lifts and lowers the tops even where the sky is fully covered
vec2 CloudCloudiness(vec2 windRel, float broad)
{
    vec2 q = windRel * _CloudPattern.x;
    float z = _CloudFormation;
    vec2 bend = vec2(textureLod(_CloudNoise3D, vec3(q * 0.7, z + 0.13), 0.0).b,
                     textureLod(_CloudNoise3D, vec3(q * 0.7 + 0.31, z + 0.61), 0.0).b) - 0.5;
    vec2 p = q * 1.3 + bend * 0.55;
    vec2 small = mat2(0.8, -0.6, 0.6, 0.8) * (p * 2.7) + 0.37;
    vec2 noise = textureLod(_CloudNoise3D, vec3(p, z), 0.0).ba;
    float lumps = textureLod(_CloudNoise3D, vec3(small, z * 2.0 + 0.5), 0.0).a;
    float shape = noise.x * 0.55 + noise.y * 0.25 + lumps * 0.2;
    float n = broad * 0.4 + shape * 0.6;
    float threshold = 0.9 - _CloudPattern.y;
    return vec2(clamp((n - threshold) * 4.0, 0.0, 1.0), shape);
}

// Where a ray from cameraHeight meets the sphere at altitude around the planet's centre, both roots in order, or an
// empty range. Written so neither root loses its precision to the planet's size
vec2 CloudShellRoots(vec3 dir, float cameraHeight, float altitude)
{
    float planet = _CloudShape.w;
    float b = (cameraHeight + planet) * dir.y;
    float c = (cameraHeight - altitude) * (cameraHeight + altitude + 2.0 * planet);
    float disc = b * b - c;
    if (disc < 0.0)
        return vec2(1e30, -1e30);
    float q = -(b + (b >= 0.0 ? 1.0 : -1.0) * sqrt(disc));
    float t0 = q, t1 = abs(q) > 1e-6 ? c / q : q;
    return vec2(min(t0, t1), max(t0, t1));
}

// The nearest point ahead where the ray meets the sphere at altitude, or -1
float CloudShellDistance(vec3 dir, float cameraHeight, float altitude)
{
    vec2 roots = CloudShellRoots(dir, cameraHeight, altitude);
    return roots.x > 0.0 ? roots.x : roots.y > 0.0 ? roots.y : -1.0;
}

float CloudHenyeyGreenstein(float cosTheta, float g)
{
    float g2 = g * g;
    return (1.0 - g2) / (4.0 * 3.14159265 * pow(max(1.0 + g2 - 2.0 * g * cosTheta, 1e-4), 1.5));
}

// The phase of each order of scattering for a ray, per steradian, each order scattering less forward than the last
vec3 CloudPhases(float cosTheta, float silverLining)
{
    vec3 phases;
    float forward = 1.0;
    for (int order = 0; order < 3; order++)
    {
        float lobes = mix(CloudHenyeyGreenstein(cosTheta, 0.2 * forward), CloudHenyeyGreenstein(cosTheta, 0.85 * forward), silverLining * 0.6);
        phases[order] = lobes;
        forward *= 0.5;
    }
    return phases;
}

uniform vec2 _CloudScatter;         // how strong each further order of scattering is, how much of the cloud it still crosses

// Sunlight reaching a point behind tau of optical depth, scattered toward the viewer. Each further order of scattering
// is dimmer but crosses less of the cloud, so light soaks deep into thick cloud and bases stay grey, not black
float CloudScattering(float tau, vec3 phases)
{
    vec2 strength = vec2(_CloudScatter.x, _CloudScatter.x * _CloudScatter.x);
    vec2 crossed = vec2(_CloudScatter.y, _CloudScatter.y * _CloudScatter.y);
    return exp(-tau) * phases.x + strength.x * exp(-tau * crossed.x) * phases.y + strength.y * exp(-tau * crossed.y) * phases.z;
}

// How a cloud changes with height through the layer: how far in from its edge it reaches, how dense it is and how much
// the detail noise eats it, baked by the host for the coverage
vec3 CloudProfile(float h)
{
    vec3 profile = textureLod(_CloudProfile, vec2(h, 0.5), 0.0).rgb;
    profile.y = profile.y * profile.y * profile.y;
    return profile;
}

vec2 CloudMapUV(vec2 windRel)
{
    return windRel * _CloudCoverageRect.z;
}

#if defined(CLOUD_MAPS)
vec2 CloudWeatherAt(vec2 windRel) { return CloudWeatherNoise(windRel); }
#else
vec2 CloudWeatherAt(vec2 windRel) { return textureLod(_CloudCoverage, CloudMapUV(windRel), 0.0).rg; }
#endif

// The cloud at a point before its detail: its cloudiness less the profile's taper, so clouds narrow toward their tops
// and sit on flat bases. The profile is squeezed into how tall the cloud grows there, low at its edges, up to what the
// weather allows in its core and lifted and lowered by the shape noise, so clouds rise in domes rather than walls and
// a covered sky still has an uneven top. The shape thins over the top of each cloud, so erosion carves billows there.
// The point is relative to the wind across, absolute in height
float CloudShape(vec3 q, out vec3 profile)
{
    float h = (q.y - _CloudShape.x) * _CloudShape.z;
    profile = vec3(0.0);
    if (h <= 0.0 || h >= 1.0)
        return 0.0;
    vec2 weather = CloudWeatherAt(q.xz);
    if (h >= weather.y)
        return 0.0;
    vec2 cloud = CloudCloudiness(q.xz, weather.x);
    float cloudiness = cloud.x;
    if (cloudiness <= 0.0)
        return 0.0;
    h /= weather.y * (0.25 + 0.75 * sqrt(cloudiness)) * mix(0.55, 1.0, smoothstep(0.3, 0.7, cloud.y));
    if (h >= 1.0)
        return 0.0;
    profile = CloudProfile(h);
    return clamp(cloudiness - (1.0 - profile.x) * 0.5 - smoothstep(0.5, 1.0, h) * 0.7, 0.0, 1.0);
}

// The cloud's density at a point: the edges between cells of the 3D noise eat into the shape, most where the profile
// says. Large cells carve the main lobes of a cloud, small ones billow over them and the finest give the billows their
// own small puffs, bent, mixed between sizes and stronger in some places than others by slow noise, so the billows
// differ across a cloud. Thin cloud thins faster
// still, so edges stay soft. Times the profile's density through the layer
float CloudDensity(vec3 q, float shape, vec3 profile, float lod)
{
    vec3 uvw = q * _CloudPattern.z;
    vec2 slow = textureLod(_CloudNoise3D, uvw * 0.17 + vec3(0.31, 0.17, 0.53), lod).ba;
    uvw += (slow.x - 0.5) * 0.35;
    float base = textureLod(_CloudNoise3D, uvw, lod).r;
    float coarse = textureLod(_CloudNoise3D, uvw * 0.55 + 0.5, lod).r;
    float finer = textureLod(_CloudNoise3D, mat3(0.8, 0.0, -0.6, 0.0, 1.0, 0.0, 0.6, 0.0, 0.8) * uvw * 1.8 + 0.3, lod + 0.85).r;
    float edges = mix(mix(base, coarse, slow.y), finer, smoothstep(0.45, 0.75, slow.x) * (1.0 - slow.y));
    float large = textureLod(_CloudNoise3D, uvw * 0.3 + 0.7, max(lod - 1.7, 0.0)).r;
    float lobes = large * large * mix(0.9, 1.6, slow.y);
    float billows = edges * edges * mix(0.4, 1.7, smoothstep(0.2, 0.8, slow.x));
    float puffs = textureLod(_CloudNoise3D, mat3(0.6, 0.8, 0.0, -0.8, 0.6, 0.0, 0.0, 0.0, 1.0) * uvw * 2.1 + 0.11, lod + 1.07).g;
    puffs = puffs * puffs * mix(0.25, 0.8, slow.y);
    float density = clamp(shape - (lobes + billows + puffs) * _CloudPattern.w * profile.z, 0.0, 1.0);
    return density * smoothstep(0.0, 0.15, density) * profile.y;
}

// How much cloud lies between q and the sun, density times meters, at a blur of lod. Steps grow outward and reach a
// little past the layer's thickness
float CloudMarchToSun(vec3 q, vec3 toSun, int samples, float lod)
{
    const float growth = 1.3;
    float reach = _CloudShape.y * 1.1;
    float stride = reach * (growth - 1.0) / (pow(growth, float(samples)) - 1.0);
    float travelled = 0.0, optical = 0.0;
    for (int i = 0; i < samples; i++)
    {
        vec3 p = q + toSun * (travelled + stride * 0.5);
        vec3 profile;
        float s = CloudShape(p, profile);
        if (s > 0.0)
            optical += CloudDensity(p, s, profile, lod) * stride;
        travelled += stride;
        stride *= growth;
        if (p.y > _CloudShape.x + _CloudShape.y)
            break;
    }
    return optical;
}

// The sun map holds the march toward the sun from four heights, one per channel, blended between the two around q
float CloudSunDepthAt(vec3 q)
{
    if (q.y >= _CloudShape.x + _CloudShape.y)
        return 0.0;
    vec4 depths = textureLod(_CloudSunDepth, CloudMapUV(q.xz), 0.0);
    float slot = clamp((q.y - _CloudShape.x) * _CloudShape.z * 4.0 - 0.5, 0.0, 3.0);
    vec4 weights = max(1.0 - abs(vec4(slot) - vec4(0.0, 1.0, 2.0, 3.0)), 0.0);
    return dot(depths, weights);
}

// Distance from the camera to the scene under a pixel of the full resolution depth, far for the sky
float CloudSceneDistance(Graphite_ViewData view, GRAPHITE_SCREEN_TEXTURE depthTexture, ivec2 pixel, vec2 resolution)
{
    float depth = Graphite_Fetch(depthTexture, pixel, 0).r;
    if (depth <= 1e-7)
        return 1e8;
    return length(Graphite_RelativeFromDepth(view, (vec2(pixel) + 0.5) / resolution, depth));
}

#if defined(CLOUD_MAPS)

layout(location = 1) out vec4 outSun;

uniform sampler2D _CloudMapsPrevious;
uniform sampler2D _CloudMapsPreviousSun;
uniform vec4 _CloudMapWindow;   // the first lattice texel of the square now, then last frame's
uniform vec4 _CloudMapRefresh;  // the slice of rows drawn again this frame, the slice count, 1 when last frame's maps hold, texel size
uniform float _CloudMapSize;    // texels along each side, a power of two
uniform float _CloudSunMapLod;

void main()
{
    int size = int(_CloudMapSize);
    ivec2 texel = ivec2(gl_FragCoord.xy);
    ivec2 first = ivec2(_CloudMapWindow.xy), previous = ivec2(_CloudMapWindow.zw);
    ivec2 lattice = first + ((texel - first) & (size - 1));

    int slices = int(_CloudMapRefresh.y);
    bool held = _CloudMapRefresh.z > 0.5 && all(greaterThanEqual(lattice, previous)) && all(lessThan(lattice, previous + size));
    if (held && (lattice.y & (slices - 1)) != int(_CloudMapRefresh.x))
    {
        outColor = texelFetch(_CloudMapsPrevious, texel, 0);
        outSun = texelFetch(_CloudMapsPreviousSun, texel, 0);
        return;
    }

    vec2 windRel = (vec2(lattice) + 0.5) * _CloudMapRefresh.w;
    outColor = vec4(CloudWeatherNoise(windRel), 0.0, 1.0);
    for (int i = 0; i < 4; i++)
    {
        float height = _CloudShape.x + (float(i) + 0.5) * 0.25 * _CloudShape.y;
        outSun[i] = CloudMarchToSun(vec3(windRel.x, height, windRel.y), _CloudToSun, 8, _CloudSunMapLod);
    }

    // A row drawn again eases toward its new light, so the slices it is refreshed in never show
    if (held)
        outSun = mix(texelFetch(_CloudMapsPreviousSun, texel, 0), outSun, 0.35);
}

#elif defined(CLOUD_DEPTH)

uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
uniform vec2 _CloudFullResolution;
uniform vec2 _CloudLowResolution;
uniform int _CloudDownsample;
uniform int _CloudDepthMode;     // 1 takes the one scene pixel under each jittered cloud sample
uniform vec2 _CloudJitter;       // this frame's sample offset, in pixels of the clouds' target

// Neighbouring pixels alternate between the nearest and furthest of what they cover, so both sides of an edge survive
// for the depth aware upsample
void main()
{
    Graphite_ViewData view = Graphite_PostView();
    ivec2 last = ivec2(_CloudFullResolution) - 1;
    if (_CloudDepthMode == 1)
    {
        vec2 at = (gl_FragCoord.xy + _CloudJitter) / _CloudLowResolution * _CloudFullResolution;
        outColor = vec4(CloudSceneDistance(view, _CameraDepthTexture, min(ivec2(at), last), _CloudFullResolution), 0.0, 0.0, 1.0);
        return;
    }

    ivec2 low = ivec2(gl_FragCoord.xy);
    float spread = float(max(_CloudDownsample - 1, 0));
    vec2 base = gl_FragCoord.xy / _CloudLowResolution * _CloudFullResolution - spread * 0.5;
    bool takeFar = ((low.x + low.y) & 1) == 0;
    float pick = takeFar ? 0.0 : 1e9;
    for (int i = 0; i < 4; i++)
    {
        vec2 offset = vec2(i & 1, i >> 1) * spread;
        float d = CloudSceneDistance(view, _CameraDepthTexture, clamp(ivec2(base + offset), ivec2(0), last), _CloudFullResolution);
        pick = takeFar ? max(pick, d) : min(pick, d);
    }
    outColor = vec4(pick, 0.0, 0.0, 1.0);
}

#elif defined(CLOUD_MARCH)

#define CLOUD_MAX_LAYERS 4

layout(location = 1) out vec4 outDistance;

uniform GRAPHITE_SCREEN_TEXTURE _CloudSceneDepth;
uniform vec2 _CloudLowResolution;
uniform vec2 _CloudJitter;          // this frame's sample offset, in pixels of the clouds' target
uniform vec3 _CloudCamera;          // the first layer's camera, absolute
uniform vec4 _CloudLighting;        // extinction per meter, unused, ambient, silver lining
uniform vec3 _CloudSunColor;        // colour times intensity times sun brightness
uniform vec3 _CloudSkyLight;
uniform vec4 _CloudAlbedo;
uniform float _CloudAmbientSaturation;
uniform float _CloudHorizonFade;

vec3 CloudSun()
{
    return _CloudSunColor * smoothstep(-0.1, 0.05, _CloudToSun.y);
}

// Each flat layer: altitude, coverage, opacity and 1 / scale, its wind offset and the wind's direction, its tint and style
uniform int _CloudLayerCount;
uniform vec4 _LayerShape_0, _LayerShape_1, _LayerShape_2, _LayerShape_3;
uniform vec4 _LayerWind_0, _LayerWind_1, _LayerWind_2, _LayerWind_3;
uniform vec4 _LayerTint_0, _LayerTint_1, _LayerTint_2, _LayerTint_3;
uniform sampler2D _LayerTexture_0;
uniform sampler2D _LayerTexture_1;
uniform sampler2D _LayerTexture_2;
uniform sampler2D _LayerTexture_3;

void CloudLayerAt(int i, out vec4 shape, out vec4 wind, out vec4 tint)
{
    if (i == 0) { shape = _LayerShape_0; wind = _LayerWind_0; tint = _LayerTint_0; }
    else if (i == 1) { shape = _LayerShape_1; wind = _LayerWind_1; tint = _LayerTint_1; }
    else if (i == 2) { shape = _LayerShape_2; wind = _LayerWind_2; tint = _LayerTint_2; }
    else { shape = _LayerShape_3; wind = _LayerWind_3; tint = _LayerTint_3; }
}

float CloudLayerTexture(int i, vec2 q)
{
    if (i == 0) return texture(_LayerTexture_0, q).r;
    if (i == 1) return texture(_LayerTexture_1, q).r;
    if (i == 2) return texture(_LayerTexture_2, q).r;
    return texture(_LayerTexture_3, q).r;
}

// Wispy patches: broad noise decides where cirrus is, and fibres drawn out along the wind give it texture inside
float CloudCirrus(vec2 q, vec4 wind, float coverage)
{
    vec2 along = wind.zw;
    vec2 s = vec2(dot(q, along), dot(q, vec2(-along.y, along.x)));
    s.y += (texture(_CloudNoise2D, q * 0.08).r - 0.5) * 0.8;

    float fibres = 0.0, amplitude = 0.5, total = 0.0;
    vec2 frequency = vec2(0.45, 1.5);
    for (int o = 0; o < 3; o++)
    {
        fibres += texture(_CloudNoise2D, s * frequency + vec2(0.37, 0.71) * float(o)).b * amplitude;
        total += amplitude;
        amplitude *= 0.55;
        frequency *= 2.0;
    }
    fibres = clamp((fibres / total - 0.42) * 2.5, 0.0, 1.0);

    float threshold = 1.0 - coverage;
    return smoothstep(threshold - 0.1, threshold + 0.25, texture(_CloudNoise2D, q * 0.3).r) * fibres;
}

// Soft puffs drawn out along the wind with fine streaks running through them
float CloudStratus(vec2 q, vec4 wind, float coverage)
{
    vec2 along = wind.zw;
    vec2 s = vec2(dot(q, along), dot(q, vec2(-along.y, along.x)));
    vec2 bend = vec2(texture(_CloudNoise2D, s * 0.05).r, texture(_CloudNoise2D, s * 0.05 + vec2(0.31, 0.67)).r) - 0.5;
    s += bend * vec2(0.6, 1.2);

    float threshold = 1.0 - coverage;
    float lumps = texture(_CloudNoise2D, s * vec2(0.7, 5.0)).r * 0.55 + texture(_CloudNoise2D, s * vec2(1.1, 7.0) + vec2(0.5, 0.25)).g * 0.45;
    float puffs = smoothstep(threshold - 0.12, threshold + 0.3, lumps);
    float streaks = texture(_CloudNoise2D, s * vec2(0.14, 2.8)).b * 0.6 + texture(_CloudNoise2D, s * vec2(0.28, 6.3) + vec2(0.13, 0.57)).b * 0.4;
    return clamp(puffs * mix(0.0, 1.6, streaks), 0.0, 1.0);
}

uniform float _CloudSunTap;         // how far toward the sun one sample shades from, 0 for the sun map past a short detailed march
uniform float _CloudReach;          // how far out the clouds reach around the camera

vec4 CloudLayerSample(int i, vec3 cam, vec3 dir, float sceneDist, vec3 sun, out float t)
{
    vec4 shape, wind, tint;
    CloudLayerAt(i, shape, wind, tint);
    t = CloudShellDistance(dir, cam.y, shape.x);
    if (t <= 0.0 || t > sceneDist)
        return vec4(0.0);
    vec3 p = cam + dir * t;
    vec2 q = (p.xz - wind.xy) * shape.w;
    int style = int(tint.w);
    float density = style == 2 ? smoothstep(-shape.y + 0.95, 1.25 - shape.y, CloudLayerTexture(i, q))
        : style == 0 ? CloudCirrus(q, wind, shape.y) : CloudStratus(q, wind, shape.y);
    float alpha = density * shape.z;
    if (alpha < 0.002)
        return vec4(0.0);
    float phase = mix(1.0 / (4.0 * 3.14159265), CloudHenyeyGreenstein(dot(dir, _CloudToSun), 0.75), _CloudLighting.w * 0.5);
    return vec4(_CloudAlbedo.rgb * tint.rgb * (sun * phase * (1.0 - 0.4 * density) + _CloudSkyLight * _CloudLighting.z), alpha);
}

// How much cloud the sunlight crosses to reach q, whose own density is given: where the view still sees through, a
// short march with all the detail through the cloud right around it then the sun map from where it ends, fading to the
// sun map alone deeper in. With the sun tap, one sample a short way toward the sun instead: denser there means q is
// on the shadowed side of its billow, and how fast it grows denser says how much cloud the light crosses
float CloudLightDepth(vec3 q, float density, float lod, float front)
{
    if (_CloudSunTap > 0.0)
    {
        const float reach = 100.0;
        vec3 p = q + _CloudToSun * _CloudSunTap;
        vec3 profile;
        float s = CloudShape(p, profile);
        float toward = s > 0.0 ? CloudDensity(p, s, profile, lod) : 0.0;
        return max(density + (toward - density) / _CloudSunTap * reach * 0.5, 0.0) * reach;
    }
    float coarse = CloudSunDepthAt(q);
    if (front <= 0.0)
        return coarse;
    float near = _CloudShape.y * 0.25;
    float optical = 0.0;
    for (int i = 0; i < 3; i++)
    {
        vec3 p = q + _CloudToSun * (near * (float(i) + 0.5) / 3.0);
        vec3 profile;
        float s = CloudShape(p, profile);
        if (s > 0.0)
            optical += CloudDensity(p, s, profile, lod + 1.0) * near / 3.0;
    }
    return mix(coarse, optical + CloudSunDepthAt(q + _CloudToSun * near), front);
}

void main()
{
    Graphite_ViewData view = Graphite_PostView();
    outColor = vec4(0.0);
    outDistance = vec4(0.0);
    if (Graphite_PostOrthographic(view))
        return;

    vec2 uv = (gl_FragCoord.xy + _CloudJitter) / _CloudLowResolution;
    vec3 dir = normalize(Graphite_RelativeFromDepth(view, uv, 1.0));
    vec3 cam = _CloudCamera + view.treeOffset.xyz;
    float sceneDist = Graphite_Fetch(_CloudSceneDepth, ivec2(gl_FragCoord.xy), 0).r;
    vec3 sun = CloudSun();

    float layerT[CLOUD_MAX_LAYERS];
    vec4 layerColor[CLOUD_MAX_LAYERS];
    int layers = 0;
    for (int i = 0; i < min(_CloudLayerCount, CLOUD_MAX_LAYERS); i++)
    {
        float t;
        vec4 c = CloudLayerSample(i, cam, dir, sceneDist, sun, t);
        if (c.a <= 0.0)
            continue;
        int at = layers++;
        for (; at > 0 && layerT[at - 1] > t; at--)
        {
            layerT[at] = layerT[at - 1];
            layerColor[at] = layerColor[at - 1];
        }
        layerT[at] = t;
        layerColor[at] = c;
    }

    float top = _CloudShape.x + _CloudShape.y;
    vec2 outer = CloudShellRoots(dir, cam.y, top);
    vec2 inner = CloudShellRoots(dir, cam.y, _CloudShape.x);
    float start = max(outer.x, 0.0), end = outer.y;
    if (inner.x < inner.y)
    {
        if (inner.x > start)
            end = min(end, inner.x);
        else if (inner.y > start)
            start = inner.y;
    }
    float horizontal = max(length(dir.xz), 1e-3);
    end = min(min(end, sceneDist), _CloudReach / horizontal);

    vec3 ambientSky = mix(vec3(dot(_CloudSkyLight, vec3(0.2126, 0.7152, 0.0722))), _CloudSkyLight, _CloudAmbientSaturation) * _CloudLighting.z;
    float planet = _CloudShape.w;
    float cosTheta = dot(dir, _CloudToSun);
    vec3 phases = CloudPhases(cosTheta, _CloudLighting.w);
    float pixelAngle = 2.0 / (max(abs(view.projection[1][1]), 1e-4) * _CloudLowResolution.y);
    float powderFacing = cosTheta * 0.5 + 0.5;
    float extinction = _CloudLighting.x * 40.0;

    vec3 color = vec3(0.0);
    float transmittance = 1.0, distSum = 0.0, weightSum = 0.0;
    int nextLayer = 0;

    if (end > start)
    {
        // Steps grow with distance, as a pixel covers more of the cloud further away. Through empty sky the march takes
        // long strides, and steps back to fine ones when it lands in cloud
        float nearStride = _CloudShape.y / 40.0;
        float stride = max(nearStride, start * 0.004);
        float jitter = fract(52.9829189 * fract(dot(gl_FragCoord.xy + mod(view.time.z, 64.0) * 5.588238, vec2(0.06711056, 0.00583715))));
        float t = start + stride * jitter;
        bool fine = false;
        int empty = 0;
        for (int i = 0; i < 256 && t < end && transmittance > 0.01; i++)
        {
            stride = max(nearStride, t * 0.004);
            for (; nextLayer < layers && layerT[nextLayer] <= t; nextLayer++)
            {
                float w = transmittance * layerColor[nextLayer].a;
                color += layerColor[nextLayer].rgb * w;
                distSum += layerT[nextLayer] * w;
                weightSum += w;
                transmittance *= 1.0 - layerColor[nextLayer].a;
            }

            vec3 p = cam + dir * t;
            vec2 ground = p.xz - cam.xz;
            float g2 = dot(ground, ground);
            float radius = sqrt((p.y + planet) * (p.y + planet) + g2);
            vec3 q = vec3(p.x - _CloudWind.x, (p.y * (p.y + 2.0 * planet) + g2) / (radius + planet), p.z - _CloudWind.y);

            vec3 profile;
            float shape = CloudShape(q, profile);
            if (shape <= 0.0)
            {
                if (fine && ++empty < 4)
                {
                    t += stride;
                    continue;
                }
                fine = false;
                t += stride * 4.0;
                continue;
            }
            if (!fine)
            {
                fine = true;
                empty = 0;
                t = max(t - stride * 3.0, start + stride * jitter);
                continue;
            }
            empty = 0;

            float lod = max(log2(t * pixelAngle * _CloudPattern.z * _CloudErosionSize * 2.0), 0.0);
            // Detailed light only shows where the view still sees through the cloud
            float front = smoothstep(0.1, 0.4, transmittance);
            float density = CloudDensity(q, shape, profile, lod);
            if (density < 1e-4)
            {
                t += stride;
                continue;
            }

            vec2 mapUV = (p.xz - _CloudCoverageRect.xy) * _CloudCoverageRect.z;
            vec2 edge = min(mapUV, 1.0 - mapUV);
            float fade = smoothstep(0.0, 0.02, min(edge.x, edge.y)) * (1.0 - smoothstep(_CloudReach * 0.5, _CloudReach, sqrt(g2)));
            fade *= clamp((sceneDist - t) / 50.0, 0.0, 1.0);
            float sigma = density * fade * extinction;
            if (sigma <= 0.0)
            {
                t += stride;
                continue;
            }

            float h = (q.y - _CloudShape.x) * _CloudShape.z;
            float transmitted = CloudScattering(CloudLightDepth(q, density, lod, front) * extinction, phases);
            float powder = mix(1.0 - exp(-sigma * 300.0), 1.0, powderFacing);
            vec3 ambient = ambientSky * mix(0.55, 1.0, sqrt(h));
            vec3 lit = _CloudAlbedo.rgb * (sun * transmitted * powder + ambient);

            // In dense cloud a whole step would be opaque and show as a slice, so the step shortens to about two
            // optical depths. The march ends soon after anyway, once the cloud has hidden what is behind it
            float step = clamp(2.0 / sigma, nearStride * 0.25, stride);
            float stepTransmittance = exp(-sigma * step);
            float w = transmittance * (1.0 - stepTransmittance);
            color += lit * w;
            distSum += t * w;
            weightSum += w;
            transmittance *= stepTransmittance;
            t += step;
        }
    }

    for (; nextLayer < layers; nextLayer++)
    {
        float w = transmittance * layerColor[nextLayer].a;
        color += layerColor[nextLayer].rgb * w;
        distSum += layerT[nextLayer] * w;
        weightSum += w;
        transmittance *= 1.0 - layerColor[nextLayer].a;
    }

    // Far clouds fade into the sky behind them as a whole, by how far the cloud the pixel shows is, so their shapes
    // stay whole as they go rather than thinning out
    float distance = weightSum > 1e-6 ? distSum / weightSum : 0.0;
    float far = distance * _CloudHorizonFade;
    float alpha = (1.0 - transmittance) * exp(-far * far);
    color *= exp(-far * far);
    outColor = vec4(color, alpha);
    outDistance = vec4(distance * 0.001 * alpha, 0.0, 0.0, 1.0);
}

#elif defined(CLOUD_TEMPORAL)

layout(location = 1) out vec4 outDistance;

uniform GRAPHITE_SCREEN_TEXTURE _CloudCurrent;
uniform GRAPHITE_SCREEN_TEXTURE _CloudCurrentDistance;     // kilometers times coverage
uniform GRAPHITE_SCREEN_TEXTURE _CloudTargetDepth;         // scene distance at this pass's own resolution
uniform GRAPHITE_SCREEN_TEXTURE _CloudPreviousTargetDepth; // the same, last frame
uniform GRAPHITE_SCREEN_TEXTURE _CloudLowDepth;            // scene distance under each of this frame's samples
uniform GRAPHITE_SCREEN_TEXTURE _CloudHistory;
uniform GRAPHITE_SCREEN_TEXTURE _CloudHistoryDistance;
uniform vec2 _CloudLowResolution;
uniform vec2 _CloudHistoryResolution;
uniform vec2 _CloudJitterCell;   // which pixel of each block this frame's samples land on
uniform int _CloudBlock;         // pixels along each side of a block
uniform float _CloudHistoryValid;
uniform vec2 _CloudWindStep;     // how far the wind carried the clouds since the history was drawn

// This frame's samples interpolated to any pixel, each counted by how well the scene behind it matches the pixel's
void CurrentAt(vec2 pixelCentre, ivec2 last, float sceneDistance, out vec4 color, out float dist)
{
    vec2 at = (pixelCentre - _CloudJitterCell - 0.5) / float(_CloudBlock);
    ivec2 first = ivec2(floor(at));
    vec2 f = at - vec2(first);
    color = vec4(0.0);
    dist = 0.0;
    float total = 0.0;
    for (int i = 0; i < 4; i++)
    {
        ivec2 offset = ivec2(i & 1, i >> 1);
        ivec2 tap = clamp(first + offset, ivec2(0), last);
        float bilinear = (offset.x == 1 ? f.x : 1.0 - f.x) * (offset.y == 1 ? f.y : 1.0 - f.y);
        float difference = abs(Graphite_Fetch(_CloudLowDepth, tap, 0).r - sceneDistance) / max(sceneDistance, 1.0);
        float weight = (bilinear + 1e-3) / (difference + 1e-3);
        color += Graphite_Fetch(_CloudCurrent, tap, 0) * weight;
        dist += Graphite_Fetch(_CloudCurrentDistance, tap, 0).r * weight;
        total += weight;
    }
    color /= total;
    dist /= total;
}

// History read with a sharpening cubic from five bilinear reads, so reprojecting it every frame does not blur it
vec4 HistoryAt(vec2 uv)
{
    vec2 position = uv * _CloudHistoryResolution;
    vec2 centre = floor(position - 0.5) + 0.5;
    vec2 f = position - centre;
    vec2 w0 = f * (-0.5 + f * (1.0 - 0.5 * f));
    vec2 w1 = 1.0 + f * f * (-2.5 + 1.5 * f);
    vec2 w2 = f * (0.5 + f * (2.0 - 1.5 * f));
    vec2 w3 = f * f * (-0.5 + 0.5 * f);
    vec2 w12 = w1 + w2;
    vec2 middle = (centre + w2 / w12) / _CloudHistoryResolution;
    vec2 before = (centre - 1.0) / _CloudHistoryResolution;
    vec2 after = (centre + 2.0) / _CloudHistoryResolution;

    vec4 sum = Graphite_Sample(_CloudHistory, vec2(middle.x, before.y)) * (w12.x * w0.y)
             + Graphite_Sample(_CloudHistory, vec2(before.x, middle.y)) * (w0.x * w12.y)
             + Graphite_Sample(_CloudHistory, middle) * (w12.x * w12.y)
             + Graphite_Sample(_CloudHistory, vec2(after.x, middle.y)) * (w3.x * w12.y)
             + Graphite_Sample(_CloudHistory, vec2(middle.x, after.y)) * (w12.x * w3.y);
    float total = w12.x * w0.y + w0.x * w12.y + w12.x * w12.y + w3.x * w12.y + w12.x * w3.y;
    return max(sum / total, vec4(0.0));
}

// Nothing that is not a number survives into the history, where it would stay until the next reset
void Output(vec4 color, float dist)
{
    bool bad = any(isnan(color)) || any(isinf(color)) || isnan(dist) || isinf(dist);
    outColor = bad ? vec4(0.0) : max(color, vec4(0.0));
    outDistance = vec4(bad ? 0.0 : max(dist, 0.0), 0.0, 0.0, 1.0);
}

void main()
{
    Graphite_ViewData view = Graphite_PostView();
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    ivec2 last = ivec2(_CloudLowResolution) - 1;
    ivec2 historyLast = ivec2(_CloudHistoryResolution) - 1;
    ivec2 low = min(pixel / _CloudBlock, last);
    vec4 current = Graphite_Fetch(_CloudCurrent, low, 0);
    float currentDist = Graphite_Fetch(_CloudCurrentDistance, low, 0).r;
    float sceneDistance = Graphite_Fetch(_CloudTargetDepth, pixel, 0).r;
    vec4 smoothed;
    float smoothedDist;
    CurrentAt(gl_FragCoord.xy, last, sceneDistance, smoothed, smoothedDist);

    // The pixel this frame's samples landed on takes its own sample, the others this frame's samples around them
    bool fresh = all(equal(pixel - low * _CloudBlock, ivec2(_CloudJitterCell)));
    vec4 own = fresh ? current : smoothed;
    float ownDist = fresh ? currentDist : smoothedDist;
    if (_CloudHistoryValid < 0.5)
    {
        Output(own, ownDist);
        return;
    }

    // How far away this pixel's cloud is, from this frame's samples where they hold cloud, else from the history
    float guideAlpha = own.a, guideDist = ownDist;
    if (guideAlpha <= 0.01)
    {
        guideAlpha = Graphite_Fetch(_CloudHistory, pixel, 0).a;
        guideDist = Graphite_Fetch(_CloudHistoryDistance, pixel, 0).r;
    }
    bool cloudy = guideAlpha > 0.01;
    float distance = cloudy ? guideDist / guideAlpha * 1000.0 : 1e6;

    // Where this pixel's cloud was last frame, found from how far away it is and moved back against the wind
    vec2 uv = gl_FragCoord.xy / _CloudHistoryResolution;
    vec3 now = normalize(Graphite_RelativeFromDepth(view, uv, 1.0)) * distance;
    vec3 then = now;
    if (cloudy)
        then.xz -= _CloudWindStep;
    vec3 moved = then + (view.cameraHigh.xyz - view.previousCameraHigh.xyz) + (view.cameraLow.xyz - view.previousCameraLow.xyz);
    vec4 previous = view.previousViewProjection * vec4(moved, 1.0);
    vec2 previousUV = vec2(previous.x / previous.w * 0.5 + 0.5, 0.5 - previous.y / previous.w * 0.5);
    if (previous.w <= 0.0 || any(lessThan(previousUV, vec2(0.0))) || any(greaterThanEqual(previousUV, vec2(1.0))))
    {
        Output(own, ownDist);
        return;
    }

    // Where geometry hid this pixel's cloud last frame there is no history for it
    ivec2 previousPixel = clamp(ivec2(previousUV * _CloudHistoryResolution), ivec2(0), historyLast);
    if (Graphite_Fetch(_CloudPreviousTargetDepth, previousPixel, 0).r < distance - 50.0)
    {
        Output(own, ownDist);
        return;
    }

    vec4 history = HistoryAt(previousUV);
    float historyDist = Graphite_Sample(_CloudHistoryDistance, previousUV).r;

    // The guards against stale history only act on movement, so a still sky does not shimmer through the cycle
    float motion = clamp(length((previousUV - uv) * _CloudHistoryResolution) * 2.0, 0.0, 1.0);

    // Held within what the clouds around it look like this frame, centred on this frame's nearest sample
    ivec2 nearest = clamp(ivec2(floor((gl_FragCoord.xy - _CloudJitterCell - 0.5) / float(_CloudBlock) + 0.5)), ivec2(0), last);
    vec4 lowest = smoothed, highest = smoothed;
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            vec4 around = Graphite_Fetch(_CloudCurrent, clamp(nearest + ivec2(x, y), ivec2(0), last), 0);
            lowest = min(lowest, around);
            highest = max(highest, around);
        }
    }
    history = mix(history, clamp(history, lowest, highest), motion);

    // A sampled pixel takes a large share of its sample, the others a little of this frame's while moving
    float amount = fresh ? 0.5 : 0.1 * motion;
    Output(mix(history, own, amount), mix(historyDist, ownDist, amount));
}

#else

uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
uniform GRAPHITE_SCREEN_TEXTURE _CloudTex;
uniform GRAPHITE_SCREEN_TEXTURE _CloudTexDistance; // kilometers times coverage
uniform GRAPHITE_SCREEN_TEXTURE _CloudSceneDepth;
uniform vec2 _CloudLowResolution;

// Each pixel weights the nearby cloud pixels whose scene depth matches its own
void main()
{
    Graphite_ViewData view = Graphite_PostView();
    vec4 scene = Graphite_SampleSource(vUV);
    float depth = Graphite_Sample(_CameraDepthTexture, vUV).r;
    float own = depth <= 1e-7 ? 1e8 : length(Graphite_RelativeFromDepth(view, vUV, depth));

    vec2 position = vUV * _CloudLowResolution - 0.5;
    ivec2 first = ivec2(floor(position));
    vec2 f = position - vec2(first);
    ivec2 last = ivec2(_CloudLowResolution) - 1;

    vec4 sum = vec4(0.0);
    float sumDistance = 0.0, total = 0.0;
    for (int i = 0; i < 4; i++)
    {
        ivec2 offset = ivec2(i & 1, i >> 1);
        ivec2 pixel = clamp(first + offset, ivec2(0), last);
        float bilinear = (offset.x == 1 ? f.x : 1.0 - f.x) * (offset.y == 1 ? f.y : 1.0 - f.y);
        float difference = abs(Graphite_Fetch(_CloudSceneDepth, pixel, 0).r - own) / max(own, 1.0);
        float weight = (bilinear + 1e-3) / (difference + 1e-3);
        sum += Graphite_Fetch(_CloudTex, pixel, 0) * weight;
        sumDistance += Graphite_Fetch(_CloudTexDistance, pixel, 0).r * weight;
        total += weight;
    }

    // Wherever geometry is nearer than the cloud, the cloud fades out over 50 meters
    vec4 color = sum / total;
    float cloud = sumDistance / total / max(color.a, 1e-4) * 1000.0;
    float visible = color.a > 1e-3 ? clamp((own - cloud) / 50.0, 0.0, 1.0) : 1.0;
    color *= visible;
    outColor = vec4(scene.rgb * (1.0 - color.a) + color.rgb * view.exposure.x, scene.a);
}

#endif

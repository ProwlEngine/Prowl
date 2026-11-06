#pragma variant CLOUD COMPOSITE MAPS DEPTH TILES SHADOW MARCH TEMPORAL
// Volumetric clouds as fullscreen passes, one per define:
// CLOUD_MAPS       how cloudy each point of the ground plane is over a square around the camera. Kept from frame to
//                  frame, only the texels the square newly covers are drawn
// CLOUD_DEPTH      scene distance at the clouds' resolution
// CLOUD_TILES      how far along the centre ray of each block of pixels the first and last clouds lie, what the
//                  march covers
// CLOUD_MARCH      the cloud layer and the flat layers marched front to back at the clouds' resolution
// CLOUD_SHADOW     one cascade of the clouds' shadow map: along each texel's ray from the sun, where it first meets
//                  cloud, the cloud's mean density from there on and how much cloud the ray crosses in all
// CLOUD_TEMPORAL   the march blended over last frame's, moved to where the clouds are now, for a march in long steps
// otherwise        the clouds scaled up over the colour, matched to the scene's depth
// Cloud positions are absolute, the camera's from the host. Each ray's planet centre sits straight under the camera.
// The map rides the wind: a texel holds one point of the pattern for as long as it is in the square, wrapped around the
// map by its place on the wind's lattice, so the map is read with repeat at the position relative to the wind.

#include "Graphite/Internal/PostView.glsl"

uniform sampler2D _CloudCoverage;
uniform sampler2D _CloudNoise2D;
uniform sampler3D _CloudNoise3D;
uniform sampler2D _CloudProfile;
uniform vec4 _CloudCoverageRect; // min x, min z, 1 / span, span
uniform vec4 _CloudShape;        // base altitude, thickness, 1 / thickness, planet radius
uniform vec4 _CloudPattern;      // 1 / pattern scale, coverage, 1 / detail scale, erosion
uniform vec2 _CloudWind;
uniform vec3 _CloudToSun;        // toward the sun, straight up when the scene has none
uniform float _CloudErosionSize; // texels along each side of the 3D noise
uniform float _CloudFormation;   // how far the formations have changed, along the 3D noise's third axis
uniform float _CloudReach;       // how far out the clouds reach around the camera
uniform vec2 _CloudWarp;         // how far smooth noise bends the outlines, how much the bend changes through the layer
uniform float _CloudErosionWeight; // how much erosion leans toward a cloud's base, 1 leaving its very top smooth
uniform float _CloudTileSize;    // pixels along each side of a block the march's range is found for

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
// thickens the sky. Read live as the noise moves through time, so clouds change shape smoothly. The bend drifts with
// height h through the layer, so a cloud twists from its base to its top. Also gives the shape noise alone, which lifts
// and lowers the tops even where the sky is fully covered
vec2 CloudCloudiness(vec2 windRel, float broad, float h)
{
    vec2 q = windRel * _CloudPattern.x;
    float z = _CloudFormation;
    float drift = h * _CloudWarp.y;
    vec2 bend = vec2(textureLod(_CloudNoise3D, vec3(q * 0.7, z + 0.13 + drift), 0.0).b,
                     textureLod(_CloudNoise3D, vec3(q * 0.7 + 0.31, z + 0.61 + drift), 0.0).b) - 0.5;
    vec2 p = q * 1.3 + bend * _CloudWarp.x;
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
// a covered sky still has an uneven top. The shape thins over the top of each cloud, so erosion carves billows there,
// less toward the top as the erosion weight leans it to the base.
// The point is relative to the wind across, absolute in height. Also gives how high through the layer the cloud over
// q reaches, 0 where there is none, so a march can tell when a long stride would step over a thin cloud below it
float CloudShape(vec3 q, out vec3 profile, out float top)
{
    float h = (q.y - _CloudShape.x) * _CloudShape.z;
    profile = vec3(0.0);
    top = 0.0;
    if (h <= 0.0 || h >= 1.0)
        return 0.0;
    vec2 weather = CloudWeatherAt(q.xz);
    vec2 cloud = CloudCloudiness(q.xz, weather.x, h);
    float cloudiness = cloud.x;
    if (cloudiness <= 0.0)
        return 0.0;
    top = weather.y * (0.25 + 0.75 * sqrt(cloudiness)) * mix(0.55, 1.0, smoothstep(0.3, 0.7, cloud.y));
    h /= top;
    if (h >= 1.0)
        return 0.0;
    profile = CloudProfile(h);
    profile.z *= mix(1.0, 1.0 - h, _CloudErosionWeight);
    return clamp(cloudiness - (1.0 - profile.x) * 0.5 - smoothstep(0.5, 1.0, h) * 0.7, 0.0, 1.0);
}

float CloudShape(vec3 q, out vec3 profile)
{
    float top;
    return CloudShape(q, profile, top);
}

// Whether a stride from q that drops by fall through the layer ends below the top of the cloud over q, where thin
// cloud can lie anywhere down to its base
bool CloudUnderTop(vec3 q, float top, float fall)
{
    float h = (q.y - _CloudShape.x) * _CloudShape.z;
    return top > 0.0 && h - fall < top;
}

// The cloud's density at a point: the edges between cells of the 3D noise eat into the shape, most where the profile
// says. Large cells carve the main lobes of a cloud, small ones billow over them and the finest give the billows their
// own small puffs, bent, mixed between sizes and stronger in some places than others by slow noise, so the billows
// differ across a cloud. Thin cloud thins faster still, so edges stay soft. Times the profile's density through the
// layer. Every size only takes away, so once one has eaten the cloud the finer ones are not read. For light only the
// main lobes are read, the billows and puffs taken at their average
float CloudDensity(vec3 q, float shape, vec3 profile, float lod, bool forLight)
{
    vec3 uvw = q * _CloudPattern.z;
    vec2 slow = textureLod(_CloudNoise3D, uvw * 0.17 + vec3(0.31, 0.17, 0.53), lod).ba;
    uvw += (slow.x - 0.5) * 0.35;
    float bite = _CloudPattern.w * profile.z;

    float large = textureLod(_CloudNoise3D, uvw * 0.3 + 0.7, max(lod - 1.7, 0.0)).r;
    float density = shape - large * large * mix(0.9, 1.6, slow.y) * bite;
    if (density <= 0.0)
        return 0.0;

    float billows = 0.3194;
    if (!forLight)
    {
        float base = textureLod(_CloudNoise3D, uvw, lod).r;
        float coarse = textureLod(_CloudNoise3D, uvw * 0.55 + 0.5, lod).r;
        float finer = textureLod(_CloudNoise3D, mat3(0.8, 0.0, -0.6, 0.0, 1.0, 0.0, 0.6, 0.0, 0.8) * uvw * 1.8 + 0.3, lod + 0.85).r;
        float edges = mix(mix(base, coarse, slow.y), finer, smoothstep(0.45, 0.75, slow.x) * (1.0 - slow.y));
        billows = edges * edges;
    }
    density -= billows * mix(0.4, 1.7, smoothstep(0.2, 0.8, slow.x)) * bite;
    if (density <= 0.0)
        return 0.0;

    float puffs = forLight ? 0.3181 : pow(textureLod(_CloudNoise3D, mat3(0.6, 0.8, 0.0, -0.8, 0.6, 0.0, 0.0, 0.0, 1.0) * uvw * 2.1 + 0.11, lod + 1.07).g, 2.0);
    density = clamp(density - puffs * mix(0.25, 0.8, slow.y) * bite, 0.0, 1.0);
    return density * smoothstep(0.0, 0.15, density) * profile.y;
}

// Distance from the camera to the scene under a pixel of the full resolution depth, far for the sky
float CloudSceneDistance(Graphite_ViewData view, GRAPHITE_SCREEN_TEXTURE depthTexture, ivec2 pixel, vec2 resolution)
{
    float depth = Graphite_Fetch(depthTexture, pixel, 0).r;
    if (depth <= 1e-7)
        return 1e8;
    return length(Graphite_RelativeFromDepth(view, (vec2(pixel) + 0.5) / resolution, depth));
}

// Where a ray from the camera enters and leaves the cloud layer, up to the clouds' reach
vec2 CloudLayerRange(vec3 dir, float cameraHeight)
{
    vec2 outer = CloudShellRoots(dir, cameraHeight, _CloudShape.x + _CloudShape.y);
    vec2 inner = CloudShellRoots(dir, cameraHeight, _CloudShape.x);
    float start = max(outer.x, 0.0), end = outer.y;
    if (inner.x < inner.y)
    {
        if (inner.x > start)
            end = min(end, inner.x);
        else if (inner.y > start)
            start = inner.y;
    }
    return vec2(start, min(end, _CloudReach / max(length(dir.xz), 1e-3)));
}

// A point along a ray relative to the wind across, its height above the curved ground, and its squared distance from
// the camera across the ground
vec3 CloudWindPoint(vec3 p, vec3 cam, out float ground2)
{
    float planet = _CloudShape.w;
    vec2 ground = p.xz - cam.xz;
    ground2 = dot(ground, ground);
    float radius = sqrt((p.y + planet) * (p.y + planet) + ground2);
    return vec3(p.x - _CloudWind.x, (p.y * (p.y + 2.0 * planet) + ground2) / (radius + planet), p.z - _CloudWind.y);
}

#if defined(CLOUD_MAPS)

uniform sampler2D _CloudMapsPrevious;
uniform vec4 _CloudMapWindow;   // the first lattice texel of the square now, then last frame's
uniform vec2 _CloudMapState;    // 1 when last frame's map holds, texel size
uniform float _CloudMapSize;    // texels along each side, a power of two

void main()
{
    int size = int(_CloudMapSize);
    ivec2 texel = ivec2(gl_FragCoord.xy);
    ivec2 first = ivec2(_CloudMapWindow.xy), previous = ivec2(_CloudMapWindow.zw);
    ivec2 lattice = first + ((texel - first) & (size - 1));
    if (_CloudMapState.x > 0.5 && all(greaterThanEqual(lattice, previous)) && all(lessThan(lattice, previous + size)))
    {
        outColor = texelFetch(_CloudMapsPrevious, texel, 0);
        return;
    }
    outColor = vec4(CloudWeatherNoise((vec2(lattice) + 0.5) * _CloudMapState.y), 0.0, 1.0);
}

#elif defined(CLOUD_DEPTH)

uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
uniform vec2 _CloudFullResolution;
uniform vec2 _CloudLowResolution;
uniform int _CloudDownsample;

// Neighbouring pixels alternate between the nearest and furthest of what they cover, so both sides of an edge survive
// for the depth aware upsample
void main()
{
    Graphite_ViewData view = Graphite_PostView();
    ivec2 last = ivec2(_CloudFullResolution) - 1;
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

#elif defined(CLOUD_TILES)

uniform vec2 _CloudLowResolution;
uniform vec3 _CloudCamera;

// Steps half the march's stride through empty sky along the block's centre ray and keeps two steps short of the first
// cloud and two steps past the last, an empty range when the ray meets none
void main()
{
    Graphite_ViewData view = Graphite_PostView();
    outColor = vec4(1e30, -1e30, 0.0, 1.0);
    vec2 uv = gl_FragCoord.xy * _CloudTileSize / _CloudLowResolution;
    vec3 dir = normalize(Graphite_RelativeFromDepth(view, uv, 1.0));
    vec3 cam = _CloudCamera + view.treeOffset.xyz;
    vec2 range = CloudLayerRange(dir, cam.y);
    float nearStride = _CloudShape.y / 32.0;
    float t = range.x;
    for (int i = 0; i < 256 && t < range.y; i++)
    {
        float stride = max(nearStride, t * 0.005) * 3.0;
        float ground2, top;
        vec3 profile;
        vec3 q = CloudWindPoint(cam + dir * t, cam, ground2);
        if (CloudShape(q, profile, top) > 0.0 || CloudUnderTop(q, top, -dir.y * stride * _CloudShape.z))
        {
            outColor.x = min(outColor.x, t - stride * 2.0);
            outColor.y = t + stride * 2.0;
        }
        t += stride;
    }
    if (t < range.y)
        outColor.xy = vec2(min(outColor.x, t), 1e30);
}

#elif defined(CLOUD_SHADOW)

uniform vec3 _CloudCamera;
uniform vec4 _CloudShadowBuild;         // centre of the cascade drawn, absolute, and half its width
uniform vec3 _CloudShadowRight;         // across the shadow map, both square to the sun
uniform vec3 _CloudShadowUp;
uniform float _CloudShadowResolution;   // texels along each side of one cascade

// The cloud's density at a point the shadow map's ray reaches, the main lobes only as the light reads them
float CloudShadowDensity(vec3 p, vec3 cam, float lod)
{
    float g2;
    vec3 q = CloudWindPoint(p, cam, g2);
    vec3 profile;
    float shape = CloudShape(q, profile);
    return shape > 0.0 ? CloudDensity(q, shape, profile, lod, true) : 0.0;
}

// Marches the texel's ray from the sun down through the cloud layer, from a start that differs texel to texel so the
// steps do not band. Where it first meets cloud is narrowed down between the last empty step and the first in cloud.
// Distances along it are toward the sun from the cascade's plane through its centre
void main()
{
    vec2 local = vec2(mod(gl_FragCoord.x, _CloudShadowResolution), gl_FragCoord.y) / _CloudShadowResolution * 2.0 - 1.0;
    vec3 plane = _CloudShadowBuild.xyz + (_CloudShadowRight * local.x + _CloudShadowUp * local.y) * _CloudShadowBuild.w;
    vec3 cam = _CloudCamera;
    float sunY = max(_CloudToSun.y, 0.05);

    // The ground curves away under far clouds, so the march reaches below the layer's base by as much
    float across = length(plane.xz - cam.xz) + _CloudShape.y / sunY;
    float top = _CloudShape.x + _CloudShape.y, bottom = _CloudShape.x - across * across / (2.0 * _CloudShape.w);
    float from = (top - plane.y) / sunY, to = (bottom - plane.y) / sunY;

    const int steps = 48;
    float step = (from - to) / float(steps);
    float texel = 2.0 * _CloudShadowBuild.w / _CloudShadowResolution;
    float lod = max(log2(texel * _CloudPattern.z * _CloudErosionSize), 0.0);
    float jitter = fract(52.9829189 * fract(dot(gl_FragCoord.xy, vec2(0.06711056, 0.00583715))));
    float front = to, back = to, optical = 0.0;
    for (int i = 0; i < steps; i++)
    {
        float s = from - (float(i) + jitter) * step;
        float density = CloudShadowDensity(plane + _CloudToSun * s, cam, lod);
        if (density <= 0.0)
            continue;
        if (optical <= 0.0)
        {
            float above = min(s + step, from), below = s;
            for (int k = 0; k < 4; k++)
            {
                float middle = (above + below) * 0.5;
                if (CloudShadowDensity(plane + _CloudToSun * middle, cam, lod) > 0.0)
                    below = middle;
                else
                    above = middle;
            }
            front = above;
        }
        back = s - step * 0.5;
        optical += density * step;
    }
    outColor = vec4(front, optical / max(front - back, step), optical, 1.0);
}

#elif defined(CLOUD_MARCH)

#define CLOUD_MAX_LAYERS 4

layout(location = 1) out vec4 outDistance;

uniform GRAPHITE_SCREEN_TEXTURE _CloudSceneDepth;
uniform vec2 _CloudLowResolution;
uniform vec3 _CloudCamera;          // the first layer's camera, absolute
uniform vec4 _CloudLighting;        // extinction per meter, unused, ambient, silver lining
uniform vec3 _CloudSunColor;        // colour times intensity times sun brightness
uniform vec3 _CloudSkyLight;
uniform vec4 _CloudAlbedo;
uniform float _CloudAmbientSaturation;
uniform float _CloudHorizonFade;
uniform GRAPHITE_SCREEN_TEXTURE _CloudTileRange;
uniform vec2 _CloudTileResolution;
uniform float _CloudStepScale;      // how many times longer than full quality the march's steps are
uniform float _CloudFrame;          // counts frames for a march blended over frames, 0 otherwise

uniform sampler2D _CloudShadowMap;
uniform vec4 _CloudShadowCascade0, _CloudShadowCascade1, _CloudShadowCascade2, _CloudShadowCascade3; // centre, half width
uniform vec4 _CloudShadowWindA, _CloudShadowWindB;  // how far the wind has carried the clouds since each was drawn
uniform vec3 _CloudShadowRight;
uniform vec3 _CloudShadowUp;

// How much cloud the sunlight crosses to reach p, from the smallest cascade of the shadow map holding it, moved back
// by the wind since that cascade was drawn. Each texel's ray crosses its mean density from where it first meets cloud
// to p, up to all the cloud it crosses
float CloudShadowDepth(vec3 p)
{
    for (int c = 0; c < 4; c++)
    {
        vec4 cascade = c == 0 ? _CloudShadowCascade0 : c == 1 ? _CloudShadowCascade1 : c == 2 ? _CloudShadowCascade2 : _CloudShadowCascade3;
        vec2 wind = c == 0 ? _CloudShadowWindA.xy : c == 1 ? _CloudShadowWindA.zw : c == 2 ? _CloudShadowWindB.xy : _CloudShadowWindB.zw;
        vec3 rel = p - vec3(wind.x, 0.0, wind.y) - cascade.xyz;
        vec2 local = vec2(dot(rel, _CloudShadowRight), dot(rel, _CloudShadowUp)) / cascade.w;
        if (max(abs(local.x), abs(local.y)) < 0.98)
        {
            vec4 texel = textureLod(_CloudShadowMap, vec2((local.x * 0.5 + 0.5 + float(c)) * 0.25, local.y * 0.5 + 0.5), 0.0);
            return clamp((texel.x - dot(rel, _CloudToSun)) * texel.y, 0.0, texel.z);
        }
    }
    return 0.0;
}

vec3 CloudSun()
{
    return _CloudSunColor * smoothstep(-0.1, 0.05, _CloudToSun.y);
}

// Where along its first step a ray starts: interleaved gradient noise over the pixels, moved each frame so the frames
// blended together fill in between the steps
float CloudJitter(vec2 pixel, float frame)
{
    pixel += 5.588238 * mod(frame, 64.0);
    return fract(52.9829189 * fract(dot(pixel, vec2(0.06711056, 0.00583715))));
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

void main()
{
    Graphite_ViewData view = Graphite_PostView();
    outColor = vec4(0.0);
    outDistance = vec4(0.0);
    if (Graphite_PostOrthographic(view))
        return;

    float sceneDist = Graphite_Fetch(_CloudSceneDepth, ivec2(gl_FragCoord.xy), 0).r;
    vec2 uv = gl_FragCoord.xy / _CloudLowResolution;
    vec3 dir = normalize(Graphite_RelativeFromDepth(view, uv, 1.0));
    float pixelAngle = 2.0 / (max(abs(view.projection[1][1]), 1e-4) * _CloudLowResolution.y);
    vec3 cam = _CloudCamera + view.treeOffset.xyz;
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

    // The march covers where clouds lie along this pixel's block and the blocks around it
    ivec2 tile = ivec2(gl_FragCoord.xy / _CloudTileSize);
    ivec2 lastTile = ivec2(_CloudTileResolution) - 1;
    vec2 tileRange = vec2(1e30, -1e30);
    for (int i = 0; i < 9; i++)
    {
        vec2 around = Graphite_Fetch(_CloudTileRange, clamp(tile + ivec2(i % 3 - 1, i / 3 - 1), ivec2(0), lastTile), 0).rg;
        tileRange = vec2(min(tileRange.x, around.x), max(tileRange.y, around.y));
    }
    vec2 range = CloudLayerRange(dir, cam.y);
    float start = max(range.x, tileRange.x);
    float end = min(min(range.y, sceneDist), tileRange.y);

    vec3 ambientSky = mix(vec3(dot(_CloudSkyLight, vec3(0.2126, 0.7152, 0.0722))), _CloudSkyLight, _CloudAmbientSaturation) * _CloudLighting.z;
    float cosTheta = dot(dir, _CloudToSun);
    vec3 phases = CloudPhases(cosTheta, _CloudLighting.w);
    float powderFacing = cosTheta * 0.5 + 0.5;
    float extinction = _CloudLighting.x * 40.0;

    vec3 color = vec3(0.0);
    float transmittance = 1.0, distSum = 0.0, weightSum = 0.0;
    int nextLayer = 0;

    if (end > start)
    {
        // Every step is the same length at a distance, growing further out as a pixel covers more of the cloud there.
        // Only where the march starts moves, along its first step, so frames blended together fill in between steps
        float nearStride = _CloudShape.y / 32.0 * _CloudStepScale;
        float stride = max(nearStride, start * 0.005 * _CloudStepScale);
        float jitter = CloudJitter(floor(gl_FragCoord.xy), _CloudFrame);
        float t = start + stride * jitter;
        for (int i = 0; i < 256 && t < end && transmittance > 0.01; i++)
        {
            stride = max(nearStride, t * 0.005 * _CloudStepScale);
            for (; nextLayer < layers && layerT[nextLayer] <= t; nextLayer++)
            {
                float w = transmittance * layerColor[nextLayer].a;
                color += layerColor[nextLayer].rgb * w;
                distSum += layerT[nextLayer] * w;
                weightSum += w;
                transmittance *= 1.0 - layerColor[nextLayer].a;
            }

            vec3 p = cam + dir * t;
            float g2;
            vec3 q = CloudWindPoint(p, cam, g2);

            vec3 profile;
            float shape = CloudShape(q, profile);
            if (shape <= 0.0)
            {
                t += stride;
                continue;
            }

            float lod = max(log2(t * pixelAngle * _CloudPattern.z * _CloudErosionSize * 2.0), 0.0);
            float density = CloudDensity(q, shape, profile, lod, false);
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
            float transmitted = CloudScattering(CloudShadowDepth(p) * extinction, phases);
            float powder = mix(1.0 - exp(-sigma * 300.0), 1.0, powderFacing);
            vec3 ambient = ambientSky * mix(0.55, 1.0, sqrt(h));
            vec3 lit = _CloudAlbedo.rgb * (sun * transmitted * powder + ambient);

            float stepTransmittance = exp(-sigma * stride);
            float w = transmittance * (1.0 - stepTransmittance);
            color += lit * w;
            distSum += t * w;
            weightSum += w;
            transmittance *= stepTransmittance;
            t += stride;
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
    // How far the cloud the pixel shows is, 0 for clear sky
    outColor = vec4(color, alpha);
    outDistance = vec4(alpha > 0.01 ? distance : 0.0, 0.0, 0.0, 0.0);
}

#elif defined(CLOUD_TEMPORAL)

uniform GRAPHITE_SCREEN_TEXTURE _CloudCurrent;
uniform GRAPHITE_SCREEN_TEXTURE _CloudCurrentDistance;
uniform GRAPHITE_SCREEN_TEXTURE _CloudHistory;
uniform vec2 _CloudLowResolution;
uniform float _CloudHistoryValid;   // 1 when last frame's clouds can be blended over
uniform vec2 _CloudWindMoved;       // how far the wind carried the clouds since last frame

// This frame's march blended over last frame's result, read where this pixel's cloud was last frame by its distance,
// the camera's move and the wind's. Last frame's is kept within the colours around the pixel this frame, so clouds
// that changed or came into view leave no trails
void main()
{
    Graphite_ViewData view = Graphite_PostView();
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    ivec2 last = ivec2(_CloudLowResolution) - 1;
    vec4 current = Graphite_Fetch(_CloudCurrent, pixel, 0);
    outColor = current;
    if (_CloudHistoryValid < 0.5)
        return;

    float distance = Graphite_Fetch(_CloudCurrentDistance, pixel, 0).r;
    vec3 ray = normalize(Graphite_RelativeFromDepth(view, gl_FragCoord.xy / _CloudLowResolution, 1.0));
    vec3 shift = (view.cameraHigh.xyz - view.previousCameraHigh.xyz) + (view.cameraLow.xyz - view.previousCameraLow.xyz);
    vec3 point = ray * (distance > 0.0 ? distance : 1e6) + shift - vec3(_CloudWindMoved.x, 0.0, _CloudWindMoved.y);
    vec4 previous = view.previousViewProjection * vec4(point, 1.0);
    if (previous.w <= 0.0)
        return;
    vec2 uv = vec2(previous.x / previous.w * 0.5 + 0.5, 0.5 - previous.y / previous.w * 0.5);
    if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0))))
        return;

    vec4 low = current, high = current;
    for (int i = 0; i < 9; i++)
    {
        vec4 around = Graphite_Fetch(_CloudCurrent, clamp(pixel + ivec2(i % 3 - 1, i / 3 - 1), ivec2(0), last), 0);
        low = min(low, around);
        high = max(high, around);
    }
    outColor = mix(clamp(Graphite_Sample(_CloudHistory, uv), low, high), current, 0.15);
}

#else

uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
uniform GRAPHITE_SCREEN_TEXTURE _CloudTex;
uniform GRAPHITE_SCREEN_TEXTURE _CloudTexDistance; // how far the cloud each pixel shows is
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
        vec4 cloudColor = Graphite_Fetch(_CloudTex, pixel, 0);
        sum += cloudColor * weight;
        sumDistance += Graphite_Fetch(_CloudTexDistance, pixel, 0).r * 0.001 * cloudColor.a * weight;
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

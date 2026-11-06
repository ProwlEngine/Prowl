#pragma variant CLOUD COMPOSITE MAPS SHADOW MARCH TEMPORAL
// Volumetric clouds as fullscreen passes, one per define:
// CLOUD_MAPS       the weather over a square around the camera: how far each column's cloud rises from the layer's
//                  floor and hangs from its ceiling, and how high that ceiling is. Kept from frame to frame while the
//                  clouds are not changing shape,
//                  only the texels the square newly covers are drawn
// CLOUD_MARCH      the clouds and the high layer marched front to back at the clouds' resolution
// CLOUD_SHADOW     one cascade of the clouds' shadow map: along each texel's ray from the sun, where it first meets
//                  cloud, the cloud's mean density from there on and how much cloud the ray crosses in all
// CLOUD_TEMPORAL   the march blended over last frame's, moved to where the clouds are now, for a march in long steps
// otherwise        the clouds scaled up over the colour, matched to the scene's depth
// Two height maps shape the clouds: one rises from the layer's floor in domes over flat bases, the other hangs from its
// ceiling in domes under flat tops, the ceiling's height varying across the sky. Each grows with its own coverage, so
// clouds swell at their cores and pinch shut at their edges and the two layers' clouds sit in different places, and both
// drift through the 3D noise over time so the clouds change shape. The edges between cells of 3D noise carve lobes, billows and puffs into them.
// Cloud positions are absolute, the camera's from the host. Each ray's planet centre sits straight under the camera.
// The map rides the wind: a texel holds one point of the pattern for as long as it is in the square, wrapped around the
// map by its place on the wind's lattice, so the map is read with repeat at the position relative to the wind.

#include "Graphite/Internal/PostView.glsl"

uniform sampler2D _CloudCoverage;    // the weather: how far the cloud rises from the floor and hangs from the ceiling, 0 to 1,
                                     // and the ceiling's height in thicknesses
uniform sampler2D _CloudNoise2D;
uniform sampler3D _CloudEdges;       // cell edges
uniform sampler3D _CloudPuffs;       // finer cell edges
uniform sampler3D _CloudSlow;        // smooth noise, cell centres
uniform sampler3D _CloudLobeNoise;   // the two averaged
uniform vec4 _CloudCoverageRect;     // min x, min z, 1 / span, span
uniform vec4 _CloudShape;            // the layer's floor, how far up the highest ceiling is, 1 / that, planet radius
uniform vec4 _CloudPattern;          // 1 / weather scale, coverage, 1 / detail scale, erosion
uniform vec4 _CloudHeights;          // how far the clouds drift through the noise, how far through the layer they rise from
                                     // the floor and hang from the ceiling, how far in from a surface they reach full density
uniform vec2 _CloudCoverages;        // the floor's clouds' coverage, the ceiling's
uniform vec2 _CloudGap;              // the thickness, how many thicknesses up the ceiling is
uniform vec2 _CloudLobes;            // how far lobes push each cloud's domed side in and out, as a share of its height, and
                                     // 1 / their size
uniform vec2 _CloudWind;
uniform vec3 _CloudToSun;            // toward the sun, straight up when the scene has none
uniform float _CloudErosionSize;     // texels along each side of the detail noise
uniform float _CloudReach;           // how far out the clouds reach around the camera

// How deep the shell under where the shadow map's rays first meet cloud is, whose cloud the map keeps apart
#define CLOUD_SHELL 200.0

// How far a shadow map cascade's rays reach either side of its plane, a few of its widths and never past 80 kilometers,
// where farther cloud is the coarser cascades' to shade
float CloudShadowLimit(float halfWidth)
{
    return min(6.0 * halfWidth, 80000.0);
}

float CloudRemap(float value, float fromMin, float fromMax, float toMin, float toMax)
{
    return toMin + (value - fromMin) / (fromMax - fromMin) * (toMax - toMin);
}

// The tiling noise read twice, the second time turned and at an unrelated scale, so the weather built from it does not
// visibly repeat. Blending two reads narrows their spread, so it is widened back to one read's
vec2 CloudPatternNoise(vec2 q, float z)
{
    vec2 first = textureLod(_CloudSlow, vec3(q, z), 0.0).xy;
    vec2 second = textureLod(_CloudSlow, vec3(mat2(0.8, -0.6, 0.6, 0.8) * q * 0.618 + 0.17, z + 0.41), 0.0).xy;
    return clamp(0.5 + (mix(first, second, 0.4) - 0.5) / 0.72, 0.0, 1.0);
}

// The weather at a point relative to the wind, what the weather map stores. Each layer has its own clusters of cells
// rounded by smooth noise, cut by its own coverage, and how far past the cut each point is sets its coverage. The rise
// from the floor and the hang from the ceiling are each their own smooth noise times their coverage, so both rise
// toward the middle from each cloud's core. The ceiling sits the gap's thicknesses up. All of it
// drifts through the noise's third axis, so the clouds change shape
vec4 CloudWeatherNoise(vec2 windRel)
{
    vec2 q = windRel * _CloudPattern.x;
    float drift = _CloudHeights.x;
    vec2 lowClusters = CloudPatternNoise(q, 0.25 + drift);
    vec2 highClusters = CloudPatternNoise(q * 0.9 + vec2(0.53, 0.29), 0.62 + drift * 0.9);
    float low = clamp((lowClusters.y * 0.6 + lowClusters.x * 0.4 - (1.0 - _CloudCoverages.x)) / 0.3, 0.0, 1.0);
    float high = clamp((highClusters.y * 0.6 + highClusters.x * 0.4 - (1.0 - _CloudCoverages.y)) / 0.3, 0.0, 1.0);
    float rise = CloudPatternNoise(q * 2.1 + 0.37, 0.75 + drift * 1.3).x;
    float hang = CloudPatternNoise(q * 1.7 + 0.61, 0.5 + drift * 0.7).x;
    return vec4(low * mix(0.35, 1.0, rise), high * mix(0.35, 1.0, hang), _CloudGap.y, 1.0);
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

vec2 CloudMapUV(vec2 windRel)
{
    return windRel * _CloudCoverageRect.z;
}

#if defined(CLOUD_MAPS)
vec4 CloudWeatherAt(vec2 windRel) { return CloudWeatherNoise(windRel); }
#else
vec4 CloudWeatherAt(vec2 windRel) { return textureLod(_CloudCoverage, CloudMapUV(windRel), 0.0); }
#endif

// The cloud at a point before its detail, inside either height map: the one rising from the floor or the one hanging
// from the ceiling, each 0 at its flat side and its domed side and rising to 1 a set depth inside. Round cells of 3D
// noise blended with smooth noise push each domed side in and out by a share of its height, changing with height too,
// so clouds bulge into uneven lobes and overhangs rather than standing as smooth hills. The top clouds dome both ways
// from the ceiling, down by their full depth and up by half, so only the bottom clouds keep a flat side. The point is relative
// to the wind across, absolute in height, and footprint is how far apart the samples reading it are, so far ones read
// the lobes from coarser mips. Also gives how strongly the detail eats it there, strongest at the flat side and less
// toward the dome so domes stay round, and its height through the layer
float CloudShape(vec3 q, float footprint, out float bite, out float h)
{
    bite = 0.0;
    h = (q.y - _CloudShape.x) * _CloudShape.z;
    if (h <= 0.0 || h >= 1.0)
        return 0.0;
    vec3 weather = CloudWeatherAt(q.xz).xyz;
    float thickness = _CloudGap.x;
    float rise = weather.x * _CloudHeights.y * thickness, hang = weather.y * _CloudHeights.z * thickness;
    if (rise <= 0.0 && hang <= 0.0)
        return 0.0;
    float fromFloor = q.y - _CloudShape.x, fromCeiling = weather.z * thickness - fromFloor;

    // Lobes stretch a cloud by at most their share of its height either way, so a point beyond that from every surface
    // is clear of cloud or deep inside it whatever they do, and their noise is only read near a surface
    float most = 1.0 + _CloudLobes.x, least = 1.0 - _CloudLobes.x;
    float outer = max(min(fromFloor, rise * most - fromFloor), min(hang * most * 0.5 + fromCeiling, hang * most - fromCeiling));
    if (outer <= 0.0)
        return 0.0;
    float inner = max(min(fromFloor, rise * least - fromFloor), min(hang * least * 0.5 + fromCeiling, hang * least - fromCeiling));
    float lobes = 0.0;
    if (inner < _CloudHeights.w)
    {
        float lobeNoise = textureLod(_CloudLobeNoise, q * _CloudLobes.y, max(log2(footprint * _CloudLobes.y * _CloudErosionSize), 0.0)).r;
        lobes = (lobeNoise - 0.5) * 2.0 * _CloudLobes.x;
    }
    rise *= 1.0 + lobes;
    hang *= 1.0 + lobes;
    float lower = min(fromFloor, rise - fromFloor), upper = min(hang * 0.5 + fromCeiling, hang - fromCeiling);
    float inside = max(lower, upper);
    if (inside <= 0.0)
        return 0.0;
    float along = lower >= upper ? fromFloor / max(rise, 1.0) : abs(fromCeiling) / max(fromCeiling >= 0.0 ? hang : hang * 0.5, 1.0);
    bite = _CloudPattern.w * mix(1.0, 0.3, clamp(along, 0.0, 1.0));
    return clamp(inside / _CloudHeights.w, 0.0, 1.0);
}

// The cloud's density at a point: the edges between cells of the 3D noise eat into the shape by bite. Large cells carve
// the main lobes of a cloud, small ones billow over them and the finest give the billows their own small puffs, bent,
// mixed between sizes and stronger in some places than others by slow noise, so the billows differ across a cloud. Thin
// cloud thins faster still, so edges stay soft. Every size only takes away, so once one has eaten the cloud the finer
// ones are not read. For light only the main lobes are read, the billows and puffs taken at their average
float CloudDensity(vec3 q, float shape, float bite, float lod, bool forLight)
{
    vec3 uvw = q * _CloudPattern.z;
    vec2 slow = textureLod(_CloudSlow, uvw * 0.17 + vec3(0.31, 0.17, 0.53), lod).xy;
    uvw += (slow.x - 0.5) * 0.35;

    float large = textureLod(_CloudEdges, uvw * 0.3 + 0.7, max(lod - 1.7, 0.0)).r;
    float density = shape - large * large * mix(0.9, 1.6, slow.y) * bite;
    if (density <= 0.0)
        return 0.0;

    float billows = 0.3194;
    if (!forLight)
    {
        float base = textureLod(_CloudEdges, uvw, lod).r;
        float coarse = textureLod(_CloudEdges, uvw * 0.55 + 0.5, lod).r;
        float edges = mix(base, coarse, slow.y);
        float finerWeight = smoothstep(0.45, 0.75, slow.x) * (1.0 - slow.y);
        if (finerWeight > 0.0)
            edges = mix(edges, textureLod(_CloudEdges, mat3(0.8, 0.0, -0.6, 0.0, 1.0, 0.0, 0.6, 0.0, 0.8) * uvw * 1.8 + 0.3, lod + 1.0).r, finerWeight);
        billows = edges * edges;
    }
    density -= billows * mix(0.4, 1.7, smoothstep(0.2, 0.8, slow.x)) * bite;
    if (density <= 0.0)
        return 0.0;

    float puffs = forLight ? 0.3181 : pow(textureLod(_CloudPuffs, mat3(0.6, 0.8, 0.0, -0.8, 0.6, 0.0, 0.0, 0.0, 1.0) * uvw * 2.1 + 0.11, lod + 1.0).r, 2.0);
    density = clamp(density - puffs * mix(0.25, 0.8, slow.y) * bite, 0.0, 1.0);
    return density * smoothstep(0.0, 0.15, density);
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
    outColor = CloudWeatherNoise((vec2(lattice) + 0.5) * _CloudMapState.y);
}

#elif defined(CLOUD_SHADOW)

uniform vec3 _CloudCamera;
uniform vec4 _CloudShadowBuild;         // centre of the cascade drawn, absolute, and half its width across the sun
uniform float _CloudShadowBuildUp;      // half its height, toward the sun's side of the sky
uniform vec3 _CloudShadowRight;         // across the shadow map, both square to the sun
uniform vec3 _CloudShadowUp;
uniform float _CloudShadowResolution;   // texels along each side of one cascade

// The cloud's density at a point the shadow map's ray reaches, its detail at its average as the light reads it
float CloudShadowDensity(vec3 p, vec3 cam, float lod, float texel)
{
    float g2;
    vec3 q = CloudWindPoint(p, cam, g2);
    float bite, h;
    float shape = CloudShape(q, texel, bite, h);
    return shape > 0.0 ? CloudDensity(q, shape, bite, lod, true) : 0.0;
}

// Marches the texel's ray from the sun down through the cloud layer, from a start that differs texel to texel so the
// steps do not band. Where it first meets cloud is narrowed down between the last empty step and the first in cloud.
// Distances along it are toward the sun from the cascade's plane through its centre
void main()
{
    vec2 local = vec2(mod(gl_FragCoord.x, _CloudShadowResolution), gl_FragCoord.y) / _CloudShadowResolution * 2.0 - 1.0;
    vec3 plane = _CloudShadowBuild.xyz + _CloudShadowRight * local.x * _CloudShadowBuild.w + _CloudShadowUp * local.y * _CloudShadowBuildUp;
    vec3 cam = _CloudCamera;
    // The sun may sit a little either side of the horizon, so the ray runs through the layer from whichever side it
    // comes in, never quite level
    float sunY = (_CloudToSun.y >= 0.0 ? 1.0 : -1.0) * max(abs(_CloudToSun.y), 0.01);

    // A low sun's ray runs far through the layer, so it is kept within the cascade's limit either side of its plane. The
    // ground curves away under far clouds, so the march reaches below the layer's base by as much
    float limit = CloudShadowLimit(_CloudShadowBuild.w);
    float across = length(plane.xz - cam.xz) + min(_CloudShape.y / abs(sunY), limit);
    float top = _CloudShape.x + _CloudShape.y, bottom = _CloudShape.x - across * across / (2.0 * _CloudShape.w);
    float throughTop = (top - plane.y) / sunY, throughBottom = (bottom - plane.y) / sunY;
    float from = min(max(throughTop, throughBottom), limit), to = max(min(throughTop, throughBottom), -limit);
    if (from <= to)
    {
        outColor = vec4(to, 0.0, 0.0, 0.0);
        return;
    }

    // Steps about four texels long but no shorter than the light needs, so a long slanted ray is not stepped too coarsely
    float texel = 2.0 * _CloudShadowBuild.w / _CloudShadowResolution;
    int steps = int(clamp((from - to) / max(texel * 4.0, 150.0), 32.0, 160.0));
    float step = (from - to) / float(steps);
    float lod = max(log2(texel * _CloudPattern.z * _CloudErosionSize), 0.0);
    float jitter = fract(52.9829189 * fract(dot(gl_FragCoord.xy, vec2(0.06711056, 0.00583715))));
    float front = to, back = to, optical = 0.0, shell = 0.0;
    bool found = false;
    for (int i = 0; i < steps; i++)
    {
        float s = from - (float(i) + jitter) * step;
        float density = CloudShadowDensity(plane + _CloudToSun * s, cam, lod, texel);
        if (density <= 0.0)
            continue;
        if (!found)
        {
            found = true;
            float above = min(s + step, from), below = s;
            for (int k = 0; k < 4; k++)
            {
                float middle = (above + below) * 0.5;
                if (CloudShadowDensity(plane + _CloudToSun * middle, cam, lod, texel) > 0.0)
                    below = middle;
                else
                    above = middle;
            }
            front = above;
        }
        // Each step counts only the part of it below where the cloud starts, and the part within the shell just under
        // that is kept apart, as a cloud thins toward its surface
        float upper = min(s + step * 0.5, front), lower = s - step * 0.5;
        back = lower;
        optical += density * max(upper - lower, 0.0);
        shell += density * max(upper - max(lower, front - CLOUD_SHELL), 0.0);
    }
    float rest = max(front - CLOUD_SHELL - back, step);
    outColor = vec4(front, (optical - shell) / rest, optical, shell);
}

#elif defined(CLOUD_MARCH)

layout(location = 1) out vec4 outDistance;

uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
uniform vec2 _CloudFullResolution;
uniform vec2 _CloudLowResolution;
uniform vec3 _CloudCamera;          // the first layer's camera, absolute
uniform vec4 _CloudLighting;        // extinction per meter, unused, ambient, silver lining
uniform vec3 _CloudSunColor;        // colour times intensity times sun brightness
uniform vec3 _CloudSkyLight;
uniform vec4 _CloudAlbedo;
uniform float _CloudAmbientSaturation;
uniform float _CloudHorizonFade;
uniform float _CloudStepScale;      // how many times longer than full quality the march's steps are
uniform float _CloudFrame;          // counts frames for a march blended over frames, 0 otherwise

uniform sampler2D _CloudShadowMap;
uniform vec4 _CloudShadowCascade0, _CloudShadowCascade1, _CloudShadowCascade2, _CloudShadowCascade3; // centre, half width
uniform vec4 _CloudShadowHeights;       // each cascade's half height toward the sun's side of the sky
uniform vec4 _CloudShadowWindA, _CloudShadowWindB;  // how far the wind has carried the clouds since each was drawn
uniform vec3 _CloudShadowRight;
uniform vec3 _CloudShadowUp;
uniform float _CloudShadowResolution;

// How much cloud the sunlight crosses to reach p in one cascade of the shadow map, moved back by the wind since that
// cascade was drawn and lifted two of its texels toward the sun, as a texel cannot follow the billows on a cloud's
// surface and would shade the dips between them. The lift shrinks with the sun's height, as toward a low sun it would
// skip along the light's own path through the cloud, and how far out across the cascade p is, past 1 outside it or beyond
// where its rays reach toward or away from the sun. Each texel's ray crosses the cloud
// of the thin shell under where it first meets cloud in proportion, then its mean density below that, up to all the
// cloud it crosses. Four taps a texel either side soften it
// over about four texels
float CloudShadowCascade(int c, vec3 p, out float reach)
{
    vec4 cascade = c == 0 ? _CloudShadowCascade0 : c == 1 ? _CloudShadowCascade1 : c == 2 ? _CloudShadowCascade2 : _CloudShadowCascade3;
    vec2 wind = c == 0 ? _CloudShadowWindA.xy : c == 1 ? _CloudShadowWindA.zw : c == 2 ? _CloudShadowWindB.xy : _CloudShadowWindB.zw;
    vec3 rel = p - vec3(wind.x, 0.0, wind.y) - cascade.xyz + _CloudToSun * (4.0 * cascade.w / _CloudShadowResolution * max(_CloudToSun.y, 0.0));
    vec2 local = vec2(dot(rel, _CloudShadowRight) / cascade.w, dot(rel, _CloudShadowUp) / _CloudShadowHeights[c]);
    float along = dot(rel, _CloudToSun);
    reach = max(max(abs(local.x), abs(local.y)), abs(along) / CloudShadowLimit(cascade.w)) / 0.98;
    if (reach >= 1.0)
        return 0.0;
    vec2 uv = vec2((local.x * 0.5 + 0.5 + float(c)) * 0.25, local.y * 0.5 + 0.5);
    vec2 spread = vec2(0.25, 1.0) / _CloudShadowResolution;
    float depth = 0.0;
    for (int k = 0; k < 4; k++)
    {
        vec4 texel = textureLod(_CloudShadowMap, uv + vec2(k & 1, k >> 1) * 2.0 * spread - spread, 0.0);
        float d = texel.x - along;
        depth += clamp(d <= CLOUD_SHELL ? texel.w * d / CLOUD_SHELL : texel.w + (d - CLOUD_SHELL) * texel.y, 0.0, texel.z);
    }
    return depth * 0.25;
}

// How much cloud the sunlight crosses to reach p, from the smallest cascade holding it, fading into the next one over
// its outer fifth so the coarser cascade takes over without a seam
float CloudShadowDepth(vec3 p)
{
    for (int c = 0; c < 4; c++)
    {
        float reach;
        float depth = CloudShadowCascade(c, p, reach);
        if (reach >= 1.0)
            continue;
        float fade = smoothstep(0.8, 1.0, reach);
        if (fade > 0.0 && c < 3)
        {
            float outer;
            float next = CloudShadowCascade(c + 1, p, outer);
            if (outer < 1.0)
                depth = mix(depth, next, fade);
        }
        return depth;
    }
    return 0.0;
}

// Sunlight reaching the clouds, gone once the sun sinks far enough below the horizon that the planet hides it from them
vec3 CloudSun()
{
    return _CloudSunColor * smoothstep(-0.03, 0.02, _CloudToSun.y);
}

// Where along its first step a ray starts: interleaved gradient noise over the pixels, stepped by the golden ratio each
// frame so any few frames blended together spread evenly between the steps
float CloudJitter(vec2 pixel, float frame)
{
    return fract(52.9829189 * fract(dot(pixel, vec2(0.06711056, 0.00583715))) + frame * 0.6180339887);
}

uniform vec4 _CloudHigh;            // the high layer's altitude, opacity, 1 / scale, 1 when it is drawn
uniform vec2 _CloudHighWind;        // how far the wind has carried the high layer

// The high layer along a ray, where the ray meets its altitude: faint fBm of the 2D noise drifting with the wind, lit as
// a thin sheet by the sun and the sky. Gives how far along the ray it is
vec4 CloudHighLayer(vec3 cam, vec3 dir, float sceneDist, vec3 sun, out float t)
{
    t = CloudShellDistance(dir, cam.y, _CloudHigh.x);
    if (_CloudHigh.w < 0.5 || t <= 0.0 || t > sceneDist)
        return vec4(0.0);
    vec2 q = ((cam + dir * t).xz - _CloudHighWind) * _CloudHigh.z;
    float fbm = 0.0, amplitude = 0.5, total = 0.0;
    for (int o = 0; o < 4; o++)
    {
        fbm += texture(_CloudNoise2D, q + vec2(0.37, 0.71) * float(o)).r * amplitude;
        total += amplitude;
        amplitude *= 0.5;
        q = mat2(0.8, -0.6, 0.6, 0.8) * q * 2.03;
    }
    float alpha = smoothstep(0.4, 0.75, fbm / total) * _CloudHigh.y;
    if (alpha < 0.002)
        return vec4(0.0);
    float phase = mix(1.0 / (4.0 * 3.14159265), CloudHenyeyGreenstein(dot(dir, _CloudToSun), 0.75), _CloudLighting.w * 0.5);
    return vec4(_CloudAlbedo.rgb * (sun * phase + _CloudSkyLight * _CloudLighting.z), alpha);
}

void main()
{
    Graphite_ViewData view = Graphite_PostView();
    outColor = vec4(0.0);
    outDistance = vec4(0.0);
    if (Graphite_PostOrthographic(view))
        return;

    ivec2 full = min(ivec2(gl_FragCoord.xy / _CloudLowResolution * _CloudFullResolution), ivec2(_CloudFullResolution) - 1);
    float sceneDist = CloudSceneDistance(view, _CameraDepthTexture, full, _CloudFullResolution);
    vec2 uv = gl_FragCoord.xy / _CloudLowResolution;
    vec3 dir = normalize(Graphite_RelativeFromDepth(view, uv, 1.0));
    float pixelAngle = 2.0 / (max(abs(view.projection[1][1]), 1e-4) * _CloudLowResolution.y);
    vec3 cam = _CloudCamera + view.treeOffset.xyz;
    vec3 sun = CloudSun();

    float highT;
    vec4 high = CloudHighLayer(cam, dir, sceneDist, sun, highT);

    vec2 range = CloudLayerRange(dir, cam.y);
    float start = range.x;
    float end = min(range.y, sceneDist);

    vec3 ambientSky = mix(vec3(dot(_CloudSkyLight, vec3(0.2126, 0.7152, 0.0722))), _CloudSkyLight, _CloudAmbientSaturation) * _CloudLighting.z;
    float cosTheta = dot(dir, _CloudToSun);
    vec3 phases = CloudPhases(cosTheta, _CloudLighting.w);
    float powderFacing = cosTheta * 0.5 + 0.5;
    float extinction = _CloudLighting.x * 40.0;

    vec3 color = vec3(0.0);
    float transmittance = 1.0, distSum = 0.0, weightSum = 0.0;

    if (end > start)
    {
        // Every step is the same length at a distance, growing further out as a pixel covers more of the cloud there.
        // Only where the march starts moves, along its first step, so frames blended together fill in between steps
        float nearStride = _CloudShape.y / 32.0 * _CloudStepScale;
        float stride = max(nearStride, start * 0.005 * _CloudStepScale);
        float jitter = CloudJitter(floor(gl_FragCoord.xy), _CloudFrame);
        float t = start + stride * jitter;
        for (int i = 0; i < 256 && t < end && transmittance > 0.03; i++)
        {
            stride = max(nearStride, t * 0.005 * _CloudStepScale);
            if (high.a > 0.0 && highT <= t)
            {
                float w = transmittance * high.a;
                color += high.rgb * w;
                distSum += highT * w;
                weightSum += w;
                transmittance *= 1.0 - high.a;
                high.a = 0.0;
            }

            vec3 p = cam + dir * t;
            float g2;
            vec3 q = CloudWindPoint(p, cam, g2);

            float bite, h;
            float shape = CloudShape(q, t * pixelAngle * 2.0, bite, h);
            if (shape <= 0.0)
            {
                t += stride;
                continue;
            }

            float lod = max(log2(t * pixelAngle * _CloudPattern.z * _CloudErosionSize * 2.0), 0.0);
            float density = CloudDensity(q, shape, bite, lod, transmittance < 0.1);
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

    // A ray stopped in thick cloud takes the rest of it as the same colour as what it saw
    if (transmittance <= 0.03)
    {
        color /= 1.0 - transmittance;
        weightSum += transmittance;
        distSum += transmittance * distSum / max(weightSum - transmittance, 1e-6);
        transmittance = 0.0;
    }

    if (high.a > 0.0)
    {
        float w = transmittance * high.a;
        color += high.rgb * w;
        distSum += highT * w;
        weightSum += w;
        transmittance *= 1.0 - high.a;
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
    outColor = mix(clamp(Graphite_Sample(_CloudHistory, uv), low, high), current, 0.04);
}

#else

uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
uniform GRAPHITE_SCREEN_TEXTURE _CloudTex;
uniform GRAPHITE_SCREEN_TEXTURE _CloudTexDistance; // how far the cloud each pixel shows is
uniform vec2 _CloudLowResolution;

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
    float sumDistance = 0.0;
    for (int i = 0; i < 4; i++)
    {
        ivec2 offset = ivec2(i & 1, i >> 1);
        ivec2 pixel = clamp(first + offset, ivec2(0), last);
        float weight = (offset.x == 1 ? f.x : 1.0 - f.x) * (offset.y == 1 ? f.y : 1.0 - f.y);
        vec4 cloudColor = Graphite_Fetch(_CloudTex, pixel, 0);
        sum += cloudColor * weight;
        sumDistance += Graphite_Fetch(_CloudTexDistance, pixel, 0).r * 0.001 * cloudColor.a * weight;
    }

    // Wherever geometry is nearer than the cloud, the cloud fades out over 50 meters
    vec4 color = sum;
    float cloud = sumDistance / max(color.a, 1e-4) * 1000.0;
    float visible = color.a > 1e-3 ? clamp((own - cloud) / 50.0, 0.0, 1.0) : 1.0;
    color *= visible;
    outColor = vec4(scene.rgb * (1.0 - color.a) + color.rgb * view.exposure.x, scene.a);
}

#endif

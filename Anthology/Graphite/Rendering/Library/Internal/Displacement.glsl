#pragma variant DISPLACE FILL DEPTH
// The two fullscreen parts of a deferred view's screen space displacement, see DisplacementFeature. Fill puts the
// GBuffer's own normal and motion onto the scene's where the GBuffer holds a surface and keeps each pixel's height, in
// and the way it lifts, for the grid that Graphite/Internal/DisplacementGrid.glsl draws over them. The height fades to
// nothing where lifting would only alias: on surfaces seen dead edge on, where the lift is smaller than a pixel can
// show, and toward the screen's edge. The way it lifts is its normal blended across the hard creases near it, so a
// crease's two sides stay joined as they lift. Depth then puts the grid's depth into the scene's, where the grid drew.
#include "Graphite/Internal/PostView.glsl"
#include "Graphite/GBuffer.glsl"

#if defined(DISPLACE_FILL)
uniform GRAPHITE_SCREEN_TEXTURE _Surface;
uniform GRAPHITE_SCREEN_TEXTURE _Custom;
uniform GRAPHITE_SCREEN_TEXTURE _Normal;
uniform GRAPHITE_SCREEN_TEXTURE _Motion;
uniform GRAPHITE_SCREEN_TEXTURE _Depth;

// x the pixels over which the direction of the lift blends across a crease, 0 for no blend
uniform vec4 _Fade;

// How far in front of the camera a reversed Z depth lies, under either kind of projection
float DisplaceDistance(mat4 projection, float depth)
{
    return abs((projection[3][2] - depth * projection[3][3]) / (depth * projection[2][3] - projection[2][2]));
}

// The way a pixel lifts: its normal blended with those of its surface around it, nearer ones counting more, so the
// two sides of a hard crease lift the same way where they meet and stay joined. squarely is how squarely the surface
// faces the camera, which sets how far apart in depth its pixels can lie
vec3 DisplaceDirection(Graphite_ViewData view, ivec2 pixel, float distance, vec3 normal, float pixelSize, float squarely)
{
    int radius = int(_Fade.x);
    if (radius <= 0)
        return normal;
    const ivec2 directions[8] = ivec2[](ivec2(1, 0), ivec2(-1, 0), ivec2(0, 1), ivec2(0, -1), ivec2(1, 1), ivec2(-1, 1), ivec2(1, -1), ivec2(-1, -1));
    ivec2 last = ivec2(view.screen.xy) - 1;
    vec3 sum = normal;
    for (int d = 0; d < 8; d++)
    {
        for (int s = 1; s <= radius; s++)
        {
            ivec2 p = pixel + directions[d] * s;
            if (any(lessThan(p, ivec2(0))) || any(greaterThan(p, last)))
                break;
            float depth = Graphite_Fetch(_Depth, p, 0).r;
            if (depth <= 0.0 || abs(DisplaceDistance(view.projection, depth) - distance) > pixelSize * float(s) * 3.0 / max(squarely, 0.05) + 0.01)
                break;
            sum += Graphite_OctDecode(Graphite_Fetch(_Normal, p, 0).rg) * (1.0 - float(s) / float(radius + 1));
        }
    }
    return dot(sum, sum) > 1e-8 ? normalize(sum) : normal;
}

layout(location = 1) out vec2 outNormal;
layout(location = 2) out vec2 outMotion;
layout(location = 3) out vec4 outLift;

void main()
{
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    uint model = uint(Graphite_Fetch(_Surface, pixel, 0).a * 255.0 + 0.5) & 15u;
    if (model == GRAPHITE_MODEL_NONE)
        discard;
    outColor = vec4(0.0);
    outNormal = Graphite_Fetch(_Normal, pixel, 0).rg;
    outMotion = Graphite_Fetch(_Motion, pixel, 0).rg;
    vec3 normal = Graphite_OctDecode(outNormal);
    vec3 direction = normal;
    float height = model == GRAPHITE_MODEL_LIT ? Graphite_UnpackHeight(Graphite_Fetch(_Custom, pixel, 0).rg) : 0.0;
    if (height > 0.0)
    {
        Graphite_ViewData view = Graphite_PostView();
        vec2 uv = gl_FragCoord.xy * view.screen.zw;
        float depth = Graphite_Fetch(_Depth, pixel, 0).r;
        float distance = DisplaceDistance(view.projection, max(depth, 1e-7));
        float pixelSize = (Graphite_PostOrthographic(view) ? 1.0 : distance) * 2.0 / (abs(view.projection[1][1]) * view.screen.y);
        vec3 relative = Graphite_RelativeFromDepth(view, uv, depth);

        // Surfaces seen dead edge on, lifts under a third of a pixel and the screen's border keep none
        float squarely = abs(dot(normal, Graphite_PostTowardCamera(view, uv, relative)));
        height *= smoothstep(0.0, 0.1, squarely);
        height *= smoothstep(0.1, 0.3, height / pixelSize);
        vec2 border = min(gl_FragCoord.xy, view.screen.xy - gl_FragCoord.xy) / (0.01 * view.screen.y);
        height *= clamp(min(border.x, border.y), 0.0, 1.0);
        if (height > 0.0)
            direction = DisplaceDirection(view, pixel, distance, normal, pixelSize, squarely);
    }
    outLift = vec4(direction, height);
}
#elif defined(DISPLACE_DEPTH)
uniform GRAPHITE_SCREEN_TEXTURE _GridDepth;

void main()
{
    float depth = Graphite_Fetch(_GridDepth, ivec2(gl_FragCoord.xy), 0).r;
    if (depth <= 0.0)
        discard;
    outColor = vec4(0.0);
    gl_FragDepth = depth;
}
#else
void main() { outColor = vec4(0.0); }
#endif

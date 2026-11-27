// The contact shadows of a deferred view: how much of the sun still reaches each pixel of the GBuffer past what the
// view's own depth shows on the way, for the small gaps a shadow map misses. A short march from the pixel toward the
// sun, started at a different place each pixel and frame so temporal anti aliasing smooths it. 1 where the sun casts
// no shadow, the pixel takes none or faces away. See ContactShadows.
#include "Graphite/Internal/PostView.glsl"
#include "Graphite/Directional.glsl"
#include "Graphite/GBuffer.glsl"

uniform GRAPHITE_SCREEN_TEXTURE _Depth;
uniform GRAPHITE_SCREEN_TEXTURE _Normal;
uniform GRAPHITE_SCREEN_TEXTURE Graphite_GBufferSurface;
uniform GRAPHITE_SCREEN_TEXTURE Graphite_GBufferOffset;
uniform float Graphite_GBufferDisplaced;

// x how far a ray reaches as a part of the point's distance from the camera, y its steps, z how thick what the depth
// shows is taken to be, as a part of that distance
uniform vec4 _Settings;

// How far in front of the camera a reversed Z depth lies, under either kind of projection
float ContactDistance(mat4 projection, float depth)
{
    return abs((projection[3][2] - depth * projection[3][3]) / (depth * projection[2][3] - projection[2][2]));
}

// 0 where something the depth shows stands between a point and the light, else 1
float ContactMarch(Graphite_ViewData view, vec3 position, vec3 toLight)
{
    vec4 from = view.viewProjection * vec4(position, 1.0);
    float depth = ContactDistance(view.projection, from.z / from.w);
    float thickness = _Settings.z * depth;
    vec4 to = view.viewProjection * vec4(position + toLight * (_Settings.x * depth), 1.0);
    if (to.w <= 0.0)
        return 1.0;
    float steps = max(_Settings.y, 1.0);
    float start = fract(52.9829189 * fract(dot(gl_FragCoord.xy + view.time.z * 5.588238, vec2(0.06711056, 0.00583715))));
    for (float i = 0.0; i < steps; i++)
    {
        vec4 clip = mix(from, to, (i + start) / steps);
        vec3 ndc = clip.xyz / clip.w;
        vec2 uv = vec2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5);
        if (any(lessThan(uv, vec2(0.0))) || any(greaterThanEqual(uv, vec2(1.0))))
            return 1.0;
        float behind = ContactDistance(view.projection, ndc.z) - ContactDistance(view.projection, Graphite_Fetch(_Depth, ivec2(uv * view.screen.xy), 0).r);
        if (behind > thickness * 0.1 && behind < thickness)
            return 0.0;
    }
    return 1.0;
}

void main()
{
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    outColor = vec4(1.0);
    float depth = Graphite_Fetch(_Depth, pixel, 0).r;
    if (depth <= 0.0 || Graphite_DirectionalCount() == 0u)
        return;
    ivec2 source = pixel;
    if (Graphite_GBufferDisplaced > 0.5)
        source += ivec2(round(Graphite_Fetch(Graphite_GBufferOffset, pixel, 0).rg));
    uint surface = uint(Graphite_Fetch(Graphite_GBufferSurface, source, 0).a * 255.0 + 0.5);
    if ((surface & 15u) == GRAPHITE_MODEL_NONE || (surface & 128u) == 0u)
        return;
    Graphite_DirectionalLight sun = Graphite_GetDirectional(0u);
    if (!sun.shadow || dot(Graphite_OctDecode(Graphite_Fetch(_Normal, pixel, 0).rg), sun.direction) <= 0.0)
        return;
    Graphite_ViewData view = Graphite_PostView();
    outColor = vec4(ContactMarch(view, Graphite_RelativeFromDepth(view, gl_FragCoord.xy * view.screen.zw, depth), sun.direction));
}

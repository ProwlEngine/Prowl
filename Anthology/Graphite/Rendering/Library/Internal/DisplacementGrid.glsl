// The grid of a deferred view's screen space displacement: a quad for every cell of pixels, each corner at the surface
// the view's depth shows there pushed out along its normal by its height. Drawn with a depth test of its own over the
// scene's normal and motion, it leaves at each pixel the nearest lifted surface: its normal, turned as the lift
// tilts the surface there, its motion with what the lift adds to it as the camera moves, and how far away, in pixels,
// the pixel it came from lies, which the lighting reads the rest of the GBuffer at. A quad is left out where no
// corner is lifted. One that reaches past the edge of its surface has those corners brought in to the surface's last
// pixels, so the lift runs right up to the edge and the surface's outline moves with it. See DisplacementFeature.
#include "Graphite/Internal/PostView.glsl"

uniform GRAPHITE_SCREEN_TEXTURE _Depth;
// Each pixel's way of lifting in rgb, nothing off the GBuffer's surfaces, and its height in a
uniform GRAPHITE_SCREEN_TEXTURE _Height;
uniform GRAPHITE_SCREEN_TEXTURE _Normal;
uniform GRAPHITE_SCREEN_TEXTURE _Motion;

// x the pixels a cell spans, y the quads across
uniform vec4 _Grid;

#ifdef GRAPHITE_VERTEX
layout(location = 0) out vec2 vSource;
layout(location = 1) out vec3 vFlat;
layout(location = 2) out vec2 vParallax;
layout(location = 3) out vec3 vLifted;

// How far a point that stays put in the world moved across the screen since last frame, in uv
vec2 DisplaceCameraMotion(Graphite_ViewData view, vec3 relative)
{
    vec4 clip = view.viewProjection * vec4(relative, 1.0);
    vec2 previous = Graphite_PostPreviousUV(view, relative);
    if (clip.w <= 0.0 || previous.x < 0.0)
        return vec2(0.0);
    return vec2(clip.x / clip.w * 0.5 + 0.5, 0.5 - clip.y / clip.w * 0.5) - previous;
}

// How far in front of the camera a reversed Z depth lies, under either kind of projection
float DisplaceDistance(mat4 projection, float depth)
{
    return abs((projection[3][2] - depth * projection[3][3]) / (depth * projection[2][3] - projection[2][2]));
}

void main()
{
    // A quad as two triangles either way across: split between the corners 1 and 2, or between 0 and 3
    const int split12[6] = int[](0, 1, 2, 2, 1, 3);
    const int split03[6] = int[](0, 1, 3, 0, 3, 2);
    Graphite_ViewData view = Graphite_PostView();
    int cell = int(_Grid.x), across = int(_Grid.y);
    int quad = gl_VertexIndex / 6;
    ivec2 first = ivec2(quad % across, quad / across);
    ivec2 last = ivec2(view.screen.xy) - 1;

    // The corner that lifts most, whose surface the quad belongs to
    ivec2 pixels[4];
    float distances[4], heights[4];
    int tallest = 0;
    for (int i = 0; i < 4; i++)
    {
        pixels[i] = min((first + ivec2(i & 1, i >> 1)) * cell, last);
        float depth = Graphite_Fetch(_Depth, pixels[i], 0).r;
        distances[i] = depth > 0.0 ? DisplaceDistance(view.projection, depth) : -1.0;
        heights[i] = Graphite_Fetch(_Height, pixels[i], 0).a;
        if (heights[i] > heights[tallest])
            tallest = i;
    }
    if (heights[tallest] <= 0.0)
    {
        vSource = vec2(0.0);
        vFlat = vec3(0.0);
        vParallax = vec2(0.0);
        vLifted = vec3(0.0);
        gl_Position = vec4(2.0, 2.0, 0.0, 1.0);
        return;
    }

    // How far apart in depth the corners of one surface can lie: a cell's width in the world at the quad, over how
    // squarely the surface faces the camera there, so a surface seen at a steep angle still counts as one
    float reach = distances[tallest];
    float pixel = (Graphite_PostOrthographic(view) ? 1.0 : reach) * 2.0 / (abs(view.projection[1][1]) * view.screen.y);
    vec2 tallestUV = (vec2(pixels[tallest]) + 0.5) * view.screen.zw;
    vec3 tallestAt = Graphite_RelativeFromDepth(view, tallestUV, Graphite_Fetch(_Depth, pixels[tallest], 0).r);
    float squarely = abs(dot(Graphite_OctDecode(Graphite_Fetch(_Normal, pixels[tallest], 0).rg), Graphite_PostTowardCamera(view, tallestUV, tallestAt)));
    float gap = pixel * float(cell) * 2.0 / max(squarely, 0.05) + 0.01;

    // Split along the diagonal whose ends are nearer in height, so the edge of a wall in the relief runs along the
    // triangles' edges and not across them
    int corner = abs(heights[0] - heights[3]) < abs(heights[1] - heights[2]) ? split03[gl_VertexIndex % 6] : split12[gl_VertexIndex % 6];

    // A corner off the surface, past its edge, comes in to the last pixel of the surface toward the tallest corner, so
    // the lifted surface runs right up to its edge
    ivec2 p = pixels[corner];
    if (distances[corner] < 0.0 || abs(distances[corner] - reach) > gap)
    {
        ivec2 toward = sign(pixels[tallest] - p);
        for (int s = 0; s < cell; s++)
        {
            p += toward * ivec2(notEqual(p, pixels[tallest]));
            float depth = Graphite_Fetch(_Depth, p, 0).r;
            if (depth > 0.0 && abs(DisplaceDistance(view.projection, depth) - reach) <= gap)
                break;
        }
    }
    vec2 uv = (vec2(p) + 0.5) * view.screen.zw;
    vec3 position = Graphite_RelativeFromDepth(view, uv, Graphite_Fetch(_Depth, p, 0).r);
    vec4 lift = Graphite_Fetch(_Height, p, 0);
    vec3 normal = dot(lift.rgb, lift.rgb) > 1e-8 ? lift.rgb : Graphite_OctDecode(Graphite_Fetch(_Normal, p, 0).rg);
    vSource = vec2(p) + 0.5;
    vFlat = position;

    // The corner's height blended with its surface's around it, so relief finer than a cell lifts the grid smoothly,
    // not in steps. Where the lift jumps by more than the grid can follow, a wall in the relief, it blends over a
    // wider patch.
    float height = 0.0, weight = 0.0, lowest = 1e30, highest = 0.0;
    for (int i = 0; i < 4; i++)
    {
        vec4 near = Graphite_Fetch(_Height, clamp(p + ivec2(i & 1, i >> 1) - 1, ivec2(0), last), 0);
        float on = dot(near.rgb, near.rgb) > 1e-8 ? 1.0 : 0.0;
        height += near.a * on;
        weight += on;
        lowest = min(lowest, mix(lift.a, near.a, on));
        highest = max(highest, near.a * on);
    }
    height = weight > 0.0 ? height / weight : lift.a;
    float wall = smoothstep(0.5, 2.0, (highest - lowest) / pixel);
    if (wall > 0.0)
    {
        float wide = 0.0;
        weight = 0.0;
        for (int i = 0; i < 16; i++)
        {
            vec4 near = Graphite_Fetch(_Height, clamp(p + ivec2(i & 3, i >> 2) - 2, ivec2(0), last), 0);
            float on = dot(near.rgb, near.rgb) > 1e-8 ? 1.0 : 0.0;
            wide += near.a * on;
            weight += on;
        }
        if (weight > 0.0)
            height = mix(height, wide / weight, wall);
    }
    vec3 lifted = position + normal * height;
    vLifted = lifted;

    // A lifted point crosses the screen at another rate than the surface under it as the camera moves, which the
    // surface's own motion does not hold
    vParallax = DisplaceCameraMotion(view, lifted) - DisplaceCameraMotion(view, position);
    gl_Position = view.viewProjection * vec4(lifted, 1.0);
}
#else
layout(location = 0) in vec2 vSource;
layout(location = 1) in vec3 vFlat;
layout(location = 2) in vec2 vParallax;
layout(location = 3) in vec3 vLifted;

layout(location = 1) out vec2 outNormal;
layout(location = 2) out vec2 outMotion;

void main()
{
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    // Behind what the scene shows there by more than the grid's own error, so a quad barely lifted still owns its pixels
    if (gl_FragCoord.z < Graphite_Fetch(_Depth, pixel, 0).r * 0.998)
        discard;
    ivec2 source = ivec2(vSource);
    outColor = vec4(vec2(source - pixel), 0.0, 0.0);

    // The surface's own normal turned by how the lift tilts the surface there: the slope of the heights around the
    // place it came from, blended between pixels, which stays smooth where the grid's own triangles are not and along
    // a wall in the relief, whose few pixels of heights a quad stretches over many
    vec3 normal = Graphite_OctDecode(Graphite_Fetch(_Normal, source, 0).rg);
    vec3 across = dFdx(vFlat), down = dFdy(vFlat);

    // The surface's steps per pixel of where it came from, not of where the lift put it, which a stretched quad differs in
    mat2 stretch = mat2(dFdx(vSource), dFdy(vSource));
    float kept = abs(determinant(stretch));
    if (kept > 1e-4)
    {
        mat2 back = inverse(stretch);
        vec3 x = across, y = down;
        across = x * back[0][0] + y * back[0][1];
        down = x * back[1][0] + y * back[1][1];
    }
    vec3 resting = cross(across, down);
    vec2 texel = Graphite_PostView().screen.zw;
    vec2 at = vSource * texel;
    float a = Graphite_Sample(_Height, at + vec2(-1.5, -1.5) * texel).a, b = Graphite_Sample(_Height, at + vec2(1.5, -1.5) * texel).a;
    float c = Graphite_Sample(_Height, at + vec2(-1.5, 1.5) * texel).a, d = Graphite_Sample(_Height, at + vec2(1.5, 1.5) * texel).a;
    vec2 slope = vec2((b + d) - (a + c), (c + d) - (a + b)) / 6.0;
    vec3 lifted = cross(across + normal * slope.x, down + normal * slope.y);
    if (dot(lifted, lifted) > 1e-24 && dot(resting, resting) > 1e-24)
    {
        float facing = dot(resting, normal) < 0.0 ? -1.0 : 1.0;
        normal = normalize(normal + facing * (normalize(lifted) - normalize(resting)));
    }

    // A quad stretched over a wall in the relief shows a pixel or two of the surface many times over, too little to
    // tell the wall's slope from, so the more it is stretched the more it takes the grid's own slope there
    vec3 wall = cross(dFdx(vLifted), dFdy(vLifted));
    if (dot(wall, wall) > 1e-24)
    {
        Graphite_ViewData view = Graphite_PostView();
        wall = normalize(wall);
        wall *= dot(wall, Graphite_PostTowardCamera(view, gl_FragCoord.xy * view.screen.zw, vLifted)) < 0.0 ? -1.0 : 1.0;
        normal = normalize(mix(wall, normal, smoothstep(0.1, 0.4, kept)));
    }
    outNormal = Graphite_OctEncode(normal);
    outMotion = Graphite_Fetch(_Motion, source, 0).rg + vParallax;
}
#endif

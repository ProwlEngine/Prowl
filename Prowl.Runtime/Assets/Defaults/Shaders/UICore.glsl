// GameObject UI: the RectMask clip and SDF text coverage every UI shader shares, and the material passes world space
// canvases draw with. A material shader defines PROWL_UI_MATERIAL, and PROWL_UI_TEXT for the glyph atlas.
#pragma once

#ifdef GRAPHITE_FRAGMENT
// Coverage of a point in a RectMask's local space against its rect (minX, minY, maxX, maxY), rounded corners and soft edge
float Prowl_UIClip(vec2 p, vec4 rect, float radius, float softness)
{
    vec2 c = (rect.xy + rect.zw) * 0.5;
    vec2 e = (rect.zw - rect.xy) * 0.5 - vec2(radius);
    vec2 d = abs(p - c) - e;
    float dist = length(max(d, vec2(0.0))) + min(max(d.x, d.y), 0.0) - radius;
    float soft = max(softness, max(fwidth(dist), 1e-4));
    return clamp(0.5 - dist / soft, 0.0, 1.0);
}

// Coverage of a glyph from the single channel distance field atlas, sharp at any scale. Taken per axis then the smaller,
// so a stretched underline keeps the gradient across its thin edges.
float Prowl_SdfCoverage(float field, vec2 uv, vec2 atlasSize)
{
    vec2 range = vec2(4.0) / atlasSize / max(fwidth(uv), vec2(1e-8));
    float screenPxRange = max(min(range.x, range.y), 1.0);
    return clamp(screenPxRange * (field - 0.5) + 0.5, 0.0, 1.0);
}
#endif

#ifdef PROWL_UI_MATERIAL

#ifdef GRAPHITE_VERTEX
#include "Graphite/Vertex.glsl"
#endif

uniform sampler2D _MainTex;
uniform vec4 _MainColor;
uniform vec2 _Tiling;
uniform vec2 _Offset;

// The clip position in the mask's space rides in UV1, the mask's rect in custom0, radius, softness and enable in custom1
#ifdef GRAPHITE_VERTEX
out vec2 vUV;
out vec4 vColor;
out vec2 vClip;
flat out vec4 vClipRect;
flat out vec4 vClipParams;

void main()
{
    gl_Position = Graphite_ObjectToClip(vertexPosition);
    vUV = vertexUV0 * _Tiling + _Offset;
    vColor = vertexColor;
    vClip = vertexUV1;
    Graphite_ObjectData object = Graphite_Object();
    vClipRect = object.custom0;
    vClipParams = object.custom1;
}
#else
in vec2 vUV;
in vec4 vColor;
in vec2 vClip;
flat in vec4 vClipRect;
flat in vec4 vClipParams;
layout(location = 0) out vec4 finalColor;

void main()
{
    vec4 color = vColor * _MainColor;
#ifdef PROWL_UI_TEXT
    color.a *= Prowl_SdfCoverage(texture(_MainTex, vUV).r, vUV, vec2(textureSize(_MainTex, 0)));
#else
    color *= texture(_MainTex, vUV);
#endif
    float clip = Prowl_UIClip(vClip, vClipRect, vClipParams.x, vClipParams.y);
    color.a *= vClipParams.z > 0.5 ? clip : 1.0;
    finalColor = color;
}
#endif

#endif

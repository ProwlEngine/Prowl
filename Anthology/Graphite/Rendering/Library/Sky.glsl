// Sky passes, tagged "LightMode" = "Sky": a fullscreen triangle at the far plane, drawn after the opaque surfaces where
// nothing was drawn and into the sky probe. Both stages include this. The vertex stage's main runs after the library's
// and has nothing left to do, the fragment stage reads Graphite_SkyDirection(). Include it before any other library
// file: a sky has no object, so object data is left out and surfaces count as on every layer.
#pragma once

#define GRAPHITE_NO_OBJECT
#include "Graphite/Core.glsl"

#ifdef GRAPHITE_VERTEX
layout(location = 29) out vec2 graphite_SkyClip;
layout(location = 31) flat out uint graphite_MaterialSlot;

void graphite_ShaderMain();

void main()
{
    vec2 p = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
    graphite_SkyClip = p * 2.0 - 1.0;
    graphite_MaterialSlot = graphite_Push.instanceBase;
    gl_Position = vec4(graphite_SkyClip, 0.0, 1.0);
    graphite_ShaderMain();
}

#define main graphite_ShaderMain
#endif

#ifdef GRAPHITE_FRAGMENT
layout(location = 29) in vec2 graphite_SkyClip;

// The unit world direction the pixel looks along
vec3 Graphite_SkyDirection() { return Graphite_ViewRay(graphite_SkyClip); }
#endif

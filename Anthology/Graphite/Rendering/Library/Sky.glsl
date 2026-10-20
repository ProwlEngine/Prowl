// Sky passes, tagged "LightMode" = "Sky": a fullscreen triangle at the far plane, drawn after the opaque surfaces where
// nothing was drawn and into the sky probe. Both stages include this. The vertex stage's main runs after the library's
// and has nothing left to do, the fragment stage reads Graphite_SkyDirection(). Include it before any other library
// file: a sky has no object, so object data is left out and surfaces count as on every layer.
#pragma once

#define GRAPHITE_NO_OBJECT
#include "Graphite/Core.glsl"

#ifdef GRAPHITE_VERTEX
layout(location = 27) out vec2 graphite_SkyClip;
layout(location = 29) flat out uint graphite_MaterialSlot;

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
layout(location = 27) in vec2 graphite_SkyClip;

// Vertical field of view the sky takes for orthographic views
#define GRAPHITE_SKY_ORTHO_FOV 1.0471975512

// The unit world direction the pixel looks along. An orthographic view has no directions, every ray through it is
// parallel, so the sky is seen through a perspective of the same aspect instead.
vec3 Graphite_SkyDirection()
{
    Graphite_ViewData view = Graphite_View();
    if (view.projection[2][3] != 0.0)
        return Graphite_ViewRay(graphite_SkyClip);

    float aspect = view.projection[1][1] / max(1e-6, view.projection[0][0]);
    float f = 1.0 / tan(GRAPHITE_SKY_ORTHO_FOV * 0.5);
    vec3 ray = normalize(vec3(graphite_SkyClip.x * aspect / f, graphite_SkyClip.y / f, 1.0));
    return normalize(transpose(mat3(view.view)) * ray);
}
#endif

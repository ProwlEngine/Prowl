// Fullscreen programs: the triangle in the vertex stage, the source colour and its sampling in the fragment stage, layered views through multiview.
// Procedural programs (GRAPHITE_PROCEDURAL) write their own vertex stage and varyings, making geometry from gl_VertexIndex.
#pragma variant GRAPHITE_LAYERED
#pragma variant GRAPHITE_NO_SOURCE
#ifdef GRAPHITE_LAYERED
#extension GL_EXT_multiview : require
#endif

// Textures the size of the view are arrays on layered views, each layer read at its own view index
#ifdef GRAPHITE_LAYERED
#define GRAPHITE_SCREEN_TEXTURE sampler2DArray
#define Graphite_Sample(t, uv) texture(t, vec3(uv, float(gl_ViewIndex)))
#define Graphite_SampleLod(t, uv, lod) textureLod(t, vec3(uv, float(gl_ViewIndex)), lod)
#define Graphite_Fetch(t, p, lod) texelFetch(t, ivec3(p, gl_ViewIndex), lod)
#define Graphite_Size(t, lod) textureSize(t, lod).xy
#define GRAPHITE_LAYER int(gl_ViewIndex)
#else
#define GRAPHITE_SCREEN_TEXTURE sampler2D
#define Graphite_Sample(t, uv) texture(t, uv)
#define Graphite_SampleLod(t, uv, lod) textureLod(t, uv, lod)
#define Graphite_Fetch(t, p, lod) texelFetch(t, p, lod)
#define Graphite_Size(t, lod) textureSize(t, lod)
#define GRAPHITE_LAYER 0
#endif

#ifdef GRAPHITE_VERTEX
#ifndef GRAPHITE_PROCEDURAL
layout(location = 0) out vec2 vUV;

void main()
{
    // uv runs down the image as texture rows do, clip y up, as Graphite's viewports put clip y 1 on the first row
    vec2 p = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
    vUV = vec2(p.x, 1.0 - p.y);
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}
#endif
#else
#ifndef GRAPHITE_PROCEDURAL
layout(location = 0) in vec2 vUV;
#endif
layout(location = 0) out vec4 outColor;

#ifndef GRAPHITE_NO_SOURCE
uniform GRAPHITE_SCREEN_TEXTURE Graphite_Source;
vec4 Graphite_SampleSource(vec2 uv) { return Graphite_Sample(Graphite_Source, uv); }
#endif
#endif

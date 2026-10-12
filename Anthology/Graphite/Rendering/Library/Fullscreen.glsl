// Fullscreen programs: the triangle in the vertex stage, the source colour and its sampling in the fragment stage, layered views through multiview.
#ifdef GRAPHITE_LAYERED
#extension GL_EXT_multiview : require
#endif

#ifdef GRAPHITE_VERTEX
layout(location = 0) out vec2 vUV;

void main()
{
    vec2 p = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
    vUV = vec2(p.x, 1.0 - p.y);
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}
#else
layout(location = 0) in vec2 vUV;
layout(location = 0) out vec4 outColor;

#ifndef GRAPHITE_NO_SOURCE
#ifdef GRAPHITE_LAYERED
uniform sampler2DArray Graphite_Source;
vec4 Graphite_SampleSource(vec2 uv) { return texture(Graphite_Source, vec3(uv, float(gl_ViewIndex))); }
#else
uniform sampler2D Graphite_Source;
vec4 Graphite_SampleSource(vec2 uv) { return texture(Graphite_Source, uv); }
#endif
#endif
#endif

#version 460
#extension GL_GOOGLE_cpp_style_line_directive : require
#line 5 "Graphite/Internal/Prefilter.glsl"

    layout(location = 0) out vec2 vUV;

    void main()
    {
        vec2 p = vec2( (gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
        vUV = p;
        gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
    }

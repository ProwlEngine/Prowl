#version 460
#extension GL_GOOGLE_include_directive : enable
#line 1 "Graphite/GodRays"

#extension GL_GOOGLE_cpp_style_line_directive : require
#line 2 "Graphite/GodRays"

#line 1 "Graphite/Fullscreen.glsl"


#pragma variantGRAPHITE_LAYERED
#pragma variantGRAPHITE_NO_SOURCE























layout(location = 0) out vec2 vUV;

void main()
{

    vec2 p = vec2( (gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
    vUV = vec2(p.x, 1.0 - p.y);
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}












#line 4 "Graphite/GodRays"


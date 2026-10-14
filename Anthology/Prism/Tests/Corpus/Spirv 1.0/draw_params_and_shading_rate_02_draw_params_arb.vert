#version 460
#extension GL_ARB_shader_draw_parameters : require
layout(location=0) out float o;
void main() { o = float(gl_BaseVertexARB + gl_BaseInstanceARB + gl_DrawIDARB); gl_Position = vec4(0.0); }

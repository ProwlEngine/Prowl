#version 320 es
precision mediump float;
layout(vertices=3) out;
layout(location=0) in vec2 uv[];
layout(location=0) out vec2 uvOut[];
void main(){
    float l = uv[gl_InvocationID].x * 4.0;
    gl_TessLevelInner[0] = l;
    gl_TessLevelOuter[gl_InvocationID] = l + 1.0;
    gl_out[gl_InvocationID].gl_Position = gl_in[gl_InvocationID].gl_Position * l;
    gl_out[gl_InvocationID].gl_PointSize = gl_in[gl_InvocationID].gl_PointSize;
    uvOut[gl_InvocationID] = uv[gl_InvocationID] * l;
}

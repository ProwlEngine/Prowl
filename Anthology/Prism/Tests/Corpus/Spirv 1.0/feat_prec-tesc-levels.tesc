#version 450
layout(vertices=3) out;
layout(location=0) in mediump float lod[];
void main(){
    mediump float l = lod[gl_InvocationID] * 2.0;
    gl_TessLevelInner[0] = l;
    gl_TessLevelOuter[gl_InvocationID] = float(gl_InvocationID) + l;
    gl_out[gl_InvocationID].gl_Position = gl_in[gl_InvocationID].gl_Position * l;
}

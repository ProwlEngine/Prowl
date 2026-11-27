#version 310 es
precision mediump float;
layout(location=0) in vec4 p;
layout(location=1) in highp vec4 q;
layout(location=0) out vec2 uv;
layout(location=1) out highp float depth;
void main(){
    vec4 w = p * q;
    gl_Position = w;
    uv = p.xy + vec2(gl_VertexIndex);
    depth = q.z * float(gl_InstanceIndex);
}

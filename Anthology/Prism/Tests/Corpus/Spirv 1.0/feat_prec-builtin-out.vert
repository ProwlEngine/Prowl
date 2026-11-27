#version 450
layout(location=0) in vec4 p;
layout(location=1) in mediump vec4 q;
void main(){
    mediump vec4 m = q * 0.5;
    gl_Position = m;
    gl_PointSize = m.x;
    gl_Position.y += 1.0;
}
